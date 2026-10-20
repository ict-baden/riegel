// SPDX-License-Identifier: MIT
namespace Riegel.Core.Model.Json;

/// <summary>
/// Nach dem Lesen mit <see cref="RiegelJson.Deserialize{T}(ReadOnlySpan{byte})"/>: fehlende Pflichtfelder (von Hand
/// bearbeitete oder ältere Dateien) durch leere Werte ersetzen, damit ein einzelner Eintrag ungültig wird statt die ganze
/// Datei unlesbar (die Prüfung der Register meldet ihn: RGL-0421, RGL-0424, RGL-0427, RGL-0444). Ändert die Form der
/// Datei und die Reihenfolge der Felder nicht – Prüfsummen und Inhalts-Hashes bleiben gleich.
/// </summary>
internal interface IAfterRead<out T>
{
    T AfterRead();
}
