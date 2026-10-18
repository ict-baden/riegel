// SPDX-License-Identifier: MIT
// Wird auch in Riegel.Platform.Windows übersetzt (net10.0-windows): nur Sprachmittel und Typen, die es in net35 gibt.
namespace Riegel.Collector.Collection
{
    /// <summary>
    /// Fehlercodes des Sammlers und der Windows-Anbindung: RGL-0100 bis RGL-0199 (spec 7.13). Texte: im Sammler in
    /// messages.&lt;sprache&gt;.txt, in der Windows-Anbindung in PlatformStrings (Error_RGL01nn).
    /// </summary>
    public static class RglCodesCollector
    {
        public const int First = 100;
        public const int Last = 199;

        /// <summary>Unerwarteter Fehler des Sammlers (Programmfehler); nichts oder unvollständig geschrieben.</summary>
        public const string Unexpected = "RGL-0100";

        /// <summary>Unbekannte Option oder fehlender Wert (Exitcode 3, nichts erfasst).</summary>
        public const string InvalidArguments = "RGL-0101";

        /// <summary>Ziel nicht beschreibbar – --out auf eine Freigabe verwenden (Exitcode 3, spec 7.4).</summary>
        public const string TargetNotWritable = "RGL-0102";

        /// <summary>Der Export ließ sich nach dem Erfassen nicht schreiben.</summary>
        public const string ExportWriteFailed = "RGL-0103";

        /// <summary>Eingebetteter Erfassungsplan fehlt oder ist ungültig (Programmfehler; nichts erfasst).</summary>
        public const string PlanUnreadable = "RGL-0104";

        /// <summary>Die Prüfsummendatei (.sha256) ließ sich nicht schreiben.</summary>
        public const string SidecarWriteFailed = "RGL-0105";

        /// <summary>Bordmittel-Erhebung: der Ordner &lt;HOST&gt;_&lt;Datum&gt;.riegel-builtin existiert schon (collect-builtin.cmd).</summary>
        public const string BuiltinBundleExists = "RGL-0106";

        /// <summary>Übergabe: ungültiges Kennzeichen (nur Buchstaben, Ziffern, „-“, höchstens 64 Zeichen).</summary>
        public const string HandoffInvalidToken = "RGL-0110";

        /// <summary>Übergabe: der Ordner existiert schon (wird nie wiederverwendet).</summary>
        public const string HandoffExists = "RGL-0111";

        /// <summary>Übergabe: ein Teil des Pfads ist ein Reparse-Punkt (Verknüpfung/Junction) – abgelehnt.</summary>
        public const string HandoffReparsePoint = "RGL-0112";

        /// <summary>Rechteerhöhung abgebrochen (UAC-Abfrage verneint).</summary>
        public const string ElevationCancelled = "RGL-0113";

        /// <summary>Rechteerhöhung ließ sich nicht starten.</summary>
        public const string ElevationFailed = "RGL-0114";

        /// <summary>Der erhöhte Lauf endete ohne Export im Übergabeordner.</summary>
        public const string HandoffNoSnapshot = "RGL-0115";

        /// <summary>Erhöht mit einem anderen Konto („über die Schulter“): anderes Profil, Übergabe nicht möglich.</summary>
        public const string HandoffDifferentProfile = "RGL-0116";

        /// <summary>Öffnen abgelehnt: nur von Riegel erzeugte Dateiarten und http/https-Links.</summary>
        public const string ShellOpenRefused = "RGL-0120";

        /// <summary>Öffnen mit der Shell fehlgeschlagen.</summary>
        public const string ShellOpenFailed = "RGL-0121";

        /// <summary>Sammler-Paket (collector\ neben der App) fehlt – z. B. bei der Einzeldatei-Exe.</summary>
        public const string CollectorBundleMissing = "RGL-0130";

        /// <summary>Im Zielordner liegen schon Dateien des Sammler-Pakets (werden nicht überschrieben).</summary>
        public const string CollectorBundleTargetExists = "RGL-0131";

        /// <summary>Erfassung dieses Rechners fehlgeschlagen (App/Befehlszeile).</summary>
        public const string CollectionFailed = "RGL-0140";

        /// <summary>Alle Codes dieses Bereichs (für docs/error-codes.md und Tests).</summary>
        public static readonly string[] All =
        {
            Unexpected, InvalidArguments, TargetNotWritable, ExportWriteFailed, PlanUnreadable, SidecarWriteFailed, BuiltinBundleExists,
            HandoffInvalidToken, HandoffExists, HandoffReparsePoint, ElevationCancelled, ElevationFailed, HandoffNoSnapshot,
            HandoffDifferentProfile, ShellOpenRefused, ShellOpenFailed, CollectorBundleMissing, CollectorBundleTargetExists,
            CollectionFailed,
        };
    }
}
