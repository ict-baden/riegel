// SPDX-License-Identifier: MIT
namespace Riegel.ViewModels;

/// <summary>
/// Dialoge hinter einer Schnittstelle (MVVM): die ViewModels fragen hier nach Dateien, Ordnern, Bestätigungen und
/// Bearbeitungsdialogen; die App zeigt echte Fenster, Tests antworten selbst.
/// </summary>
internal interface IDialogs
{
    /// <summary>Ordner wählen; null = abgebrochen.</summary>
    string? PickFolder(string title, string? initial = null);

    /// <summary>Dateien wählen (<paramref name="filter"/> im Format von Win32-Dialogen); leer = abgebrochen.</summary>
    IReadOnlyList<string> PickFiles(string title, string filter, bool multiple);

    /// <summary>Speicherort wählen; null = abgebrochen.</summary>
    string? SaveFile(string title, string filter, string fileName);

    /// <summary>Ja/Nein-Frage; true = ja.</summary>
    bool Confirm(string title, string text);

    /// <summary>Bearbeitungsdialog zeigen (ViewModel mit <see cref="IDialogViewModel"/>); true = übernommen.</summary>
    bool Show(IDialogViewModel dialog);

    /// <summary>Langen Text zeigen (Lizenzen).</summary>
    void ShowText(string title, string text);

    /// <summary>Gerade erzeugte Datei im Explorer zeigen; null bei Erfolg, sonst eine Meldung mit Code.</summary>
    string? Reveal(string path);
}

/// <summary>ViewModel eines Bearbeitungsdialogs.</summary>
internal interface IDialogViewModel
{
    string Title { get; }

    /// <summary>Darf übernommen werden (keine Fehler)?</summary>
    bool CanAccept { get; }

    /// <summary>Beim Übernehmen: Eingaben prüfen und das Ergebnis bilden; false = Dialog bleibt offen.</summary>
    bool Accept();
}
