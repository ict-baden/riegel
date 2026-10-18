// SPDX-License-Identifier: MIT
using Riegel.Core.Model;
using Riegel.Core.Reports.Pdf;
using Riegel.Core.Samples;
using Riegel.Tests.TestSupport;
using static Riegel.Tests.Reports.Pdf.PdfTest;

namespace Riegel.Tests.Reports.Pdf;

/// <summary>Das Deckblatt-Logo ist fremde Eingabe (spec 7.12): PNG/JPEG bis 1 MB, geprüft, nie vertraut.</summary>
public sealed class CoverLogoTests : IDisposable
{
    private readonly TempFolder _temp = new();

    public void Dispose() => _temp.Dispose();

    private string File(string name, byte[] bytes)
    {
        var path = Path.Combine(_temp.Path, name);
        System.IO.File.WriteAllBytes(path, bytes);
        return path;
    }

    [PdfFact]
    public void Gültiges_PNG_und_JPEG_werden_angenommen()
    {
        var png = CoverLogo.Load(File("logo.png", Png(200, 60)))!;
        Assert.Equal(("png", 200, 60), (png.Format, png.Width, png.Height));
        Assert.StartsWith("base64:", png.Source);
        var jpeg = CoverLogo.Load(File("logo.jpg", Jpeg()))!;
        Assert.Equal(("jpeg", 16, 8), (jpeg.Format, jpeg.Width, jpeg.Height));
    }

    [Fact]
    public void Kein_Logo_ist_kein_Fehler()
    {
        Assert.Null(CoverLogo.Load(null));
        Assert.Null(CoverLogo.Load(" "));
    }

    [Fact]
    public void Zu_groß_RGL_0502()
    {
        // Gültiger PNG-Kopf, aber 1 Byte über 1 MB
        var big = new byte[CoverLogo.MaxBytes + 1];
        Png(4, 4).CopyTo(big, 0);
        var e = Assert.Throws<RiegelException>(() => CoverLogo.Load(File("gross.png", big)));
        Assert.Equal(RglCodesPdf.LogoTooLarge, e.Code);
        Assert.Contains("gross.png", e.Message);
    }

    [PdfTheory]
    [InlineData("text")]
    [InlineData("bmp")]
    [InlineData("gif")]
    [InlineData("svg")]
    [InlineData("leer")]
    [InlineData("riesig")]
    [InlineData("null-pixel")]
    [InlineData("abgeschnitten")]
    [InlineData("jpeg-ohne-sof")]
    public void Ungültig_RGL_0503(string kind)
    {
        byte[] bytes = kind switch
        {
            "text" => "kein Bild"u8.ToArray(),
            "bmp" => [0x42, 0x4D, 0x3A, 0, 0, 0, 0, 0, 0, 0, 0x36, 0, 0, 0, 0x28, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0x18, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0, 0],
            "gif" => "GIF89a\u0001\0\u0001\0\0\0\0;"u8.ToArray(),
            "svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray(),
            "leer" => [],
            // Dekomprimierungsbombe: Kopf behauptet 50 000 × 50 000 Pixel
            "riesig" => Png(4, 4, declaredWidth: 50_000, declaredHeight: 50_000),
            "null-pixel" => Png(4, 4, declaredWidth: 0, declaredHeight: 4),
            "abgeschnitten" => Png(64, 64)[..40],
            "jpeg-ohne-sof" => [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 1, 2, 3, 4, 5, 6, 0xFF, 0xD9],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var e = Assert.Throws<RiegelException>(() => CoverLogo.Load(File("logo-" + kind + ".png", bytes)));
        Assert.Equal(RglCodesPdf.LogoInvalid, e.Code);
    }

    [Fact]
    public void Fehlende_Datei_RGL_0504()
    {
        var e = Assert.Throws<RiegelException>(() => CoverLogo.Load(Path.Combine(_temp.Path, "fehlt.png")));
        Assert.Equal(RglCodesPdf.LogoUnreadable, e.Code);
    }

    [PdfFact]
    public void Abgelehntes_Logo_bricht_den_Bericht_ab_bevor_etwas_geschrieben_wird()
    {
        var data = SampleData.Report();
        var big = new byte[CoverLogo.MaxBytes + 10];
        Png(4, 4).CopyTo(big, 0);
        var logo = File("gross.png", big);
        var output = _temp.Sub("bericht");
        var e = Assert.Throws<RiegelException>(() =>
            Writer().WriteReport(data with { Header = data.Header with { LogoPath = logo } }, new PdfOptions(output, PerMachine: true)));
        Assert.Equal(RglCodesPdf.LogoTooLarge, e.Code);
        Assert.Empty(Directory.GetFiles(output));

        var invalid = File("kaputt.png", "kein Bild"u8.ToArray());
        e = Assert.Throws<RiegelException>(() =>
            Writer().WriteReport(data with { Header = data.Header with { LogoPath = invalid } }, new PdfOptions(output)));
        Assert.Equal(RglCodesPdf.LogoInvalid, e.Code);
        Assert.Empty(Directory.GetFiles(output));
    }

    [Fact]
    public void Zufällige_Bytes_geben_nur_RGL_Codes_nie_einen_Absturz()
    {
        if (!PdfFonts.IsAvailable) return;
        var random = new Random(4711);
        for (int i = 0; i < 200; i++)
        {
            var bytes = new byte[random.Next(0, 300)];
            random.NextBytes(bytes);
            // Mal mit echtem PNG- oder JPEG-Anfang, damit die Prüfung tiefer läuft
            if (i % 3 == 0 && bytes.Length > 40) Png(8, 8).AsSpan(0, 33).CopyTo(bytes);
            if (i % 3 == 1 && bytes.Length > 2) { bytes[0] = 0xFF; bytes[1] = 0xD8; }
            try
            {
                CoverLogo.Check(bytes, "zufall.png");
            }
            catch (RiegelException e)
            {
                Assert.Contains(e.Code, new[] { RglCodesPdf.LogoInvalid, RglCodesPdf.LogoTooLarge });
            }
        }
    }
}
