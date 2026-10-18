// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Riegel.Interop;

namespace Riegel.UI.Theme;

/// <summary>
/// Hell/Dunkel nach Windows (oder fest nach Einstellung), Fluent-Steuerelemente und Fensterrahmen – übernommen von
/// Zutritt, damit die Programme gleich aussehen. Fluent aus WPF (.NET 9+) über Application.ThemeMode.
/// </summary>
public static class ThemeManager
{
    private const string FluentUri = "pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{0}.xaml";
    private const string OwnUri = "pack://application:,,,/Riegel;component/UI/Theme/{0}.xaml";

    private static readonly string[] SelectedBackgroundKeys =
        ["DataGridRowSelectedBackgroundThemeBrush", "ListBoxItemSelectedBackgroundThemeBrush", "ListBoxItemBackgroundSelectedPressedThemeBrush"];
    private static readonly string[] SelectedHoverKeys =
        ["ListBoxItemSelectedBackgroundPointerOverThemeBrush", "ListBoxItemSelectedBackgroundPressedThemeBrush"];
    private static readonly string[] SelectedForegroundKeys =
        ["DataGridRowSelectedForegroundThemeBrush", "ListBoxItemSelectedForegroundThemeBrush"];

    private static ResourceDictionary? _fluent;
    private static ResourceDictionary? _own;
    private static bool _following;
    private static bool? _forced;

    public static bool IsDark { get; private set; }

    /// <summary>Hell/Dunkel festlegen. <paramref name="forceDark"/> = null folgt Windows (auch bei späteren Wechseln).</summary>
    public static void Initialize(bool? forceDark = null)
    {
        _forced = forceDark;
        IsDark = forceDark ?? !SystemTheme.AppsUseLightTheme;
        ApplyTheme(allowHighContrast: forceDark is null);
        if (_following) return;
        _following = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (_forced is not null || e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color)) return;
            IsDark = !SystemTheme.AppsUseLightTheme;
            Application.Current?.Dispatcher.BeginInvoke(() => ApplyTheme(allowHighContrast: true));
        };
    }

    /// <summary>Einstellung „theme“ (light, dark, null = System) in den Schalter für <see cref="Initialize"/> übersetzen.</summary>
    public static bool? ForceDarkFor(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "dark" => true,
        "light" => false,
        _ => null,
    };

    private static void ApplyTheme(bool allowHighContrast)
    {
        var app = Application.Current;
        if (app is null) return;
        var merged = app.Resources.MergedDictionaries;
        if (_own is not null) merged.Remove(_own);

        bool highContrast = allowHighContrast && SystemParameters.HighContrast;
#pragma warning disable WPF0001 // ThemeMode ist in WPF als Vorschau gekennzeichnet
        app.ThemeMode = highContrast ? ThemeMode.System : IsDark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
        _fluent = merged.FirstOrDefault(d => d.Source?.OriginalString.Contains("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) == true);
        if (_fluent is null)
        {
            _fluent = new ResourceDictionary { Source = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, FluentUri, highContrast ? "HC" : IsDark ? "Dark" : "Light")) };
            merged.Insert(0, _fluent);
        }
        _own = new ResourceDictionary { Source = new Uri(string.Format(System.Globalization.CultureInfo.InvariantCulture, OwnUri, IsDark ? "Dark" : "Light")) };
        merged.Insert(merged.IndexOf(_fluent) + 1, _own);

        BuildDerivedStyles();
        var accent = (_fluent["AccentFillColorDefaultBrush"] as SolidColorBrush)?.Color ?? Color.FromRgb(0x00, 0x67, 0xC0);
        app.Resources["AccentSubtle"] = Freeze(new SolidColorBrush(Color.FromArgb(0x33, accent.R, accent.G, accent.B)));

        // Auswahl in Listen dezent getönt statt voll mit Akzent gefüllt (sonst heller Text auf hellem Akzent im Dunkeln)
        var selected = Freeze(new SolidColorBrush(Color.FromArgb(0x3D, accent.R, accent.G, accent.B)));
        var selectedHover = Freeze(new SolidColorBrush(Color.FromArgb(0x52, accent.R, accent.G, accent.B)));
        var text = _fluent["TextFillColorPrimaryBrush"] as Brush ?? Brushes.Black;
        foreach (var key in SelectedBackgroundKeys) app.Resources[key] = selected;
        foreach (var key in SelectedHoverKeys) app.Resources[key] = selectedHover;
        foreach (var key in SelectedForegroundKeys) app.Resources[key] = text;

        foreach (Window w in app.Windows)
            if (w is IThemedWindow t) t.ThemeChanged();
    }

    /// <summary>Stile auf Basis von Fluent (im Code, weil BasedOn per StaticResource über Wörterbücher hinweg unzuverlässig auflöst).</summary>
    private static void BuildDerivedStyles()
    {
        var r = Application.Current.Resources;
        var button = _fluent![typeof(Button)] as Style;
        var accent = _fluent["AccentButtonStyle"] as Style ?? button;
        var textBox = _fluent[typeof(TextBox)] as Style;

        r["AccentButton"] = new Style(typeof(Button), accent);

        var ghost = new Style(typeof(Button), button);
        ghost.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        ghost.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        ghost.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        r["GhostButton"] = ghost;

        var multi = new Style(typeof(TextBox), textBox);
        multi.Setters.Add(new Setter(TextBoxBase.AcceptsReturnProperty, true));
        multi.Setters.Add(new Setter(TextBox.TextWrappingProperty, TextWrapping.Wrap));
        multi.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 72.0));
        multi.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));
        multi.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto));
        r["MultiLineBox"] = multi;

        // Schlanke Tabelle (ListView + GridView, siehe ListTable) im Stil von Zutritt
        var listItem = new Style(typeof(ListViewItem), _fluent[GridView.GridViewItemContainerStyleKey] as Style);
        listItem.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 32.0));
        listItem.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        var list = new Style(typeof(ListView), _fluent[typeof(ListView)] as Style);
        list.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        list.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        list.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
        list.Setters.Add(new Setter(ListBox.SelectionModeProperty, SelectionMode.Single));
        list.Setters.Add(new Setter(Selector.IsSynchronizedWithCurrentItemProperty, false));
        list.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        list.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        list.Setters.Add(new Setter(ScrollViewer.CanContentScrollProperty, true));
        list.Setters.Add(new Setter(VirtualizingPanel.IsVirtualizingProperty, true));
        list.Setters.Add(new Setter(VirtualizingPanel.VirtualizationModeProperty, VirtualizationMode.Recycling));
        list.Setters.Add(new Setter(VirtualizingPanel.ScrollUnitProperty, ScrollUnit.Pixel));
        r["ListTableItem"] = listItem;
        r["ListTable"] = list;
        var listHeader = new Style(typeof(GridViewColumnHeader), _fluent["DefaultGridViewColumnHeaderStyle"] as Style);
        listHeader.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        listHeader.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        listHeader.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension("DividerStrokeColorDefaultBrush")));
        var padding = new Trigger { Property = GridViewColumnHeader.RoleProperty, Value = GridViewColumnHeaderRole.Padding };
        padding.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
        listHeader.Triggers.Add(padding);
        r["ListTableHeader"] = listHeader;
    }

    private static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Woher Hell/Dunkel kommt (Windows-Einstellung; austauschbar für Tests).</summary>
    public static ISystemTheme SystemTheme { get; set; } = new WindowsSystemTheme();

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    /// <summary>Dunkle Titelleiste und – unter Windows 11 – Mica-Hintergrund.</summary>
    public static void ApplyWindowChrome(Window w, bool mica = true)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == 0) return;

        int dark = IsDark ? 1 : 0;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

        if (mica && IsWindows11)
        {
            int backdrop = 2; // DWMSBT_MAINWINDOW (Mica)
            if (Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0)
            {
                w.Background = Brushes.Transparent;
                if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: { } ct })
                    ct.BackgroundColor = Colors.Transparent;
                var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
                return;
            }
        }
        w.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
    }
}

/// <summary>Fenster, die bei einem Wechsel zwischen Hell und Dunkel ihren Rahmen neu setzen.</summary>
public interface IThemedWindow
{
    void ThemeChanged();
}

/// <summary>Hell oder dunkel, wie das System es für Apps vorgibt (Windows-Eigenes hinter einer Schnittstelle, Richtlinie 02).</summary>
public interface ISystemTheme
{
    bool AppsUseLightTheme { get; }
}

/// <summary>Windows: HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize, Wert AppsUseLightTheme (nur gelesen).</summary>
public sealed class WindowsSystemTheme : ISystemTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public bool AppsUseLightTheme
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is not int i || i == 1;
            }
            catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return true;
            }
        }
    }
}
