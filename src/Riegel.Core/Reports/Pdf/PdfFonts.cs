// SPDX-License-Identifier: MIT
using System.Collections.Concurrent;
using PdfSharp.Fonts;
using Riegel.Core.Model;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Schriften der PDF-Dokumente (Muster aus Zutritt, um CJK erweitert; spec 7.10). Der plattformneutrale PDFsharp-Build
/// bringt keine Schriften mit und verlangt einen Resolver, der genau einmal pro Prozess gesetzt werden darf – deshalb
/// hier zentral, gesperrt und nur, wenn noch niemand einen eigenen gesetzt hat. Riegel liefert keine Schriftdateien aus.
/// <list type="bullet">
/// <item>Text: die erste gefundene Schrift aus <see cref="LatinCandidates"/> (Windows Segoe UI, Linux Liberation Sans oder
/// DejaVu Sans). Jede angefragte Familie außer der CJK-Familie wird auf sie abgebildet – so scheitert auch MigraDocs
/// interne Ersatzschrift nicht.</item>
/// <item>Chinesisch (zh-Hans): die erste Schrift aus <see cref="CjkCandidates"/> mit TrueType-Konturen und den
/// Zeichen „中文“. Sammlungen (.ttc) werden im Speicher aufgeschnitten (<see cref="Sfnt"/>); Schriften mit CFF-Konturen
/// (Noto Sans CJK, Source Han Sans) bettet PDFsharp 6.2.4 nicht korrekt ein und werden übersprungen. Gibt es keine, ist
/// <see cref="CjkFamily"/> null und die Dokumente entstehen auf Englisch mit RGL-0510.</item>
/// </list>
/// </summary>
internal sealed class PdfFonts : IFontResolver
{
    /// <summary>Eine Schriftfamilie mit den Dateinamen ihrer Schnitte (ohne Ordner).</summary>
    internal sealed record FaceSet(string Family, string Regular, string? Bold, string? Italic, string? BoldItalic);

    /// <summary>Gefundene Schrift: Familie und die vollständigen Pfade der vorhandenen Schnitte, Schlüssel = Dateiname.</summary>
    internal sealed record Found(FaceSet Faces, IReadOnlyDictionary<string, string> Files);

    internal static readonly FaceSet[] LatinCandidates =
    [
        new("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"),
        // macOS (Entwicklung, Prüfer): Arial und Verdana liegen dort unter anderen Dateinamen; nur unter macOS, damit unter
        // Linux mit msttcorefonts weiterhin der vollständige Satz mit den Windows-Namen gewinnt
        .. (OperatingSystem.IsMacOS() ? MacCandidates : []),
        new("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        new("Tahoma", "tahoma.ttf", "tahomabd.ttf", null, null),
        new("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf"),
        // Liberation Sans (Maße wie Arial) vor DejaVu Sans: näher an den Breiten von Segoe UI, auf die die Spalten abgestimmt sind
        new("Liberation Sans", "LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
        new("DejaVu Sans", "DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
    ];

    private static FaceSet[] MacCandidates =>
    [
        new("Arial", "Arial.ttf", "Arial Bold.ttf", "Arial Italic.ttf", "Arial Bold Italic.ttf"),
        new("Verdana", "Verdana.ttf", "Verdana Bold.ttf", "Verdana Italic.ttf", "Verdana Bold Italic.ttf"),
    ];

    /// <summary>
    /// Kandidaten für vereinfachtes Chinesisch in dieser Reihenfolge. Windows: Microsoft YaHei (msyh.ttc, Sammlung mit
    /// „Microsoft YaHei“ und „Microsoft YaHei UI“), DengXian, SimHei, SimSun. Linux: WenQuanYi Micro Hei (Paket
    /// fonts-wqy-microhei), WenQuanYi Zen Hei, Droid Sans Fallback, AR PL UMing. Die Noto-/Source-Han-Schriften stehen am
    /// Ende, damit die Prüfung sie sichtbar ablehnt (CFF), statt sie stillschweigend zu übergehen.
    /// </summary>
    internal static readonly FaceSet[] CjkCandidates =
    [
        new("Microsoft YaHei", "msyh.ttc", "msyhbd.ttc", null, null),
        new("DengXian", "Deng.ttf", "Dengb.ttf", null, null),
        new("SimHei", "simhei.ttf", null, null, null),
        new("SimSun", "simsun.ttc", null, null, null),
        new("WenQuanYi Micro Hei", "wqy-microhei.ttc", null, null, null),
        new("WenQuanYi Zen Hei", "wqy-zenhei.ttc", null, null, null),
        new("Droid Sans Fallback", "DroidSansFallbackFull.ttf", null, null, null),
        new("AR PL UMing CN", "uming.ttc", null, null, null),
        new("Noto Sans CJK SC", "NotoSansCJK-Regular.ttc", "NotoSansCJK-Bold.ttc", null, null),
        new("Source Han Sans SC", "SourceHanSansSC-Regular.otf", "SourceHanSansSC-Bold.otf", null, null),
    ];

    /// <summary>Zeichen, die eine CJK-Schrift haben muss („中文“, „报告“).</summary>
    private static readonly int[] CjkProbe = [0x4E2D, 0x6587, 0x62A5, 0x544A];

    private static readonly Lock Gate = new();
    private static PdfFonts? _installed;
    private static string? _foreignFamily;
    private static Found? _latin;
    private static bool _latinSearched;
    private static CjkFont? _cjk;
    private static bool _cjkSearched;
    private static readonly List<string> CjkLog = [];

    private readonly Found _fonts;
    private readonly ConcurrentDictionary<string, byte[]> _cache = new(StringComparer.OrdinalIgnoreCase);

    private PdfFonts(Found fonts) => _fonts = fonts;

    /// <summary>Herausgelöste CJK-Schnitte (regulär, fett falls vorhanden).</summary>
    private sealed record CjkFont(string Family, byte[] Regular, byte[]? Bold);

    /// <summary>Schriftfamilie für lateinischen Text; der erste Aufruf richtet den Resolver ein.</summary>
    /// <exception cref="RiegelException">RGL-0501: in den Schriftordnern gibt es keine der Schriften.</exception>
    public static string LatinFamily
    {
        get
        {
            lock (Gate)
            {
                if (_installed is not null) return _installed._fonts.Faces.Family;
                if (_foreignFamily is not null) return _foreignFamily;
                var current = GlobalFontSettings.FontResolver;
                if (current is null)
                {
                    var found = SearchLatin() ?? throw new RiegelException(RglCodesPdf.NoFont, RglCodesPdf.Message(RglCodesPdf.NoFont,
                        string.Join(", ", LatinCandidates.Select(c => c.Family)), string.Join(", ", Directories())));
                    var resolver = new PdfFonts(found);
                    try
                    {
                        GlobalFontSettings.FontResolver = resolver;
                        _installed = resolver;
                        return found.Faces.Family;
                    }
                    catch (InvalidOperationException)
                    {
                        // PDFsharp wurde in diesem Prozess schon ohne Resolver benutzt – dann gilt der vorhandene Zustand
                    }
                }
                // Fremder Resolver (z. B. UseWindowsFontsUnderWindows): Arial kennt praktisch jeder; CJK gibt es dann nicht
                return _foreignFamily = "Arial";
            }
        }
    }

    /// <summary>
    /// Familie für Dokumente auf Chinesisch, null = auf diesem System keine geeignete Schrift (dann Englisch mit RGL-0510).
    /// Liest die Schriftdatei beim ersten Aufruf (bis etwa 20 MB) und behält nur die herausgelösten Schnitte.
    /// </summary>
    public static string? CjkFamily
    {
        get
        {
            _ = LatinFamily;
            lock (Gate)
            {
                if (_installed is null) return null;
                if (!_cjkSearched)
                {
                    _cjkSearched = true;
                    _cjk = SearchCjk();
                }
                return _cjk?.Family;
            }
        }
    }

    /// <summary>Was die CJK-Suche geprüft hat (für Tests und die Doku: welche Datei, warum verworfen).</summary>
    public static IReadOnlyList<string> CjkSearchLog
    {
        get
        {
            _ = CjkFamily;
            lock (Gate) return [.. CjkLog];
        }
    }

    /// <summary>Gibt es auf diesem System eine Schrift für lateinischen Text? (Tests überspringen PDF-Tests sonst.)</summary>
    public static bool IsAvailable
    {
        get
        {
            lock (Gate) return _installed is not null || GlobalFontSettings.FontResolver is { } and not PdfFonts || SearchLatin() is not null;
        }
    }

    /// <summary>Familie, die die Dokumente auf diesem System nähmen (ohne den Resolver einzurichten), null = keine.</summary>
    public static string? AvailableLatinFamily
    {
        get
        {
            lock (Gate) return SearchLatin()?.Faces.Family;
        }
    }

    /// <summary>
    /// Ordner, in denen gesucht wird: der Schriftordner des Systems (unter Windows %WINDIR%\Fonts), der Schriftordner des
    /// Benutzers unter Windows, dann die üblichen Ordner unter Linux und macOS. Unterordner werden mit durchsucht.
    /// </summary>
    internal static IEnumerable<string> Directories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string?[] all =
        [
            system,
            windows.Length > 0 ? Path.Combine(windows, "Fonts") : null,
            OperatingSystem.IsWindows() && local.Length > 0 ? Path.Combine(local, "Microsoft", "Windows", "Fonts") : null,
            "/usr/share/fonts",
            "/usr/local/share/fonts",
            home.Length > 0 ? Path.Combine(home, ".local", "share", "fonts") : null,
            home.Length > 0 ? Path.Combine(home, ".fonts") : null,
            // macOS: Schriften des Systems (mit Supplemental/) und aller Benutzer
            OperatingSystem.IsMacOS() ? "/System/Library/Fonts" : null,
            OperatingSystem.IsMacOS() ? "/Library/Fonts" : null,
        ];
        return all.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.Ordinal)!;
    }

    private static Found? SearchLatin()
    {
        if (_latinSearched) return _latin;
        _latinSearched = true;
        var directories = Directories().Where(Directory.Exists).ToList();
        foreach (var faces in LatinCandidates)
            if (Find(faces, directories) is { } found) return _latin = found;
        return null;
    }

    private static CjkFont? SearchCjk()
    {
        var directories = Directories().Where(Directory.Exists).ToList();
        foreach (var faces in CjkCandidates)
        {
            if (Find(faces, directories) is not { } found) continue;
            var regular = found.Files[faces.Regular];
            var bytes = Load(regular, faces.Family, out var reason);
            if (bytes is null)
            {
                CjkLog.Add($"{faces.Family}: {regular} – {reason}");
                continue;
            }
            byte[]? bold = null;
            if (faces.Bold is not null && found.Files.TryGetValue(faces.Bold, out var boldPath))
                bold = Load(boldPath, faces.Family, out _);
            CjkLog.Add($"{faces.Family}: {regular} – used ({reason})");
            return new CjkFont(faces.Family, bytes, bold);
        }
        if (CjkLog.Count == 0) CjkLog.Add("no candidate file found in " + string.Join(", ", directories));
        return null;
    }

    /// <summary>
    /// Lädt eine Schriftdatei und gibt den passenden Schnitt als eigenständige TrueType-Schrift zurück; null mit Grund,
    /// wenn sie nicht taugt (CFF-Konturen, keine chinesischen Zeichen, nicht lesbar).
    /// </summary>
    internal static byte[]? Load(string path, string family, out string reason)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > Sfnt.MaxFileBytes)
            {
                reason = "file too large";
                return null;
            }
            var data = File.ReadAllBytes(path);
            var faces = Sfnt.Faces(data);
            var face = faces.FirstOrDefault(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase)) ?? faces[0];
            if (face.Outlines != SfntOutlines.TrueType)
            {
                reason = face.Outlines == SfntOutlines.Cff
                    ? "CFF (PostScript) outlines – PDFsharp 6.2.4 cannot embed them correctly"
                    : "no TrueType outlines";
                return null;
            }
            if (!Sfnt.HasGlyphs(data, face.Index, CjkProbe))
            {
                reason = "no glyphs for simplified Chinese";
                return null;
            }
            reason = Sfnt.IsCollection(data) ? $"collection, face {face.Index} extracted" : "single font";
            return Sfnt.Extract(data, face.Index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            reason = "not readable: " + e.GetType().Name;
            return null;
        }
    }

    /// <summary>Sucht die Schnitte einer Familie: erst direkt in den Ordnern, dann in deren Unterordnern.</summary>
    internal static Found? Find(FaceSet faces, IReadOnlyList<string> directories)
    {
        string[] names = [faces.Regular, .. new[] { faces.Bold, faces.Italic, faces.BoldItalic }.OfType<string>()];
        foreach (var directory in directories)
        {
            var regular = Path.Combine(directory, faces.Regular);
            if (File.Exists(regular)) return Collect(faces, names, Path.GetDirectoryName(regular)!);
        }
        foreach (var directory in directories)
        {
            var regular = SearchBelow(directory, faces.Regular);
            if (regular is not null) return Collect(faces, names, Path.GetDirectoryName(regular)!);
        }
        return null;
    }

    private static Found Collect(FaceSet faces, string[] names, string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in names)
        {
            var path = Path.Combine(folder, name);
            if (File.Exists(path)) files[name] = path;
            else if (SearchBelow(folder, name) is { } below) files[name] = below;
        }
        return new Found(faces, files);
    }

    private static string? SearchBelow(string directory, string fileName)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MatchCasing = MatchCasing.CaseInsensitive, MaxRecursionDepth = 6 };
            return Directory.EnumerateFiles(directory, fileName, options).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private const string CjkRegular = "riegel-cjk-regular";
    private const string CjkBold = "riegel-cjk-bold";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var cjk = _cjk;
        if (cjk is not null && string.Equals(familyName, cjk.Family, StringComparison.OrdinalIgnoreCase))
        {
            // Kursiv gibt es für CJK nicht; PDFsharp neigt den regulären Schnitt
            if (isBold && cjk.Bold is not null) return new FontResolverInfo(CjkBold, false, isItalic);
            return new FontResolverInfo(CjkRegular, isBold, isItalic);
        }
        var faces = _fonts.Faces;
        // Gewünschten Schnitt nehmen, sonst den regulären und PDFsharp fett/kursiv nachbilden lassen
        string? exact = (isBold, isItalic) switch
        {
            (true, true) => faces.BoldItalic,
            (true, false) => faces.Bold,
            (false, true) => faces.Italic,
            _ => faces.Regular,
        };
        if (exact is not null && _fonts.Files.ContainsKey(exact))
            return new FontResolverInfo(exact);
        if (isBold && isItalic && faces.Bold is not null && _fonts.Files.ContainsKey(faces.Bold))
            return new FontResolverInfo(faces.Bold, false, true);
        return new FontResolverInfo(faces.Regular, isBold, isItalic);
    }

    public byte[]? GetFont(string faceName) => faceName switch
    {
        CjkRegular => _cjk?.Regular,
        CjkBold => _cjk?.Bold ?? _cjk?.Regular,
        _ => _cache.GetOrAdd(faceName, name => File.ReadAllBytes(_fonts.Files[name])),
    };
}
