// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text;
using System.Windows;
using Riegel.Core.About;
using Riegel.Core.Resources;
using Riegel.Resources;

namespace Riegel.UI;

/// <summary>Platzhalter-Fenster „Riegel – im Aufbau“ mit Info-Knopf (Gerüst; das Paket app ersetzt es).</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Title = UiStrings.MainWindow_Title;
        Heading.Text = UiStrings.MainWindow_Title;
        Body.Text = UiStrings.MainWindow_Body;
        InfoButton.Content = UiStrings.MainWindow_Info;
    }

    private void OnInfo(object sender, RoutedEventArgs e)
    {
        var text = new StringBuilder()
            .AppendLine($"{AppInfo.Name} {AppInfo.VersionWithBuild}")
            .AppendLine(AppInfo.Copyright)
            .AppendLine(AppInfo.LicenseUrl)
            .AppendLine()
            .AppendLine(CommonStrings.About_NoData)
            .AppendLine()
            .AppendLine(AppInfo.WebsiteUrl)
            .AppendLine(AppInfo.ImpressumUrl)
            .AppendLine(AppInfo.SupportUrl);
        MessageBox.Show(this, text.ToString(), UiStrings.Info_Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
