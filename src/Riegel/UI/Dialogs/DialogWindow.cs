// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Riegel.UI.Theme;
using Riegel.ViewModels;

namespace Riegel.UI.Dialogs;

/// <summary>
/// Rahmen aller Bearbeitungsdialoge: Inhalt je ViewModel, unten „Übernehmen“ (Standard) und „Abbrechen“ (Esc).
/// Tastatur: Tab durch alle Felder, Eingabe übernimmt (außer in mehrzeiligen Feldern), Esc bricht ab.
/// </summary>
internal sealed class DialogWindow : Window, IThemedWindow
{
    private static readonly Dictionary<Type, Func<UserControl>> Views = new()
    {
        [typeof(ExceptionDialogViewModel)] = () => new ExceptionDialogView(),
        [typeof(TailoringDialogViewModel)] = () => new TailoringDialogView(),
        [typeof(MeasureDialogViewModel)] = () => new MeasureDialogView(),
        [typeof(ApprovalDialogViewModel)] = () => new ApprovalDialogView(),
        [typeof(MachineDialogViewModel)] = () => new MachineDialogView(),
        [typeof(GroupsDialogViewModel)] = () => new GroupsDialogView(),
        [typeof(ImportDialogViewModel)] = () => new ImportDialogView(),
        [typeof(NewPlantDialogViewModel)] = () => new NewPlantDialogView(),
    };

    private readonly IDialogViewModel _vm;

    private DialogWindow(IDialogViewModel vm, UserControl view)
    {
        _vm = vm;
        DataContext = vm;
        Title = vm.Title + " – Riegel";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        MinWidth = 520;
        MaxHeight = SystemParameters.WorkArea.Height * 0.92;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        UseLayoutRounding = true;
        SetResourceReference(FontFamilyProperty, "UiFont");
        SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");

        var accept = new Button { Content = Loc.T("Dialog_Accept"), IsDefault = true, MinWidth = 120, Margin = new Thickness(0, 0, 8, 0) };
        accept.SetResourceReference(StyleProperty, "AccentButton");
        accept.SetBinding(IsEnabledProperty, new System.Windows.Data.Binding(nameof(IDialogViewModel.CanAccept)));
        accept.Click += (_, _) =>
        {
            if (_vm.Accept()) DialogResult = true;
        };
        var cancel = new Button { Content = Loc.T("Dialog_Cancel"), IsCancel = true, MinWidth = 120 };
        System.Windows.Automation.AutomationProperties.SetName(accept, Loc.T("Dialog_Accept"));
        System.Windows.Automation.AutomationProperties.SetName(cancel, Loc.T("Dialog_Cancel"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);

        var title = new TextBlock { Text = vm.Title, Margin = new Thickness(0, 0, 0, 12) };
        title.SetResourceReference(StyleProperty, "SubtitleText");

        var scroller = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false, Padding = new Thickness(0, 0, 6, 0) };
        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        DockPanel.SetDock(title, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(title);
        root.Children.Add(buttons);
        root.Children.Add(scroller);
        Content = root;

        Loaded += (_, _) => view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        SourceInitialized += (_, _) => ThemeChanged();
    }

    public static DialogWindow For(IDialogViewModel vm) =>
        new(vm, Views.TryGetValue(vm.GetType(), out var create) ? create() : throw new ArgumentException(vm.GetType().Name));

    /// <summary>Inhalt eines Dialogs ohne Fenster (Screenshots der Dialoge).</summary>
    public static UserControl ViewFor(IDialogViewModel vm)
    {
        var view = Views[vm.GetType()]();
        view.DataContext = vm;
        return view;
    }

    public void ThemeChanged()
    {
        ThemeManager.ApplyWindowChrome(this, mica: false);
        SetResourceReference(BackgroundProperty, "ApplicationBackgroundBrush");
    }
}
