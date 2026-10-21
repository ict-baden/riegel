// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Riegel.UI.Theme;
using Riegel.ViewModels;

namespace Riegel.UI;

/// <summary>
/// Hauptfenster: Navigation links, Seiten rechts (je ViewModel eine Ansicht), Meldungen unten. Tastenkürzel laufen
/// über <see cref="Shortcuts.Resolve"/> (genau Strg – AltGr löst nichts aus); Exporte lassen sich auf das Fenster ziehen.
/// </summary>
public partial class MainWindow : Window, IThemedWindow
{
    private readonly bool _screenshot;

    internal MainWindow(MainViewModel vm, bool screenshotMode = false)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;
        _screenshot = screenshotMode;
        SourceInitialized += (_, _) => { if (!_screenshot) ThemeManager.ApplyWindowChrome(this); };
        PreviewKeyDown += OnPreviewKeyDown;
        DragOver += OnDragOver;
        DragLeave += (_, _) => _drag = null;
        Drop += OnDrop;
        Closed += (_, _) => vm.Detach();
        Loaded += (_, _) => FocusPage();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedPage)) Dispatcher.BeginInvoke(FocusPage, System.Windows.Threading.DispatcherPriority.Loaded);
        };
    }

    internal MainViewModel ViewModel { get; }

    public void ThemeChanged()
    {
        if (!_screenshot) ThemeManager.ApplyWindowChrome(this);
    }

    /// <summary>Nach einem Seitenwechsel per Tastatur: Fokus auf das erste Element der Seite.</summary>
    private void FocusPage()
    {
        if (!IsKeyboardFocusWithin || Keyboard.FocusedElement is RadioButton) return;
        PageHost.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var (action, page) = Shortcuts.Resolve(key, Keyboard.Modifiers);
        if (action == ShortcutAction.None) return;
        var (handled, focus) = ViewModel.Execute(action, page);
        if (!handled) return;
        e.Handled = true;
        Dispatcher.BeginInvoke(() => ApplyFocus(focus), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void ApplyFocus(MainViewModel.FocusRequest focus)
    {
        var name = focus switch
        {
            MainViewModel.FocusRequest.Search => "SearchBox",
            MainViewModel.FocusRequest.ReportButton => "PlantReportButton",
            _ => null,
        };
        if (name is null) return;
        var target = Ui.FindChildren<FrameworkElement>(PageHost).FirstOrDefault(f => f.Name == name);
        if (target is TextBox box) box.SelectAll();
        target?.Focus();
    }

    // Ergebnis der Prüfung je Ziehvorgang: DragOver kommt bei jeder Mausbewegung, Ordner werden nur einmal gelesen
    private (IDataObject Data, IReadOnlyList<string> Files)? _drag;

    private IReadOnlyList<string> Importable(DragEventArgs e)
    {
        if (_drag is { } cached && ReferenceEquals(cached.Data, e.Data)) return cached.Files;
        IReadOnlyList<string> files = e.Data.GetData(DataFormats.FileDrop) is string[] dropped ? [.. dropped.Where(ImportDialogViewModel.IsImportable)] : [];
        _drag = (e.Data, files);
        return files;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = ViewModel.Session.CanWrite && Importable(e).Count > 0 ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        var files = Importable(e);
        _drag = null;
        if (files.Count == 0) return;
        e.Handled = true;
        ViewModel.Select(ViewModel.Start);
        _ = DropAsync(files);
    }

    private async Task DropAsync(IReadOnlyList<string> files)
    {
        try { await ViewModel.Start.ImportAsync(files); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { ViewModel.Session.Fail(ex); }
    }

    // ================= Screenshots =================

    /// <summary>Seite zeigen und in ihren festen Zustand bringen.</summary>
    internal async Task PrepareScreenshotAsync(string slug)
    {
        ViewModel.CloseNoticeCommand.Execute(null);
        var page = ViewModel.Page(slug);
        ViewModel.Select(page);
        await Ui.IdleAsync();
        await page.PrepareScreenshotAsync();
        await Ui.IdleAsync();
    }
}
