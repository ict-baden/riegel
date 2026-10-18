// SPDX-License-Identifier: MIT
using System.Windows;
using Microsoft.Win32;
using Riegel.Core.Services;
using Riegel.Session;
using Riegel.UI.Dialogs;
using Riegel.ViewModels;

namespace Riegel.UI;

/// <summary>Echte Dialoge der App (Ordner, Dateien, Fragen, Bearbeitungsfenster) über dem aktiven Fenster.</summary>
internal sealed class DialogService(IPlatform platform) : IDialogs
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public string? PickFolder(string title, string? initial = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initial) && Directory.Exists(initial)) dialog.InitialDirectory = initial;
        return dialog.ShowDialog(Owner) == true ? dialog.FolderName : null;
    }

    public IReadOnlyList<string> PickFiles(string title, string filter, bool multiple)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, Multiselect = multiple, CheckFileExists = true };
        return dialog.ShowDialog(Owner) == true ? dialog.FileNames : [];
    }

    public string? SaveFile(string title, string filter, string fileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = fileName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public bool Confirm(string title, string text) =>
        MessageBox.Show(Owner!, text, title, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool Show(IDialogViewModel dialog)
    {
        var window = DialogWindow.For(dialog);
        window.Owner = Owner;
        return window.ShowDialog() == true;
    }

    public void ShowText(string title, string text) => TextViewerWindow.Show(Owner, title, text);

    public string? Reveal(string path) => Shell.ShowInFolder(platform, path);
}
