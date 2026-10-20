// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Resources;
using Riegel.Core.Resources;

namespace Riegel.Core.Model;

/// <summary>
/// Fehlercodes RGL-0001 bis RGL-0099 (gemeinsam, Gerüst; spec 7.13). Die Texte stehen in CommonStrings unter
/// <c>Error_RGL&lt;nnnn&gt;</c>. Andere Bereiche haben eigene Klassen (RglCodesEngine, RglCodesParsing …).
/// </summary>
public static class RglCodesCommon
{
    /// <summary>Unerwarteter Fehler (Programmfehler).</summary>
    public const string Unexpected = "RGL-0001";

    /// <summary>Datei oder Ordner nicht gefunden.</summary>
    public const string NotFound = "RGL-0002";

    /// <summary>Noch nicht umgesetzt (Platzhalter des Gerüsts).</summary>
    public const string NotImplemented = "RGL-0003";

    /// <summary>Kein gültiges JSON oder falsche Form.</summary>
    public const string InvalidJson = "RGL-0004";

    /// <summary>Unbekanntes oder nicht unterstütztes Schema.</summary>
    public const string UnsupportedSchema = "RGL-0005";

    /// <summary>Grenze für Eingaben überschritten (spec 7.12).</summary>
    public const string LimitExceeded = "RGL-0006";

    /// <summary>Ein-/Ausgabefehler.</summary>
    public const string IoError = "RGL-0007";

    /// <summary>Zugriff verweigert.</summary>
    public const string AccessDenied = "RGL-0008";

    /// <summary>Prüfsumme stimmt nicht.</summary>
    public const string HashMismatch = "RGL-0009";

    /// <summary>settings.json nicht lesbar (Standardwerte gelten).</summary>
    public const string SettingsUnreadable = "RGL-0010";

    /// <summary>defaults.json nicht lesbar (ignoriert).</summary>
    public const string DefaultsUnreadable = "RGL-0011";

    /// <summary>Auf diesem Betriebssystem nicht möglich (z. B. Erfassung unter Linux).</summary>
    public const string PlatformNotSupported = "RGL-0012";

    /// <summary>Ungültige Angabe.</summary>
    public const string InvalidArgument = "RGL-0013";

    /// <summary>Prüfermodus: Schreiben abgelehnt.</summary>
    public const string ReadOnlyMode = "RGL-0014";

    /// <summary>Diagnosepaket ließ sich nicht schreiben.</summary>
    public const string DiagnosticsFailed = "RGL-0015";

    /// <summary>Ziel existiert schon und wird nicht überschrieben.</summary>
    public const string AlreadyExists = "RGL-0016";

    /// <summary>Linux: ICU fehlt – Riegel läuft ohne Kulturdaten weiter (Hinweis, Datum und Zahlen nicht lokalisiert).</summary>
    public const string IcuMissing = "RGL-0017";

    /// <summary>Linux: OpenSSL (libssl) fehlt – ohne SHA-256 geht kein Befehl außer der Hilfe.</summary>
    public const string OpenSslMissing = "RGL-0018";

    /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Unexpected, NotFound, NotImplemented, InvalidJson, UnsupportedSchema, LimitExceeded, IoError, AccessDenied,
        HashMismatch, SettingsUnreadable, DefaultsUnreadable, PlatformNotSupported, InvalidArgument, ReadOnlyMode,
        DiagnosticsFailed, AlreadyExists, IcuMissing, OpenSslMissing,
    ];

    /// <summary>Text zu einem gemeinsamen Code in der aktuellen Sprache, mit Platzhaltern.</summary>
    public static string Message(string code, params object?[] args)
    {
        var key = "Error_" + code.Replace("-", "", StringComparison.Ordinal);
        var text = CommonStrings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? code;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}

/// <summary>Fehler mit RGL-Code (Richtlinie 13 Punkt 9): der Text ist übersetzt, der Code bleibt gleich.</summary>
public class RiegelException : Exception
{
    public RiegelException(string code, string message) : base(message) => Code = code;

    public RiegelException(string code, string message, Exception inner) : base(message, inner) => Code = code;

    /// <summary>RGL-nnnn</summary>
    public string Code { get; }

    /// <summary>„RGL-0002: …“ für Ausgaben.</summary>
    public override string ToString() => $"{Code}: {Message}";

    /// <summary>Fehler mit einem gemeinsamen Code und dessen Text aus CommonStrings.</summary>
    public static RiegelException Common(string code, params object?[] args) => new(code, RglCodesCommon.Message(code, args));
}
