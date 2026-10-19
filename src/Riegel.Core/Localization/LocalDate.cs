// SPDX-License-Identifier: MIT
using System.Globalization;

namespace Riegel.Core.Localization;

/// <summary>
/// Datum als Platzhalter in Meldungen (Review docs-ui-11, Richtlinie 13 Punkt 5): in einer echten Kultur das kurze
/// Datumsformat der Oberfläche (de „05.11.2026“, en „11/5/2026“), unter der invarianten Kultur ISO 8601 – so bleiben
/// JSON, Dateien und Protokolle bei „2026-11-05“, die Oberfläche folgt dem Gebietsschema.
/// </summary>
internal readonly record struct LocalDate(DateOnly Date) : IFormattable
{
    public string ToString(string? format, IFormatProvider? formatProvider) =>
        formatProvider is CultureInfo culture && culture.Name.Length > 0
            ? Date.ToString("d", culture)
            : Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public override string ToString() => ToString(null, CultureInfo.CurrentCulture);
}
