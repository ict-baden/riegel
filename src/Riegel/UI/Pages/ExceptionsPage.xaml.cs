// SPDX-License-Identifier: MIT
using System.Windows.Controls;
using System.Windows.Input;
using Riegel.ViewModels;

namespace Riegel.UI.Pages;

/// <summary>Ausnahmen (spec 7.8 Seite 4). Leertaste auf einer Zeile markiert sie für Genehmigung und Genehmigungsblatt.</summary>
public partial class ExceptionsPage : UserControl
{
    public ExceptionsPage()
    {
        InitializeComponent();
        RegisterKeys.Attach(List);
    }
}

/// <summary>Tastatur in den Registerlisten: Leertaste markiert, Eingabe bearbeitet.</summary>
internal static class RegisterKeys
{
    public static void Attach(ListView list)
    {
        list.PreviewKeyDown += (_, e) =>
        {
            if (list.SelectedItem is EntryRow row && e.Key == Key.Space && Keyboard.FocusedElement is ListViewItem)
            {
                row.IsChecked = !row.IsChecked;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && list.DataContext is ExceptionsViewModel ex && ex.EditCommand.CanExecute(null))
            {
                ex.EditCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && list.DataContext is TailoringViewModel tl && tl.EditCommand.CanExecute(null))
            {
                tl.EditCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}
