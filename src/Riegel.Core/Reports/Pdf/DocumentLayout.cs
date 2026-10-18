// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using Riegel.Core.Model;
using Riegel.Core.Model.Reports;
using Riegel.Core.Resources;

namespace Riegel.Core.Reports.Pdf;

/// <summary>Farben der Dokumente: Schwere-Ampel, Banner und die Produktfarben (Verlauf #F3821D → #AC281B, spec 6).</summary>
internal static class PdfColors
{
    public const string Orange = "#F3821D";
    public const string Red = "#AC281B";
    public const string Text = "#1F2937";
    public const string Muted = "#6B7280";
    public const string Greyed = "#9CA3AF";
    public const string Line = "#D1D5DB";
    public const string Header = "#374151";
    public const string HeaderLight = "#F3F4F6";
    public const string Warning = "#B42318";
    public const string BannerFill = "#FFF4E5";
    public const string BannerBorder = "#E65100";
    public const string BannerText = "#5D2E00";
    public const string InfoFill = "#EEF2F6";
    public const string InfoBorder = "#64748B";
    public const string Good = "#2E7D32";

    /// <summary>Hintergrund und Schriftfarbe der Schwere-Zelle; Gelb braucht dunkle Schrift.</summary>
    public static (string Fill, string Font) For(Severity? severity) => severity switch
    {
        Severity.High => ("#C62828", "#FFFFFF"),
        Severity.Medium => ("#EF6C00", "#FFFFFF"),
        Severity.Low => ("#FBC02D", "#212121"),
        _ => ("#BDBDBD", "#212121"),
    };

    public static Color Rgb(string hex)
    {
        var value = uint.Parse(hex.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Color((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}

/// <summary>
/// Wie viele Einträge ein Abschnitt höchstens zeigt (MigraDoc setzt grob 200 Tabellenzeilen je Sekunde). Darüber kürzt
/// das PDF mit deutlichem Hinweis; vollständig sind Excel-Mappe und JSON.
/// </summary>
internal sealed record PdfLimits(int Findings, int Changes, int RegisterRows, int Machines, int ResultsPerMachine)
{
    public static PdfLimits Default { get; } = new(Findings: 1_000, Changes: 1_000, RegisterRows: 1_000, Machines: 2_000, ResultsPerMachine: 500);
}

/// <summary>Was in jeder Fußzeile steht (spec 5: „Riegel &lt;version&gt; · Bericht &lt;reportId&gt; · Seite x/y · &lt;Vertraulichkeit&gt;“).</summary>
/// <param name="Document">Dokument mit Kennung, z. B. „Bericht RPT-…“.</param>
/// <param name="HeaderTitle">Titel in der Kopfzeile ab Seite 2.</param>
/// <param name="HeaderRight">Rechts in der Kopfzeile (Anlage).</param>
/// <param name="PreTranslated">Zweite Zeile „Maschinelle Vorübersetzung – nicht geprüft / Machine pre-translation – not reviewed“.</param>
internal sealed record PageFrame(string Version, string Document, string Confidentiality, string HeaderTitle, string HeaderRight,
    bool PreTranslated);

/// <summary>
/// Gemeinsame Bausteine aller PDF-Dokumente (Muster aus Zutritts PdfReport): A4 hoch, Stile, Kopf- und Fußzeile mit
/// Seitenzahlen, Überschriften mit Sprungmarken, Tabellen, die genau auf dem Satzspiegel sitzen, Schwere-Zellen,
/// Banner, Unterschriftsblöcke und das Zusammenhalten von Tabellenanfängen (KeepStart).
/// </summary>
internal abstract class DocumentLayout
{
    public const string IntroStyle = "Einleitung";
    protected const string TableStyle = "Tabelle";
    protected const string SmallStyle = "Klein";
    protected const double ContentWidthCm = 17.0;
    protected const double NormalSize = 9.5;
    protected const double TableSize = 8.0;
    protected const double SmallSize = 7.5;
    protected const double PaddingPt = 3.5;

    /// <summary>Höchsthöhe eines zusammengehaltenen Blocks (deutlich unter einer Seite, sonst verschluckt MigraDoc Zeilen).</summary>
    private const double KeepBudgetPt = 260;

    /// <summary>Zeilenhöhe im Verhältnis zur Schriftgröße – mit Reserve (CJK-Schriften haben größere Zeilenabstände).</summary>
    private const double LineFactor = 1.45;

    protected static readonly Color TextColor = PdfColors.Rgb(PdfColors.Text);
    protected static readonly Color MutedColor = PdfColors.Rgb(PdfColors.Muted);
    protected static readonly Color GreyedColor = PdfColors.Rgb(PdfColors.Greyed);
    protected static readonly Color LineColor = PdfColors.Rgb(PdfColors.Line);
    protected static readonly Color OrangeColor = PdfColors.Rgb(PdfColors.Orange);
    protected static readonly Color RedColor = PdfColors.Rgb(PdfColors.Red);
    protected static readonly Color WarningColor = PdfColors.Rgb(PdfColors.Warning);
    protected static readonly Color GoodColor = PdfColors.Rgb(PdfColors.Good);

    protected DocumentLayout(PdfTexts t, PdfText text, string family, PageFrame frame, PdfLimits limits)
    {
        T = t;
        Text = text;
        Family = family;
        Frame = frame;
        Limits = limits;
    }

    protected PdfTexts T { get; }
    protected PdfText Text { get; }
    protected string Family { get; }
    protected PageFrame Frame { get; }
    protected PdfLimits Limits { get; }
    protected Document Doc { get; } = new();
    protected Section Section { get; private set; } = null!;

    /// <summary>Abschnitte fürs Inhaltsverzeichnis in Reihenfolge, je mit Sprungmarke.</summary>
    protected List<(string Title, string Bookmark)> Contents { get; } = [];

    protected static double ContentWidth => Unit.FromCentimeter(ContentWidthCm).Point;

    /// <summary>Baut das Dokument (ohne zu rendern).</summary>
    public Document Build(string title, string subject)
    {
        Doc.Info.Title = PdfText.Clean(title);
        Doc.Info.Author = "Riegel";
        Doc.Info.Subject = PdfText.Clean(subject);
        DefineStyles();
        Section = Doc.AddSection();
        SetupPage();
        Compose();
        return Doc;
    }

    /// <summary>Inhalt des Dokuments.</summary>
    protected abstract void Compose();

    // ---- Seite ---------------------------------------------------------------------------------------------

    private void DefineStyles()
    {
        var normal = Doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = Family;
        normal.Font.Size = NormalSize;
        normal.Font.Color = TextColor;
        normal.ParagraphFormat.SpaceAfter = 3;

        var h1 = Doc.Styles[StyleNames.Heading1]!;
        h1.Font.Size = 14;
        h1.Font.Bold = true;
        h1.Font.Color = RedColor;
        h1.ParagraphFormat.SpaceBefore = 14;
        h1.ParagraphFormat.SpaceAfter = 5;
        h1.ParagraphFormat.KeepWithNext = true;

        var h2 = Doc.Styles[StyleNames.Heading2]!;
        h2.Font.Size = 10.5;
        h2.Font.Bold = true;
        h2.Font.Color = TextColor;
        h2.ParagraphFormat.SpaceBefore = 9;
        h2.ParagraphFormat.SpaceAfter = 3;
        h2.ParagraphFormat.KeepWithNext = true;

        foreach (var name in new[] { StyleNames.Header, StyleNames.Footer })
        {
            var style = Doc.Styles[name]!;
            style.Font.Size = SmallSize;
            style.Font.Color = MutedColor;
        }

        var table = Doc.Styles.AddStyle(TableStyle, StyleNames.Normal);
        table.Font.Size = TableSize;
        table.ParagraphFormat.SpaceAfter = 0;

        var small = Doc.Styles.AddStyle(SmallStyle, StyleNames.Normal);
        small.Font.Size = SmallSize;
        small.Font.Color = MutedColor;

        // Einleitung unter einer Überschrift: gehört zum Folgenden, sonst stünde sie allein am Seitenende
        var intro = Doc.Styles.AddStyle(IntroStyle, SmallStyle);
        intro.ParagraphFormat.SpaceAfter = 4;
        intro.ParagraphFormat.KeepWithNext = true;
    }

    private void SetupPage()
    {
        var page = Doc.DefaultPageSetup.Clone();
        page.PageFormat = PageFormat.A4;
        page.Orientation = Orientation.Portrait;
        page.TopMargin = Unit.FromCentimeter(2.2);
        page.BottomMargin = Unit.FromCentimeter(Frame.PreTranslated ? 2.3 : 2.0);
        page.LeftMargin = Unit.FromCentimeter(2.0);
        page.RightMargin = Unit.FromCentimeter(2.0);
        page.HeaderDistance = Unit.FromCentimeter(1.0);
        page.FooterDistance = Unit.FromCentimeter(0.9);
        page.DifferentFirstPageHeaderFooter = true; // erste Seite ohne Kopfzeile, Fußzeile auf jeder Seite
        Section.PageSetup = page;

        var header = Section.Headers.Primary.AddParagraph();
        header.Format.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right);
        header.Format.Borders.Bottom.Width = 0.75;
        header.Format.Borders.Bottom.Color = OrangeColor;
        header.Format.Borders.DistanceFromBottom = 3;
        header.AddFormattedText(PdfText.Clean(Shorten($"Riegel · {Frame.HeaderTitle}", 70)), TextFormat.Bold);
        header.AddTab();
        header.AddText(PdfText.Clean(Shorten(Frame.HeaderRight, 50)));

        Footer(Section.Footers.Primary);
        Footer(Section.Footers.FirstPage);
    }

    private void Footer(HeaderFooter footer)
    {
        var p = footer.AddParagraph();
        p.Format.Borders.Top.Width = 0.5;
        p.Format.Borders.Top.Color = LineColor;
        p.Format.Borders.DistanceFromTop = 3;
        p.AddText(PdfText.Clean($"Riegel {Frame.Version} · {Frame.Document} · {T.T("Footer_Page")} "));
        p.AddPageField();
        p.AddText("/");
        p.AddNumPagesField();
        p.AddText(PdfText.Clean(" · " + Frame.Confidentiality));
        if (Frame.PreTranslated)
        {
            var note = footer.AddParagraph();
            note.Format.Font.Bold = true;
            note.Format.Font.Color = PdfColors.Rgb(PdfColors.BannerBorder);
            note.AddText(PreTranslationLine());
        }
    }

    /// <summary>„Maschinelle Vorübersetzung – nicht geprüft“ in der Sprache des Dokuments und auf Englisch (spec 7.10).</summary>
    protected string PreTranslationLine()
    {
        var local = T.Common("PreTranslation_Note");
        var english = CommonStrings.ResourceManager.GetString("PreTranslation_Note", CultureInfo.InvariantCulture) ?? "";
        return local == english ? english : $"{local} / {english}";
    }

    private static string Shorten(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = char.IsHighSurrogate(text[max - 2]) ? max - 2 : max - 1;
        return text[..cut] + "…";
    }

    // ---- Bausteine -----------------------------------------------------------------------------------------

    /// <summary>Trägt einen Abschnitt fürs Inhaltsverzeichnis ein; die Sprungmarke setzt <see cref="Heading1"/>.</summary>
    protected void Plan(string title) => Contents.Add((title, $"abschnitt{Contents.Count + 1}"));

    protected Paragraph Heading1(string title, bool newPage = false)
    {
        if (newPage) Section.AddPageBreak();
        var p = Section.AddParagraph();
        p.Style = StyleNames.Heading1;
        p.AddText(PdfText.Clean(title));
        if (newPage) p.Format.SpaceBefore = 0;
        Bookmark(p, title);
        return p;
    }

    protected Paragraph Heading2(string title)
    {
        var p = Section.AddParagraph();
        p.Style = StyleNames.Heading2;
        Text.Append(p, title, ContentWidth, 10.5, bold: true);
        Bookmark(p, title);
        return p;
    }

    private void Bookmark(Paragraph heading, string title)
    {
        foreach (var (entry, bookmark) in Contents)
            if (entry == title) heading.AddBookmark(bookmark);
    }

    /// <summary>Inhaltsverzeichnis mit Seitenzahlen.</summary>
    protected void TableOfContents()
    {
        var heading = Section.AddParagraph(T.T("Contents"));
        heading.Format.SpaceBefore = 6;
        heading.Format.SpaceAfter = 4;
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 11;
        heading.Format.Font.Color = RedColor;
        heading.Format.KeepWithNext = true;
        foreach (var (title, bookmark) in Contents)
        {
            var p = Section.AddParagraph();
            p.Format.SpaceAfter = 1.5;
            p.Format.KeepWithNext = true;
            p.Format.TabStops.AddTabStop(Unit.FromCentimeter(ContentWidthCm), TabAlignment.Right, TabLeader.Dots);
            p.AddText(PdfText.Clean(title));
            p.AddTab();
            p.AddPageRefField(bookmark);
        }
        if (Section.Elements.LastObject is Paragraph last) last.Format.KeepWithNext = false;
    }

    protected Paragraph Paragraph(string content, bool small = false, Color? color = null, bool bold = false)
    {
        var p = Section.AddParagraph();
        if (small) p.Style = SmallStyle;
        Text.Append(p, content, ContentWidth, small ? SmallSize : NormalSize, bold, color);
        return p;
    }

    /// <summary>Einleitung unter einer Überschrift; ohne folgenden Inhalt normaler Kleintext.</summary>
    protected Paragraph Intro(string content, bool keepWithNext = true)
    {
        var p = Section.AddParagraph();
        p.Style = keepWithNext ? IntroStyle : SmallStyle;
        Text.Append(p, content, ContentWidth, SmallSize);
        return p;
    }

    /// <summary>Aufzählungspunkt.</summary>
    protected Paragraph Bullet(string content, bool small = false)
    {
        var p = Section.AddParagraph();
        if (small) p.Style = SmallStyle;
        p.Format.LeftIndent = Unit.FromCentimeter(0.4);
        p.Format.FirstLineIndent = Unit.FromCentimeter(-0.4);
        p.Format.TabStops.AddTabStop(Unit.FromCentimeter(0.4));
        p.AddText("•");
        p.AddTab();
        Text.Append(p, content, Unit.FromCentimeter(ContentWidthCm - 0.4).Point, small ? SmallSize : NormalSize);
        return p;
    }

    protected void Spacer(double points)
    {
        var p = Section.AddParagraph();
        p.Format.Font.Size = 1;
        p.Format.SpaceAfter = points;
    }

    /// <summary>Hinweis auf eine gekürzte Liste.</summary>
    protected void Truncated(int total, int shown)
    {
        if (total <= shown) return;
        var p = Section.AddParagraph();
        p.Format.SpaceBefore = 3;
        p.Format.Font.Color = WarningColor;
        Text.Append(p, T.F("Truncated", T.Number(shown), T.Number(total)), ContentWidth, NormalSize);
    }

    /// <summary>Hinweisbanner: farbiger Kasten mit Randlinie links; <paramref name="warning"/> = orange, sonst grau.</summary>
    protected void Banner(string title, string? detail, bool warning = true)
    {
        if (CompactBanners)
        {
            CompactBanner(title, detail, warning);
            return;
        }
        var table = NewTable(framed: false, ContentWidthCm);
        table.Borders.Visible = false;
        table.LeftPadding = 6;
        table.TopPadding = 3;
        table.BottomPadding = 3;
        var row = table.AddRow();
        var cell = row.Cells[0];
        cell.Shading.Color = PdfColors.Rgb(warning ? PdfColors.BannerFill : PdfColors.InfoFill);
        cell.Borders.Left.Visible = true;
        cell.Borders.Left.Width = 3;
        cell.Borders.Left.Color = PdfColors.Rgb(warning ? PdfColors.BannerBorder : PdfColors.InfoBorder);
        var p = cell.AddParagraph();
        p.Format.Font.Size = 8.5;
        var color = PdfColors.Rgb(warning ? PdfColors.BannerText : PdfColors.Text);
        Text.Append(p, title, CellWidth(table, 0), 8.5, bold: true, color: color);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            var d = cell.AddParagraph();
            d.Format.Font.Size = SmallSize;
            Text.Append(d, detail, CellWidth(table, 0), SmallSize, color: color);
        }
        Spacer(2);
    }

    /// <summary>Banner in einer Zeile (Titel fett, Text dahinter) – für die Übersicht, die auf eine Seite passen muss.</summary>
    protected bool CompactBanners { get; set; }

    private void CompactBanner(string title, string? detail, bool warning)
    {
        var table = NewTable(framed: false, ContentWidthCm);
        table.Borders.Visible = false;
        table.LeftPadding = 5;
        table.TopPadding = 1.5;
        table.BottomPadding = 1.5;
        var cell = table.AddRow().Cells[0];
        cell.Shading.Color = PdfColors.Rgb(warning ? PdfColors.BannerFill : PdfColors.InfoFill);
        cell.Borders.Left.Visible = true;
        cell.Borders.Left.Width = 3;
        cell.Borders.Left.Color = PdfColors.Rgb(warning ? PdfColors.BannerBorder : PdfColors.InfoBorder);
        var p = cell.AddParagraph();
        p.Format.Font.Size = SmallSize;
        var color = PdfColors.Rgb(warning ? PdfColors.BannerText : PdfColors.Text);
        Text.Append(p, string.IsNullOrWhiteSpace(detail) ? title : title + " – ", CellWidth(table, 0), SmallSize, bold: true, color: color);
        if (!string.IsNullOrWhiteSpace(detail)) Text.Append(p, detail, CellWidth(table, 0), SmallSize, color: color);
        Spacer(1);
    }

    /// <summary>
    /// Neue Tabelle, die genau auf dem Satzspiegel sitzt (MigraDoc rückt Tabellen sonst um minus Innenabstand ein).
    /// Ungerahmte Tabellen (Schlüssel/Wert, Unterschriften) haben keinen linken Innenabstand und fluchten mit dem Text.
    /// </summary>
    protected Table NewTable(params double[] widthsCm) => NewTable(framed: true, widthsCm);

    protected Table NewTable(bool framed, params double[] widthsCm)
    {
        var table = Section.AddTable();
        table.Rows.LeftIndent = Unit.FromPoint(0);
        table.Style = TableStyle;
        table.Borders.Color = LineColor;
        table.Borders.Width = 0.5;
        table.LeftPadding = framed ? PaddingPt : 0;
        table.RightPadding = PaddingPt;
        table.TopPadding = 1.5;
        table.BottomPadding = 1.5;
        foreach (var w in widthsCm) table.AddColumn(Unit.FromCentimeter(w));
        return table;
    }

    protected void HeaderRow(Table table, params string[] titles) => HeaderRow(table, light: false, titles);

    protected void HeaderRow(Table table, bool light, params string[] titles) => HeaderRow(table, light, TableSize, titles);

    protected void HeaderRow(Table table, bool light, double size, params string[] titles)
    {
        var row = table.AddRow();
        row.HeadingFormat = true; // wiederholt sich auf jeder Seite
        row.Shading.Color = PdfColors.Rgb(light ? PdfColors.HeaderLight : PdfColors.Header);
        row.Format.Font.Bold = true;
        row.Format.Font.Size = size;
        row.Format.Font.Color = light ? TextColor : Colors.White;
        for (int i = 0; i < titles.Length; i++)
        {
            // Schmale Spalten: lieber etwas kleiner als ein Wort mitten durchtrennt („Ausnahme-n“)
            double width = CellWidth(table, i), fitted = size;
            var longest = titles[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).DefaultIfEmpty("").Max(w => Text.Width(w, size, bold: true));
            while (fitted > 5.5 && longest * fitted / size > width * 0.95) fitted -= 0.5;
            var p = row.Cells[i].AddParagraph();
            if (fitted < size) p.Format.Font.Size = fitted;
            Text.Append(p, titles[i], width, fitted, bold: true);
        }
    }

    protected Paragraph CellText(Table table, Row row, int column, string? content, bool bold = false, Color? color = null, double? size = null)
    {
        var p = row.Cells[column].AddParagraph();
        if (size is { } s) p.Format.Font.Size = s;
        if (color is { } c) p.Format.Font.Color = c;
        Text.Append(p, string.IsNullOrEmpty(content) ? T.Dash : content, CellWidth(table, column, row.Cells[column].MergeRight), size ?? TableSize, bold);
        return p;
    }

    /// <summary>Zweite (kleine, graue) Zeile in einer Zelle, z. B. die Kennung unter dem Titel.</summary>
    protected void CellNote(Table table, Row row, int column, string? content, Color? color = null)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        var p = row.Cells[column].AddParagraph();
        p.Format.Font.Size = SmallSize;
        p.Format.Font.Color = color ?? MutedColor;
        Text.Append(p, content, CellWidth(table, column, row.Cells[column].MergeRight), SmallSize);
    }

    protected void SeverityCell(Cell cell, Severity? severity)
    {
        var (fill, font) = PdfColors.For(severity);
        cell.Shading.Color = PdfColors.Rgb(fill);
        var p = cell.AddParagraph(T.Severity(severity));
        p.Format.Font.Bold = true;
        p.Format.Font.Color = PdfColors.Rgb(font);
    }

    /// <summary>Schlüssel/Wert-Tabelle ohne Rahmen (Deckblatt, Kopf je Rechner).</summary>
    protected Table KeyValueTable(double keyCm = 4.2)
    {
        var table = NewTable(framed: false, keyCm, ContentWidthCm - keyCm);
        table.Borders.Visible = false;
        table.TopPadding = 1.8;
        table.BottomPadding = 1.8;
        return table;
    }

    protected void KeyValue(Table table, string key, string? value, bool bold = false, Color? color = null, double? size = null)
    {
        var row = table.AddRow();
        var k = row.Cells[0].AddParagraph();
        k.Format.Font.Color = MutedColor;
        Text.Append(k, key, CellWidth(table, 0), size ?? TableSize);
        var v = row.Cells[1].AddParagraph();
        if (size is { } s) v.Format.Font.Size = s;
        if (color is { } c) v.Format.Font.Color = c;
        Text.Append(v, string.IsNullOrWhiteSpace(value) ? T.Dash : value, CellWidth(table, 1), size ?? TableSize, bold);
    }

    /// <summary>Zahlenkacheln (Zählungen zuerst, spec 2.6): je Kachel Zahl und Beschriftung, optional Farbe.</summary>
    protected void Chips(IReadOnlyList<(string Value, string Label, string? Fill, string? Font)> chips)
    {
        if (chips.Count == 0) return;
        const double gap = 0.2;
        double chip = (ContentWidthCm - gap * (chips.Count - 1)) / chips.Count;
        var widths = new List<double>();
        for (int i = 0; i < chips.Count; i++)
        {
            if (i > 0) widths.Add(gap);
            widths.Add(chip);
        }
        var table = NewTable(framed: false, [.. widths]);
        table.Borders.Visible = false;
        table.LeftPadding = 3;
        table.TopPadding = 3;
        table.BottomPadding = 3;
        var row = table.AddRow();
        for (int i = 0; i < chips.Count; i++)
        {
            var (value, label, fill, font) = chips[i];
            var cell = row.Cells[2 * i];
            cell.Shading.Color = PdfColors.Rgb(fill ?? PdfColors.HeaderLight);
            var color = PdfColors.Rgb(font ?? PdfColors.Text);
            var v = cell.AddParagraph();
            v.Format.Alignment = ParagraphAlignment.Center;
            v.Format.Font.Size = 15;
            v.Format.Font.Bold = true;
            v.Format.Font.Color = color;
            v.AddText(PdfText.Clean(value));
            var l = cell.AddParagraph();
            l.Format.Alignment = ParagraphAlignment.Center;
            l.Format.Font.Size = SmallSize;
            l.Format.Font.Color = color;
            Text.Append(l, label, CellWidth(table, 2 * i), SmallSize);
        }
        Spacer(3);
    }

    /// <summary>Kopf der kurzen Dokumente: Produktzeile, Titel, Untertitel, Farbbalken.</summary>
    protected void TitleBlock(string title, string? subtitle, double titleSize = 20)
    {
        var label = Section.AddParagraph(T.T("Product_Label"));
        label.Format.Font.Size = 8.5;
        label.Format.Font.Bold = true;
        label.Format.Font.Color = RedColor;
        var t = Section.AddParagraph();
        t.Format.Font.Size = titleSize;
        t.Format.Font.Bold = true;
        t.Format.SpaceAfter = 1;
        Text.Append(t, title, ContentWidth, titleSize, bold: true);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var s = Section.AddParagraph();
            s.Format.Font.Size = 11.5;
            s.Format.SpaceAfter = 5;
            Text.Append(s, subtitle, ContentWidth, 11.5, bold: true);
        }
        AccentBar();
        Spacer(5);
    }

    /// <summary>Bezugszeile über den Unterschriften: Dokument, Seitenzahl (Feld).</summary>
    protected void ReferenceLine(string text)
    {
        var reference = Section.AddParagraph();
        reference.Style = IntroStyle;
        reference.Format.SpaceAfter = 6;
        Text.Append(reference, text + " ", ContentWidth, SmallSize);
        reference.AddText(PdfText.Clean(T.T("SignOff_PageCount") + " "));
        reference.AddNumPagesField();
    }

    /// <summary>Farbiger Balken in den Produktfarben (Deckblatt).</summary>
    protected void AccentBar()
    {
        var bar = NewTable(framed: false, ContentWidthCm / 2, ContentWidthCm / 2);
        bar.Borders.Visible = false;
        var row = bar.AddRow();
        row.HeightRule = RowHeightRule.Exactly;
        row.Height = Unit.FromCentimeter(0.15);
        row.Cells[0].Shading.Color = OrangeColor;
        row.Cells[1].Shading.Color = RedColor;
    }

    /// <summary>
    /// Unterschriftsblöcke (Muster aus Zutritts Block „Prüfung und Freigabe“): je Rolle ein Kopf „Schritt · Rolle“ und
    /// Linien für Name, Datum, Unterschrift und Bemerkungen. Jeder Block bleibt auf einer Seite.
    /// </summary>
    protected void SignatureBlocks(IReadOnlyList<(string Title, string? Role)> blocks, bool remarks = true)
    {
        foreach (var (title, role) in blocks)
        {
            var table = NewTable(framed: false, 3.6, ContentWidthCm - 3.6);
            table.Style = StyleNames.Normal;
            table.Borders.Visible = false;
            var head = table.AddRow();
            head.TopPadding = 6;
            var cell = head.Cells[0];
            cell.MergeRight = 1;
            var p = cell.AddParagraph();
            p.Format.Font.Bold = true;
            p.Format.Font.Color = RedColor;
            Text.Append(p, role is null ? title : $"{title} · {T.T("SignOff_Role")}: {role}", ContentWidth - PaddingPt, NormalSize, bold: true);
            void Line(string label, double heightCm)
            {
                var row = table.AddRow();
                row.Height = Unit.FromCentimeter(heightCm);
                row.HeightRule = RowHeightRule.AtLeast;
                row.VerticalAlignment = VerticalAlignment.Bottom;
                var l = row.Cells[0].AddParagraph();
                Text.Append(l, label, CellWidth(table, 0), NormalSize);
                row.Cells[1].Borders.Bottom.Visible = true;
                row.Cells[1].Borders.Bottom.Width = 0.75;
                row.Cells[1].Borders.Bottom.Color = TextColor;
            }
            Line(T.T("SignOff_Name"), 0.9);
            Line(T.T("SignOff_Date"), 0.9);
            Line(T.T("SignOff_Signature"), 1.3);
            if (remarks)
            {
                Line(T.T("SignOff_Remarks"), 0.9);
                Line("", 0.8);
            }
            table.Rows[0].KeepWith = table.Rows.Count - 1;
        }
    }

    /// <summary>
    /// Hält den Tabellenkopf mit den ersten <paramref name="rows"/> Datenzeilen zusammen – mit <paramref name="whole"/>
    /// die ganze Tabelle, wenn sie klein genug ist (aus Zutritt).
    /// </summary>
    protected void KeepStart(Table table, int rows = 3, bool whole = false)
    {
        int headers = table.Rows.Cast<Row>().TakeWhile(r => r.HeadingFormat).Count();
        int dataRows = table.Rows.Count - headers;
        if (table.Rows.Count < 2 || dataRows == 0) return;

        double height = 0;
        for (int r = 0; r < headers; r++) height += RowHeight(table, r);
        int keep = 0;
        int max = whole ? dataRows : Math.Min(rows, dataRows);
        for (int r = headers; r < headers + max; r++)
        {
            height += RowHeight(table, r);
            if (keep > 0 && height > KeepBudgetPt) break;
            keep++;
        }
        if (whole && keep < dataRows) keep = Math.Min(keep, rows);
        if (dataRows - keep == 1 && keep > 1) keep--;
        var first = table.Rows[headers];
        if (keep > 1) first.KeepWith = Math.Max(first.KeepWith, keep - 1);
        if (dataRows - keep >= 2) table.Rows[^2].KeepWith = Math.Max(table.Rows[^2].KeepWith, 1);
    }

    /// <summary>Geschätzte Höhe einer Tabellenzeile in Punkt (fett gemessen – lieber zu hoch als zu niedrig).</summary>
    private double RowHeight(Table table, int index)
    {
        var row = table.Rows[index];
        double size = table.Style == StyleNames.Normal ? NormalSize : TableSize;
        double max = 0;
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var cell = row.Cells[c];
            double width = CellWidth(table, c, cell.MergeRight);
            double height = 0;
            foreach (var p in cell.Elements.OfType<Paragraph>())
            {
                double pSize = p.Format.Font.Size.Point > 0 ? p.Format.Font.Size.Point : size;
                height += Text.Lines(PlainText(p), width, pSize, bold: true) * pSize * LineFactor + 1;
            }
            max = Math.Max(max, height);
        }
        max = Math.Max(max, row.Height.Point);
        return max + table.TopPadding.Point + table.BottomPadding.Point + 1;
    }

    private static string PlainText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        void Walk(IEnumerable<DocumentObject> elements)
        {
            foreach (var e in elements)
            {
                switch (e)
                {
                    case MigraDoc.DocumentObjectModel.Text t: sb.Append(t.Content); break;
                    case FormattedText f: Walk(f.Elements.Cast<DocumentObject>()); break;
                    case Character { SymbolName: SymbolName.LineBreak }: sb.Append('\n'); break;
                    case Character { SymbolName: SymbolName.Tab }: sb.Append("    "); break;
                }
            }
        }
        Walk(paragraph.Elements.Cast<DocumentObject>());
        return sb.ToString();
    }

    /// <summary>Nutzbare Breite einer Zelle in Punkt (Spalte(n) minus Innenabstand und Linie).</summary>
    protected static double CellWidth(Table table, int column, int mergeRight = 0)
    {
        double width = table.Columns[column].Width.Point;
        for (int m = 1; m <= mergeRight && column + m < table.Columns.Count; m++) width += table.Columns[column + m].Width.Point;
        return width - table.LeftPadding.Point - table.RightPadding.Point - 1;
    }

    // ---- Gemeinsame Textbausteine ---------------------------------------------------------------------------

    /// <summary>Titel und Text eines Banners (spec 2.4 Punkt 9, 2.8, 2.10, 5).</summary>
    protected (string Title, string? Detail, bool Warning) BannerText(Banner banner)
    {
        var hosts = T.Hosts(banner.Hosts);
        var areas = string.Join(", ", banner.Areas.Select(T.Category));
        return banner.Kind switch
        {
            BannerKind.IncompleteCheck => (T.F("Banner_Incomplete", T.Percent(banner.CheckedShare)),
                T.F("Banner_Incomplete_Detail", hosts, areas.Length == 0 ? T.Dash : areas), true),
            BannerKind.StaleSnapshot => (T.F("Banner_StaleSnapshot", T.Number(banner.AgeDays)), T.F("Banner_Hosts", hosts), true),
            BannerKind.StaleCatalogue => (T.T("Banner_StaleCatalogue"), banner.AgeDays is { } age ? T.F("Banner_StaleCatalogue_Detail", T.Number(age)) : null, true),
            BannerKind.PreTranslation => (PreTranslationLine(), null, true),
            BannerKind.ExperimentalGpoAttribution => (T.T("Banner_ExperimentalGpo"), T.F("Banner_Hosts", hosts), false),
            BannerKind.ClockSuspect => (T.T("Banner_ClockSuspect"), T.F("Banner_Hosts", hosts), true),
            BannerKind.DomainController => (T.T("Banner_DomainController"), T.F("Banner_Hosts", hosts), false),
            _ => (banner.Kind.ToString(), hosts, false),
        };
    }

    /// <summary>
    /// Banner in der Reihenfolge der Daten. Den Vermerk „Vorübersetzung“ bestimmt allein die Sprache, in der das Dokument
    /// tatsächlich entsteht (nach einem Rückfall auf Englisch entfällt er).
    /// </summary>
    protected void Banners(IReadOnlyList<Banner> banners)
    {
        if (Frame.PreTranslated) Banner(PreTranslationLine(), T.T("Banner_PreTranslation_Detail"));
        foreach (var banner in banners.Where(b => b.Kind != BannerKind.PreTranslation))
        {
            var (title, detail, warning) = BannerText(banner);
            Banner(title, detail, warning);
        }
    }

    /// <summary>Hinweis nach Rückfall auf Englisch (RGL-0510).</summary>
    protected void FontFallbackNote(string? requestedLanguage)
    {
        if (requestedLanguage is null) return;
        Banner(RglCodesPdf.NoFontForLanguage + ": " + RglCodesPdf.Message(T.Culture, RglCodesPdf.NoFontForLanguage, requestedLanguage), null);
    }

    /// <summary>„Riegel 0.1.0 (Commit abc1234) · Exe-SHA-256 …“</summary>
    protected string ToolLine(ToolInfo tool) =>
        T.F("Tool_Line", tool.Name, tool.Version, T.OrDash(tool.Commit));
}
