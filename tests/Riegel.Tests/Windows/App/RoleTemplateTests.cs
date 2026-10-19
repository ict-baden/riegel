// SPDX-License-Identifier: MIT
using System.Globalization;
using Riegel.ViewModels;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Schaltflächen der Rollenvorlagen im Zuschnitt (Review docs-ui-7): sie zeigten in jeder Sprache den Dateinamen
/// („engineering-station“, „historian-server“, „hmi“, „jump-host“) statt des Rollennamens aus den Sprachdateien.
/// </summary>
public sealed class RoleTemplateTests
{
    [Theory]
    [InlineData("de", "hmi", "Bedienstation (HMI)")]
    [InlineData("de", "jump-host", "Sprungserver")]
    [InlineData("en", "historian-server", "Historian / server")]
    public void Schaltfläche_zeigt_den_Rollennamen_der_Oberflächensprache(string language, string key, string expected)
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(language);
            var template = Assert.Single(RoleTemplate.Embedded(), t => t.Key == key);
            Assert.Equal(expected, template.Name);
        }
        finally { CultureInfo.CurrentUICulture = before; }
    }

    [Fact]
    public void Jede_eingebettete_Vorlage_hat_eine_Rolle_und_keinen_Dateinamen_als_Titel()
    {
        var templates = RoleTemplate.Embedded().ToList();
        Assert.Equal(["engineering-station", "historian-server", "hmi", "jump-host"], templates.Select(t => t.Key));
        Assert.Empty(RoleTemplate.Unmapped());
        foreach (var language in new[] { "en", "de", "es", "fr", "zh-Hans" })
        {
            var before = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo(language);
                foreach (var t in RoleTemplate.Embedded())
                    Assert.False(t.Name == t.Key || t.Name.StartsWith('['), $"{language}: {t.Key} → {t.Name}");
            }
            finally { CultureInfo.CurrentUICulture = before; }
        }
    }
}
