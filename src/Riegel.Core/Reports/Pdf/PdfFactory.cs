// SPDX-License-Identifier: MIT
using Riegel.Core.Services;

namespace Riegel.Core.Reports.Pdf;

/// <summary>
/// Factory der PDF-Dokumente (Paket pdf; Signatur fest). Der Schreiber braucht keine anderen Dienste: alle Inhalte
/// kommen fertig aufgelöst in den Berichtsdaten (Paket workflow).
/// </summary>
public static class PdfFactory
{
    public static IPdfWriter Create(RiegelServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return new PdfWriter();
    }
}
