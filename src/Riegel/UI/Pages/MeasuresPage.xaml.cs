// SPDX-License-Identifier: MIT
using System.Windows.Controls;
using System.Windows.Input;
using Riegel.ViewModels;

namespace Riegel.UI.Pages;

/// <summary>Maßnahmen (spec 7.8 Seite 6). Eingabe auf einer Zeile bearbeitet die Maßnahme.</summary>
public partial class MeasuresPage : UserControl
{
    public MeasuresPage()
    {
        InitializeComponent();
        List.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is MeasuresViewModel vm && vm.EditCommand.CanExecute(null))
            {
                vm.EditCommand.Execute(null);
                e.Handled = true;
            }
        };
    }
}
