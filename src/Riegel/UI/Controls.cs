// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Riegel.Core.Model;

namespace Riegel.UI;

/// <summary>Einstellungszeile im Windows-11-Stil: Symbol, Titel, Beschreibung, Steuerelement rechts (wie Zutritt und Vitrine).</summary>
public class SettingCard : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard), new PropertyMetadata(null));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(SettingCard), new PropertyMetadata(null));
    /// <summary>Kennzeichen rechts neben dem Titel, z. B. „vorgegeben“ (Wert aus defaults.json).</summary>
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.Register(nameof(Badge), typeof(string), typeof(SettingCard), new PropertyMetadata(null));

    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string? Description { get => (string?)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string? Badge { get => (string?)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
}

/// <summary>Zahl auf der Navigation (z. B. offene Befunde). Leer = kein Abzeichen.</summary>
public static class Nav
{
    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(Nav), new PropertyMetadata(null));
    /// <summary>Farbe des Abzeichens: "High", "Medium" oder leer (neutral).</summary>
    public static readonly DependencyProperty BadgeToneProperty =
        DependencyProperty.RegisterAttached("BadgeTone", typeof(string), typeof(Nav), new PropertyMetadata(""));

    public static string? GetBadge(DependencyObject o) => (string?)o.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject o, string? value) => o.SetValue(BadgeProperty, value);
    public static string GetBadgeTone(DependencyObject o) => (string)o.GetValue(BadgeToneProperty);
    public static void SetBadgeTone(DependencyObject o, string value) => o.SetValue(BadgeToneProperty, value);
}

/// <summary>Farbiges Status-Abzeichen („● Nicht erfüllt“); Farben per Trigger im Stil (wechseln mit Hell/Dunkel).</summary>
public sealed class StatusPill : Control
{
    public static readonly DependencyProperty StatusProperty =
        DependencyProperty.Register(nameof(Status), typeof(ResultStatus), typeof(StatusPill),
            new PropertyMetadata(ResultStatus.Info, (d, e) => d.SetValue(LabelPropertyKey, Loc.C("Status_" + (ResultStatus)e.NewValue))));
    private static readonly DependencyPropertyKey LabelPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(StatusPill), new PropertyMetadata(""));
    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;

    static StatusPill() => FocusableProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(false));

    public StatusPill() => SetValue(LabelPropertyKey, Loc.C("Status_" + Status));

    public ResultStatus Status { get => (ResultStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public string Label => (string)GetValue(LabelProperty);
}

/// <summary>Schwere-Abzeichen („● Hoch“); <see cref="Compact"/> zeigt nur den Punkt.</summary>
public sealed class SeverityPill : Control
{
    public static readonly DependencyProperty SeverityProperty =
        DependencyProperty.Register(nameof(Severity), typeof(Severity), typeof(SeverityPill),
            new PropertyMetadata(Severity.Low, (d, e) => d.SetValue(LabelPropertyKey, Loc.C("Severity_" + (Severity)e.NewValue))));
    private static readonly DependencyPropertyKey LabelPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(SeverityPill), new PropertyMetadata(""));
    public static readonly DependencyProperty LabelProperty = LabelPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CompactProperty =
        DependencyProperty.Register(nameof(Compact), typeof(bool), typeof(SeverityPill), new PropertyMetadata(false));

    static SeverityPill() => FocusableProperty.OverrideMetadata(typeof(SeverityPill), new FrameworkPropertyMetadata(false));

    public SeverityPill() => SetValue(LabelPropertyKey, Loc.C("Severity_" + Severity));

    public Severity Severity { get => (Severity)GetValue(SeverityProperty); set => SetValue(SeverityProperty, value); }
    public string Label => (string)GetValue(LabelProperty);
    public bool Compact { get => (bool)GetValue(CompactProperty); set => SetValue(CompactProperty, value); }
}

/// <summary>Kleines Kennzeichen mit Farbton: "Good", "Caution", "Critical", "Info" oder leer (neutral).</summary>
public sealed class TonePill : Control
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TonePill), new PropertyMetadata(""));
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(TonePill), new PropertyMetadata(""));

    static TonePill() => FocusableProperty.OverrideMetadata(typeof(TonePill), new FrameworkPropertyMetadata(false));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}

/// <summary>Kennzahl-Kachel: große Zahl, Beschriftung, optional Symbol und Farbton.</summary>
public sealed class StatTile : Control
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(StatTile), new PropertyMetadata("–"));
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatTile), new PropertyMetadata(""));
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(StatTile), new PropertyMetadata(null));
    /// <summary>"High", "Medium", "Low", "Good", "Caution" oder leer (Akzent).</summary>
    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(string), typeof(StatTile), new PropertyMetadata(""));

    static StatTile() => FocusableProperty.OverrideMetadata(typeof(StatTile), new FrameworkPropertyMetadata(false));

    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    public string Tone { get => (string)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
}

/// <summary>Leerer Text → sichtbar (Platzhalter über Eingabefeldern), sonst eingeklappt; Parameter "invert" kehrt um.</summary>
public sealed class EmptyToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>true → sichtbar, false → eingeklappt; Parameter "invert" kehrt um.</summary>
public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is true) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>null → eingeklappt, sonst sichtbar; Parameter "invert" kehrt um.</summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => (value is null || value as string == "") ^ (parameter as string == "invert") ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Zahl 0–100 → Breite eines Balkens in der Breite des Parameters (Standard 100).</summary>
public sealed class PercentWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double max = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var m) ? m : 100;
        return value is int i ? Math.Clamp(i, 0, 100) * max / 100.0 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Mausrad an die Seite weitergeben, wenn eine eingebettete Liste am Anfang oder Ende ist (aus Zutritt).</summary>
public static class Wheel
{
    public static readonly DependencyProperty BubbleProperty =
        DependencyProperty.RegisterAttached("Bubble", typeof(bool), typeof(Wheel), new PropertyMetadata(false, OnBubbleChanged));

    public static bool GetBubble(DependencyObject o) => (bool)o.GetValue(BubbleProperty);
    public static void SetBubble(DependencyObject o, bool value) => o.SetValue(BubbleProperty, value);

    private static void OnBubbleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if (e.NewValue is true) element.PreviewMouseWheel += OnPreviewMouseWheel;
        else element.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d || e.Handled) return;
        var inner = Ui.FindChild<ScrollViewer>(d);
        bool atTop = inner is null || inner.VerticalOffset <= 0;
        bool atBottom = inner is null || inner.VerticalOffset >= inner.ScrollableHeight;
        if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom)) return;
        e.Handled = true;
        if (VisualTreeHelper.GetParent(d) is UIElement parent)
            parent.RaiseEvent(new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender });
    }
}

/// <summary>Kleine Helfer für Ansichten.</summary>
internal static class Ui
{
    /// <summary>Wartet, bis WPF Layout und Rendern erledigt hat (Screenshots, Fokus nach dem Laden).</summary>
    public static async Task IdleAsync() =>
        await System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

    public static T? FindChild<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int n = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T t) return t;
                queue.Enqueue(child);
            }
        }
        return null;
    }

    public static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            int n = VisualTreeHelper.GetChildrenCount(current);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(current, i);
                if (child is T t) yield return t;
                queue.Enqueue(child);
            }
        }
    }
}
