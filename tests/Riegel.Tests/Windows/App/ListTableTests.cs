// SPDX-License-Identifier: MIT
using System.Windows;
using System.Windows.Controls;
using Riegel.UI;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Spaltenköpfe der Listen (Review docs-ui-6): „Geltungsbereich“, „Verantwortlich (Rolle)“ und „Responsable (función)“
/// wurden an der Mindestbreite aus dem XAML abgeschnitten, und die Layout-Prüfung der Screenshots meldete es nicht.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class ListTableTests
{
    private static ListTable.ColumnSpec Star(double factor, double min) => new(factor, true, min);
    private static ListTable.ColumnSpec Fixed(double width) => new(width, false, 0);

    [Fact]
    public void Sternspalte_unter_ihrer_Mindestbreite_bekommt_sie_und_die_anderen_teilen_den_Rest()
    {
        // Wie Maßnahmen „100, 2*:160, 1*:120, 1*:110“, der dritte Kopf braucht aber 190 px
        var widths = ListTable.Distribute(640, [Fixed(100), Star(2, 160), Star(1, 190), Star(1, 110)]);
        Assert.Equal(100, widths[0]);
        Assert.Equal(190, widths[2]);
        Assert.True(widths[1] >= 160 && widths[3] >= 110, string.Join(", ", widths));
        Assert.Equal(640, widths.Sum(), 3);
        // Genug Platz: reine Anteile wie bisher
        var wide = ListTable.Distribute(1000, [Fixed(100), Star(2, 160), Star(1, 120), Star(1, 110)]);
        Assert.Equal([100, 450, 225, 225], wide);
    }

    [Fact]
    public void Feste_Spalte_wächst_auf_die_Breite_ihres_Kopfs() =>
        Assert.Equal(130, ListTable.Distribute(500, [new ListTable.ColumnSpec(96, false, 130), Star(1, 0)])[0]);

    [Fact]
    public void Kopf_passt_ganz_in_die_Spalte_und_die_Layout_Prüfung_meldet_abgeschnittene_Köpfe() => Sta.Run(() =>
    {
        var view = new GridView();
        foreach (var title in new[] { "Fällig", "Maßnahme", "Verantwortlich (Rolle)", "Responsable (función)" })
            view.Columns.Add(new GridViewColumn { Header = title });
        ListTable.SetSortBy(view.Columns[2], "Owner");
        var list = new ListView { View = view };
        var root = new Border { Child = list };
        ListTable.SetColumns(list, "60, 2*:60, 1*:40, 1*:40");
        root.Measure(new Size(420, 300));
        root.Arrange(new Rect(0, 0, 420, 300));
        ListTable.Update(list);
        root.UpdateLayout();

        // Die langen Köpfe sind breiter als die Mindestbreite aus der Spaltenangabe
        Assert.True(ListTable.HeaderWidth(list, view.Columns[2]) > 40 && ListTable.HeaderWidth(list, view.Columns[3]) > 40);
        foreach (var column in view.Columns)
        {
            double needed = ListTable.HeaderWidth(list, column);
            Assert.True(column.Width >= needed, $"{ListTable.TitleOf(column)}: {column.Width} < {needed}");
        }
        Assert.Empty(LayoutAudit.ClippedHeaders(root, onlyVisible: false));

        // Gegenprobe: eine zu schmale Spalte meldet die Prüfung
        view.Columns[3].Width = 40;
        root.UpdateLayout();
        var problem = Assert.Single(LayoutAudit.ClippedHeaders(root, onlyVisible: false));
        Assert.Contains("Responsable (función)", problem);
        return Task.CompletedTask;
    }, timeoutSeconds: 60);
}
