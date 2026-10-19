// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.Core.Localization;
using Riegel.Core.Plants;

namespace Riegel.Tests.Registers;

/// <summary>
/// Regressionstest (Review docs-ui-11): Daten in den Meldungen der Registerprüfung (RGL-0435…0439, 0443, 0446) folgen
/// in der Oberfläche dem Gebietsschema („Läuft am 05.11.2026 ab“ neben „prüfen bis 05.11.2026“), in JSON, Dateien und
/// Protokollen (invariante Kultur) bleiben sie ISO 8601.
/// </summary>
public sealed class LocalDateTests
{
    private static string Expiring(string ui, CultureInfo format)
    {
        var previous = (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture);
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(ui);
            CultureInfo.CurrentCulture = format;
            return RglCodesWorkflow.Message(RglCodesWorkflow.Expiring, new LocalDate(new DateOnly(2026, 11, 5)), 18);
        }
        finally
        {
            (CultureInfo.CurrentUICulture, CultureInfo.CurrentCulture) = previous;
        }
    }

    [Fact]
    public void Oberflaeche_nach_Gebietsschema_JSON_nach_ISO()
    {
        Assert.Equal("Läuft am 05.11.2026 ab (in 18 Tagen).", Expiring("de", CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("Expires on 11/5/2026 (in 18 days).", Expiring("en", CultureInfo.GetCultureInfo("en-US")));
        Assert.Equal("Läuft am 2026-11-05 ab (in 18 Tagen).", Expiring("de", CultureInfo.InvariantCulture));
        Assert.Equal("2026-11-05", new LocalDate(new DateOnly(2026, 11, 5)).ToString(null, CultureInfo.InvariantCulture));
    }
}
