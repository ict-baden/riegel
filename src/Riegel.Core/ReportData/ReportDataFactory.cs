// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

// Ordner ReportData/, Namensraum ReportBuilding: ein Namensraum „ReportData“ verdeckte sonst den Typ ReportData.
namespace Riegel.Core.ReportBuilding;

/// <summary>
/// Factory der Berichtsdaten (Paket workflow; Signatur fest): Anlagenbericht, Übersicht, Soll-Konfiguration,
/// Genehmigungsblatt und Erhebungsprotokoll aus Läufen, Registern und Exporten (<see cref="ReportDataBuilder"/>).
/// </summary>
public static class ReportDataFactory
{
    public static IReportDataBuilder Create(RiegelServices services) => new ReportDataBuilder(services);
}
