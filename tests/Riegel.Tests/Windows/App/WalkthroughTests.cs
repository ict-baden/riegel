// SPDX-License-Identifier: MIT
using System.Windows.Input;
using Riegel.Core.Model;
using Riegel.Core.Settings;
using Riegel.Demo;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.Tests.TestSupport;
using Riegel.UI;
using Riegel.ViewModels;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Bedienung nur mit der Tastatur (spec 7.8): Import → Ergebnis → Ausnahme → Genehmigungsblatt → Bericht, über dieselben
/// Kürzel und Befehle, die das Fenster auslöst. Läuft auf der Demo-Anlage in einem Temp-Ordner – „Diesen PC prüfen“
/// bleibt dort gesperrt; der Test fragt nie die Rechteerhöhung an und liest diesen Rechner nicht.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class WalkthroughTests
{
    private static (bool Handled, MainViewModel.FocusRequest Focus) Press(MainViewModel vm, Key key, ModifierKeys modifiers = ModifierKeys.Control)
    {
        var (action, page) = Shortcuts.Resolve(key, modifiers);
        Assert.NotEqual(ShortcutAction.None, action);
        return vm.Execute(action, page);
    }

    private static AppSession DemoSession() =>
        new(AppMode.Demo, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);

    [Fact]
    public void Import_Ergebnis_Ausnahme_Genehmigungsblatt_Bericht() => Sta.Run(async () =>
    {
        var errors = new List<Exception>();
        AsyncCommand.Failed = errors.Add;
        AsyncCommand.Strict = false;
        using var session = DemoSession();
        await session.OpenDemoAsync();
        var dialogs = new TestDialogs();
        var vm = new MainViewModel(session, dialogs);
        try
        {
            var plantFolder = session.Plant!.Folder;
            int imports = session.Plant.Imports.Count;

            // Strg+I: Exporte importieren (erhebende Rolle und Zeuge, Erhebungsprotokoll)
            dialogs.OnShow = d =>
            {
                var import = Assert.IsType<ImportDialogViewModel>(d);
                import.Files.Add(RepoPaths.Synthetic("win11-24h2-workgroup-admin"));
                import.CollectedByRole = DemoData.RoleTechnician;
                import.WitnessRole = DemoData.RoleOwner;
                import.WriteRecord = true;
                return true;
            };
            Assert.True(Press(vm, Key.I).Handled);
            await Sta.Until(() => session.Plant!.Imports.Count == imports + 1 && !session.IsBusy, "Import");
            Assert.Empty(errors);

            // Strg+3: Ergebnisse; Rechner und Befund wählen (Pfeiltasten in der Liste)
            Press(vm, Key.D3);
            Assert.Same(vm.Results, vm.SelectedPage);
            vm.Results.SelectHost(DemoData.ApprovedHost);
            var row = vm.Results.Rows.First(r => (r.Result.Flags & ResultFlags.Regression) != 0);
            vm.Results.Selected = row;

            // Strg+E: als bewusst offen begründen – vorbefüllt aus dem Regelwerk, Istwert als akzeptierter Wert
            int exceptions = session.Plant!.Registers.Exceptions.Exceptions.Count;
            dialogs.OnShow = d =>
            {
                var exception = Assert.IsType<ExceptionDialogViewModel>(d);
                Assert.True(exception.Prefilled);
                Assert.Equal(row.Result.Actual, exception.AcceptedValue);
                if (exception.NeedsReference) exception.Reference = DemoData.VendorDocument;
                if (exception.Justification.Trim().Length < 20) exception.Justification = "Bedienpanel der Linie braucht das noch – Hersteller fragt an.";
                exception.Confirmed = true;
                return true;
            };
            Assert.True(Press(vm, Key.E).Handled);
            await Sta.Until(() => session.Plant!.Registers.Exceptions.Exceptions.Count == exceptions + 1 && !session.IsBusy, "Ausnahme gespeichert");
            var created = session.Plant!.Registers.Exceptions.Exceptions.Single(e => e.RuleId == row.RuleId && e.Scope.Machines.Contains(DemoData.ApprovedHost));
            Assert.False(RegisterRules.Complete(created.Approval)); // beantragt, bis die Genehmigung eingetragen ist

            // Strg+4: Ausnahmen; Eintrag markieren (Leertaste) und Genehmigungsblatt erstellen
            Press(vm, Key.D4);
            Assert.Same(vm.Exceptions, vm.SelectedPage);
            vm.Exceptions.Rows.Single(r => r.Id == created.Id).IsChecked = true;
            await vm.Exceptions.SheetCommand.ExecuteAsync();
            var sheet = Assert.Single(dialogs.Revealed);
            Assert.StartsWith("Riegel-Ausnahmen-zur-Genehmigung_", Path.GetFileName(sheet));
            Assert.True(File.Exists(sheet));

            // Genehmigung eintragen (Rolle, Datum, Prüftermin, Nachweis)
            vm.Exceptions.Rows.Single(r => r.Id == created.Id).IsChecked = true;
            dialogs.OnShow = d =>
            {
                var approval = Assert.IsType<ApprovalDialogViewModel>(d);
                approval.Role = DemoData.RoleQa;
                approval.Record = "CHG-DEMO-0099";
                return true;
            };
            await vm.Exceptions.ApproveCommand.ExecuteAsync();
            Assert.True(RegisterRules.Complete(session.Plant!.Registers.Exceptions.Exceptions.Single(e => e.Id == created.Id).Approval));

            // Strg+R: Berichte, Fokus auf „Anlagenbericht erstellen“
            var (handled, focus) = Press(vm, Key.R);
            Assert.True(handled);
            Assert.Equal(MainViewModel.FocusRequest.ReportButton, focus);
            Assert.Same(vm.Reports, vm.SelectedPage);
            await vm.Reports.PlantReportCommand.ExecuteAsync();
            Assert.Contains(vm.Reports.Written, f => Path.GetFileName(f).StartsWith("Riegel-Report_", StringComparison.Ordinal));
            Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(plantFolder, "reports"), "Riegel-Report_*.pdf", SearchOption.AllDirectories));
            Assert.Empty(errors);
        }
        finally
        {
            vm.Detach();
            AsyncCommand.Failed = null;
        }
    });

    [Fact]
    public void Diesen_PC_pruefen_ist_in_Demo_und_Screenshots_gesperrt() => Sta.Run(async () =>
    {
        foreach (var mode in new[] { AppMode.Demo, AppMode.Screenshots })
        {
            using var session = new AppSession(mode, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);
            await session.OpenDemoAsync();
            Assert.False(session.CollectionAllowed);
            var vm = new MainViewModel(session, new TestDialogs());
            try
            {
                Assert.False(vm.Start.CheckCommand.CanExecute(null));
                Assert.False(vm.Start.CheckLimitedCommand.CanExecute(null));
                // F5 meldet nur, dass es gesperrt ist
                Assert.True(Press(vm, Key.F5, ModifierKeys.None).Handled);
                var e = await Assert.ThrowsAsync<RiegelException>(() => session.CheckThisPcAsync(elevate: true));
                Assert.Equal(RglCodesApp.NotInDemo, e.Code);
            }
            finally { vm.Detach(); }
        }
    });

    [Fact]
    public void Demo_im_normalen_Start_ist_ebenfalls_gesperrt() => Sta.Run(async () =>
    {
        using var session = new AppSession(AppMode.Normal, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);
        Assert.False(session.HasPlant);
        await session.OpenDemoAsync();
        Assert.True(session.Plant!.IsDemo);
        Assert.False(session.CollectionAllowed);
        await Assert.ThrowsAsync<RiegelException>(() => session.CheckThisPcAsync(elevate: false));
    });

    [Fact]
    public void Pfade_ohne_Benutzernamen()
    {
        var temp = Path.Combine(Path.GetTempPath(), "Riegel-Demo-1234", "Musterwerk");
        Assert.Equal(@"%TEMP%\Riegel-Demo-1234\Musterwerk", Texts.ShortPath(temp));
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riegel");
        Assert.Equal(@"%LocalAppData%\Riegel", Texts.ShortPath(local));
        Assert.Equal(local, Environment.ExpandEnvironmentVariables(Texts.ShortPath(local)), ignoreCase: true);
        Assert.Equal(@"D:\Anlagen\Linie-3", Texts.ShortPath(@"D:\Anlagen\Linie-3"));
    }
}
