// SPDX-License-Identifier: MIT
using System.Windows.Controls;
using System.Windows.Input;
using Riegel.ViewModels;

namespace Riegel.UI.Pages;

/// <summary>Maschinen (spec 7.8 Seite 2). Eingabe auf einer Zeile öffnet die Ergebnisse des Rechners.</summary>
public partial class MachinesPage : UserControl
{
    public MachinesPage()
    {
        InitializeComponent();
        List.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && DataContext is MachinesViewModel vm && vm.ResultsCommand.CanExecute(null))
            {
                vm.ResultsCommand.Execute(null);
                e.Handled = true;
            }
        };
        List.MouseDoubleClick += (_, _) => { if (DataContext is MachinesViewModel vm && vm.ResultsCommand.CanExecute(null)) vm.ResultsCommand.Execute(null); };
    }
}
