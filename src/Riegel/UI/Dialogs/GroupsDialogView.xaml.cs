// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using Riegel.ViewModels;

namespace Riegel.UI.Dialogs;

/// <summary>Inhalt des Dialogs „Gruppen und Rollen“ (ViewModel: GroupsDialogViewModel).</summary>
public partial class GroupsDialogView : UserControl
{
    public GroupsDialogView() => InitializeComponent();

    /// <summary>Zeile entfernen: die Schaltfläche wählt zuerst ihre Zeile aus.</summary>
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GroupRow row } && DataContext is GroupsDialogViewModel vm)
        {
            vm.Selected = row;
            vm.RemoveCommand.Execute(null);
        }
    }
}
