// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Fehlercodes des Sammlers und der Windows-Anbindung: RGL-0100 bis RGL-0199 (spec 7.13). Gerüst: der Bereich und
    /// RGL-0102 aus spec 7.4 („Ziel nicht beschreibbar“); die übrigen legt das Paket collector an.
    /// </summary>
    public static class RglCodesCollector
    {
        public const int First = 100;
        public const int Last = 199;

        /// <summary>Ziel nicht beschreibbar – --out auf eine Freigabe verwenden (Exitcode 3, spec 7.4).</summary>
        public const string TargetNotWritable = "RGL-0102";

        /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
        public static readonly string[] All = { TargetNotWritable };
    }
}
