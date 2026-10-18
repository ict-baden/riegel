// SPDX-License-Identifier: MIT
using System.Windows.Controls;

namespace Riegel.UI.Pages;

/// <summary>Zuschnitt (spec 7.8 Seite 5).</summary>
public partial class TailoringPage : UserControl
{
    public TailoringPage()
    {
        InitializeComponent();
        RegisterKeys.Attach(List);
    }
}
