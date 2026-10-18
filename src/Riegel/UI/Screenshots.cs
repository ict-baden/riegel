// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Riegel.Core.Model;
using Riegel.Core.Settings;
using Riegel.Demo;
using Riegel.Interop;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.UI.Dialogs;
using Riegel.UI.Theme;
using Riegel.ViewModels;

namespace Riegel.UI;

/// <summary>
/// <c>Riegel.exe --screenshots &lt;Ordner&gt; [--theme light|dark|both]</c> (Muster aus Zutritt, Richtlinie 08): baut die
/// Demo-Anlage auf, bringt jede Seite in einen festen Zustand und speichert sie als <c>&lt;seite&gt;-light.png</c> /
/// <c>&lt;seite&gt;-dark.png</c> (144 dpi), dazu die wichtigsten Dialoge. Nur Demo-Daten, nichts von diesem Rechner;
/// „Diesen PC prüfen“ ist gesperrt. Das Fenster liegt außerhalb des Bildschirms und wird nie aktiviert. WPF zeichnet
/// nur in einer angemeldeten, verbundenen Sitzung – sonst bricht der Lauf mit RGL-0602 ab. Sprache wie immer über
/// RIEGEL_LANGUAGE (Prüfung aller fünf Sprachen); gekürzte Texte meldet die Layout-Prüfung als „[layout]“.
/// </summary>
internal static class Screenshots
{
    private const double Scale = 1.5; // 144 dpi
    internal const double Width = 1280;
    internal const double Height = 800;

    /// <summary>Seiten in der Reihenfolge der Navigation (Dateiname = Kürzel).</summary>
    internal static readonly string[] Pages = ["start", "machines", "results", "exceptions", "tailoring", "measures", "comparison", "reports", "catalogue", "settings", "about"];

    public static async Task<int> RunAsync(string folder, string theme)
    {
        Directory.CreateDirectory(folder);
        bool[] variants = theme switch { "light" => [false], "dark" => [true], _ => [false, true] };
        Console.WriteLine($"Riegel {Core.About.AppInfo.Version} – screenshots ({CultureInfo.CurrentUICulture.Name}) → {folder}");

        using var session = new AppSession(AppMode.Screenshots, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);
        AsyncCommand.Strict = true;
        AsyncCommand.Failed = session.Fail;
        await session.OpenDemoAsync();

        int written = 0, findings = 0;
        foreach (var dark in variants)
        {
            ThemeManager.Initialize(forceDark: dark);
            var suffix = dark ? "dark" : "light";
            var vm = new MainViewModel(session, new NoDialogs(), screenshotMode: true);
            var window = CreateWindow(new MainWindow(vm, screenshotMode: true));
            try
            {
                foreach (var slug in Pages)
                {
                    await window.PrepareScreenshotAsync(slug);
                    await Task.Delay(250);
                    await Ui.IdleAsync();
                    Save(window, Path.Combine(folder, $"{slug}-{suffix}.png"));
                    findings += LayoutAudit.Report(slug, window);
                    Console.WriteLine($"  {slug}-{suffix}.png");
                    written++;
                }
                foreach (var (name, dialog) in DemoDialogs(vm))
                {
                    var host = CreateWindow(new DialogHost(dialog), 720, 0);
                    try
                    {
                        await Ui.IdleAsync();
                        await Task.Delay(150);
                        Save(host, Path.Combine(folder, $"{name}-{suffix}.png"), fixedSize: false);
                        findings += LayoutAudit.Report(name, host);
                        Console.WriteLine($"  {name}-{suffix}.png");
                        written++;
                    }
                    finally { host.Close(); }
                }
            }
            finally
            {
                window.Close();
                vm.Detach();
            }
        }
        Console.WriteLine($"{written} images written{(findings > 0 ? $", {findings} layout finding(s)" : "")}.");
        return 0;
    }

    /// <summary>Die Dialoge der Doku im Zustand der Demo (Ausnahme zur Verschlechterung, Genehmigung des Antrags).</summary>
    private static IEnumerable<(string Name, IDialogViewModel Dialog)> DemoDialogs(MainViewModel vm)
    {
        if (vm.Session.Plant is not { } plant) yield break;
        var results = vm.Results;
        results.SelectHost("HMI-L3-02");
        results.FilterStatus(null);
        var row = results.Rows.FirstOrDefault(r => (r.Result.Flags & ResultFlags.Regression) != 0) ?? results.Rows.FirstOrDefault(r => r.Status == ResultStatus.Fail);
        if (row is not null)
        {
            var dialog = ExceptionDialogViewModel.ForResult(plant, "HMI-L3-02", row.Result, row.Rule, row.Title);
            if (dialog.NeedsReference && string.IsNullOrWhiteSpace(dialog.Reference)) dialog.Reference = DemoData.VendorDocument;
            yield return ("exception-dialog", dialog);
        }
        var pending = plant.Registers.Exceptions.Exceptions.FirstOrDefault(e => !RegisterRules.Complete(e.Approval));
        if (pending is not null)
            yield return ("approval-dialog", new ApprovalDialogViewModel(Loc.T("Approval_Title"), Loc.T("Approval_Intro"),
                [$"{pending.Id} · {pending.RuleId} · {plant.Title(pending.RuleId, AppSession.Language)}"],
                plant.Plant.Settings.SignOff.Select(s => s.Role), plant.Today, withReview: true, record: "FB-DEMO-0012"));
    }

    /// <summary>Fenster außerhalb des Bildschirms in fester Größe, ohne Aktivierung und ohne Mica.</summary>
    internal static T CreateWindow<T>(T window, double width = Width, double height = Height) where T : Window
    {
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.ShowActivated = false;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -10000;
        window.Top = -10000;
        window.Width = 800;
        window.Height = 600;
        if (height <= 0) window.SizeToContent = SizeToContent.Height;
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(AllowLargerThanScreen);
        window.Show();
        window.Width = width;
        if (height > 0) window.Height = height;
        return window;
    }

    private static nint AllowLargerThanScreen(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != Native.WM_GETMINMAXINFO) return 0;
        var info = Marshal.PtrToStructure<Native.MINMAXINFO>(lParam);
        info.ptMaxSize = info.ptMaxTrackSize = new Native.POINT { X = 16_000, Y = 16_000 };
        Marshal.StructureToPtr(info, lParam, false);
        return 0;
    }

    internal static void Save(Window window, string file, bool fixedSize = true)
    {
        window.UpdateLayout();
        var root = (FrameworkElement)window.Content;
        double width = root.ActualWidth, height = root.ActualHeight;
        if (fixedSize && (Math.Abs(width - Width) > 1 || Math.Abs(height - Height) > 1))
            throw new RiegelException(RglCodesApp.ScreenshotFailed, RglCodesApp.Text(RglCodesApp.ScreenshotFailed) + $" ({width:0}×{height:0})");
        var bitmap = new RenderTargetBitmap((int)Math.Round(width * Scale), (int)Math.Round(height * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        var brush = Application.Current.TryFindResource("ApplicationBackgroundBrush") as Brush ?? Brushes.White;
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen()) dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        bitmap.Render(background);
        bitmap.Render(root);
        if (IsEmpty(bitmap)) throw new RiegelException(RglCodesApp.ScreenshotFailed, RglCodesApp.Text(RglCodesApp.ScreenshotFailed));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(file);
        encoder.Save(stream);
    }

    /// <summary>Ganz durchsichtig? (passiert in getrennten Sitzungen; leere Bilder dürfen nie in die Doku)</summary>
    private static bool IsEmpty(BitmapSource bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        for (int alpha = 3; alpha < pixels.Length; alpha += 4)
            if (pixels[alpha] != 0) return false;
        return true;
    }

    /// <summary>Dialoginhalt in einem Fenster wie <see cref="DialogWindow"/>, aber ohne Modalität (nur für Bilder).</summary>
    private sealed class DialogHost : Window
    {
        public DialogHost(IDialogViewModel vm)
        {
            DataContext = vm;
            SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
            SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
            SetResourceReference(FontFamilyProperty, "UiFont");
            var title = new TextBlock { Text = vm.Title, Margin = new Thickness(0, 0, 0, 12) };
            title.SetResourceReference(StyleProperty, "SubtitleText");
            var accept = new Button { Content = Loc.T("Dialog_Accept"), MinWidth = 120, Margin = new Thickness(0, 0, 8, 0), IsEnabled = vm.CanAccept };
            accept.SetResourceReference(StyleProperty, "AccentButton");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            buttons.Children.Add(accept);
            buttons.Children.Add(new Button { Content = Loc.T("Dialog_Cancel"), MinWidth = 120 });
            var root = new DockPanel();
            DockPanel.SetDock(title, Dock.Top);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(title);
            root.Children.Add(buttons);
            root.Children.Add(DialogWindow.ViewFor(vm));
            Content = new Border { Padding = new Thickness(24, 20, 24, 20), Child = root };
        }
    }

    /// <summary>Beim Erzeugen der Bilder gibt es keine Rückfragen.</summary>
    private sealed class NoDialogs : IDialogs
    {
        public string? PickFolder(string title, string? initial = null) => null;
        public IReadOnlyList<string> PickFiles(string title, string filter, bool multiple) => [];
        public string? SaveFile(string title, string filter, string fileName) => null;
        public bool Confirm(string title, string text) => false;
        public bool Show(IDialogViewModel dialog) => false;
        public void ShowText(string title, string text) { }
        public string? Reveal(string path) => null;
    }
}

/// <summary>
/// Layout-Prüfung für die Screenshots (Richtlinie 13 Punkt 4: 30 % Reserve, einmal mit Chinesisch prüfen): meldet
/// einzeilige Texte außerhalb von Tabellenzellen, die gekürzt oder abgeschnitten werden.
/// </summary>
internal static class LayoutAudit
{
    public static int Report(string page, Window window)
    {
        int count = 0;
        var dpi = VisualTreeHelper.GetDpi(window).PixelsPerDip;
        foreach (var tb in Ui.FindChildren<TextBlock>(window))
        {
            if (!tb.IsVisible || string.IsNullOrEmpty(tb.Text) || tb.TextWrapping != TextWrapping.NoWrap || tb.ActualWidth <= 0) continue;
            if (InsideList(tb)) continue;
            var ft = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, tb.FlowDirection,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch), tb.FontSize, Brushes.Black, dpi);
            double needed = ft.WidthIncludingTrailingWhitespace + tb.Padding.Left + tb.Padding.Right;
            if (needed > tb.ActualWidth + 1.5)
            {
                Console.WriteLine($"  [layout] {page}: „{Shorten(tb.Text)}“ needs {needed:0} px, has {tb.ActualWidth:0} px");
                count++;
            }
        }
        return count;
    }

    private static bool InsideList(DependencyObject d)
    {
        for (var p = VisualTreeHelper.GetParent(d); p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ListViewItem or ListBoxItem or ComboBox or DatePicker) return true;
        return false;
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "…";
}
