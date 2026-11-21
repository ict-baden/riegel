// SPDX-License-Identifier: MIT
using System.Windows.Input;
using Riegel.Core.Model;
using Riegel.Core.Settings;
using Riegel.Platform.Windows;
using Riegel.Session;
using Riegel.Tests.TestSupport;
using Riegel.UI;
using Riegel.ViewModels;

namespace Riegel.Tests.Windows.App;

/// <summary>
/// Oberfläche bei Fehlern und Nebenläufigkeit: Gruppen-IDs nie wiederverwenden, Sperre während einer Arbeit, Vergleich
/// mit unlesbarem Export, Sprung zu einer Ausnahme bei aktiver Suche, Ordner ohne Exporte im Import-Dialog.
/// Nur Demo-Anlage und Temp-Ordner; nie „Diesen PC prüfen“.
/// </summary>
[Collection(AppCollection.Name)]
public sealed class UiRobustnessTests
{
    private static AppSession DemoSession() =>
        new(AppMode.Demo, PlatformFactory.Create(), RiegelSettings.Empty, RiegelSettings.Empty, saveSettings: false);

    private static readonly IReadOnlyList<MachineGroup> TwoGroups = [new("g-1", "Bedienstationen", MachineRole.Hmi), new("g-2", "Server", MachineRole.Server)];

    [Fact]
    public void Neue_Gruppe_bekommt_nie_die_ID_einer_geloeschten()
    {
        var dialog = new GroupsDialogViewModel(TwoGroups);
        dialog.Selected = dialog.Rows.Single(r => r.Id == "g-2");
        dialog.RemoveCommand.Execute(null);
        dialog.AddCommand.Execute(null);
        Assert.Equal("g-3", dialog.Selected!.Id);
        // im selben Dialog erneut entfernt und angelegt: wieder eine neue
        dialog.RemoveCommand.Execute(null);
        dialog.AddCommand.Execute(null);
        Assert.Equal("g-4", dialog.Selected!.Id);
    }

    [Fact]
    public void Neue_Gruppe_meidet_IDs_aus_Registern_und_Schreibweisen()
    {
        // in einer früheren Sitzung gelöscht, aber noch im Geltungsbereich einer Ausnahme
        var dialog = new GroupsDialogViewModel([new("G-1", "A", MachineRole.Hmi)], ["g-1", "g-7", "eigene-id"]);
        dialog.AddCommand.Execute(null);
        Assert.Equal("g-8", dialog.Selected!.Id);
        Assert.Equal(2, dialog.Rows.Select(r => r.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Vergebene_Gruppen_IDs_umfassen_Anlage_Rechner_und_Register() => Sta.Run(async () =>
    {
        using var session = DemoSession();
        await session.OpenDemoAsync();
        var plant = session.Plant!;
        var used = MachinesViewModel.UsedGroupIds(plant);
        foreach (var g in plant.Plant.Groups) Assert.Contains(g.Id, used);
        foreach (var id in plant.Registers.Exceptions.Exceptions.SelectMany(e => e.Scope.Groups)) Assert.Contains(id, used);
    });

    [Fact]
    public void Ueberlappende_Arbeit_haelt_die_Sperre_bis_zum_Ende_der_letzten() => Sta.Run(async () =>
    {
        using var session = DemoSession();
        await session.OpenDemoAsync();
        var vm = new MainViewModel(session, new TestDialogs());
        try
        {
            using var releaseFirst = new ManualResetEventSlim();
            using var releaseSecond = new ManualResetEventSlim();
            var first = session.Work("eins", () => releaseFirst.Wait(TimeSpan.FromSeconds(30)));
            var second = session.Work("zwei", () => releaseSecond.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(session.IsBusy);
            Assert.False(vm.IsIdle);

            // Kürzel während der Arbeit: nichts starten, Seite nicht wechseln
            var page = vm.SelectedPage;
            var (action, index) = Shortcuts.Resolve(Key.D3, ModifierKeys.Control);
            Assert.True(vm.Execute(action, index).Handled);
            Assert.Same(page, vm.SelectedPage);

            releaseFirst.Set();
            await first;
            Assert.True(session.IsBusy, "die zweite Arbeit läuft noch");
            releaseSecond.Set();
            await second;
            Assert.False(session.IsBusy);
            Assert.True(vm.IsIdle);
            Assert.Equal("", session.BusyText);

            Assert.True(vm.Execute(action, index).Handled);
            Assert.Same(vm.Results, vm.SelectedPage);
        }
        finally { vm.Detach(); }
    });

    // Exklusiv sperren, mit kurzer Wiederholung: Direkt nach dem Schreiben der Demo-Daten hält auf dem Runner oft
    // noch der Virenscanner oder das Laden im Hintergrund die Datei (IOException „being used by another process“).
    private static async Task<FileStream> LockExclusiveAsync(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None); }
            catch (IOException) when (attempt < 50) { await Task.Delay(100); }
        }
    }

    [Fact]
    public void Vergleich_mit_gesperrtem_Export_zeigt_den_Grund() => Sta.Run(async () =>
    {
        using var session = DemoSession();
        await session.OpenDemoAsync();
        var vm = new MainViewModel(session, new TestDialogs());
        try
        {
            var plant = session.Plant!;
            var machine = plant.Machines.First(m => m.Latest is not null && plant.SnapshotPath(m.Latest) is not null);
            vm.Comparison.SelectHost(machine.HostName);
            var path = plant.SnapshotPath(machine.Latest!)!;
            using (await LockExclusiveAsync(path))
                await vm.Comparison.RefreshAsync();
            Assert.StartsWith("RGL-", vm.Comparison.EmptyText, StringComparison.Ordinal);
            Assert.False(vm.Comparison.HasComparison);

            await vm.Comparison.RefreshAsync();
            Assert.DoesNotContain("RGL-0007", vm.Comparison.EmptyText, StringComparison.Ordinal);
        }
        finally { vm.Detach(); }
    });

    [Fact]
    public void Sprung_zur_Ausnahme_setzt_die_Suche_zurueck() => Sta.Run(async () =>
    {
        using var session = DemoSession();
        await session.OpenDemoAsync();
        var vm = new MainViewModel(session, new TestDialogs());
        try
        {
            var id = session.Plant!.Registers.Exceptions.Exceptions[0].Id;
            vm.Exceptions.Search = "kein-eintrag-passt-hierzu";
            Assert.Empty(vm.Exceptions.Rows);
            vm.Exceptions.SelectEntry(id);
            Assert.Equal("", vm.Exceptions.Search);
            Assert.Equal(id, vm.Exceptions.Selected?.Id);

            // unbekannte ID: die Auswahl bleibt stehen, das Detail wird nicht leer
            vm.Exceptions.SelectEntry("EX-9999");
            Assert.Equal(id, vm.Exceptions.Selected?.Id);
        }
        finally { vm.Detach(); }
    });

    [Fact]
    public void Import_Dialog_meldet_Ordner_ohne_Exporte()
    {
        using var temp = new TempFolder();
        var empty = temp.Sub("stick");
        Directory.CreateDirectory(Path.Combine(empty, "Exporte"));
        var dialogs = new TestDialogs { OnPickFolder = _ => empty };
        var dialog = new ImportDialogViewModel(dialogs, [], null);
        dialog.AddFolderCommand.Execute(null);
        Assert.Empty(dialog.Files);
        Assert.StartsWith(Riegel.Commands.RglCodesCli.NoExportsInFolder + ": ", dialog.Error, StringComparison.Ordinal);
        Assert.False(dialog.CanAccept);

        // ein Ordner mit Export: der Hinweis verschwindet
        var export = SnapshotBuilder.Windows11Workgroup("W11-TEST").WriteTo(Path.Combine(empty, "Exporte"));
        dialogs.OnPickFolder = _ => Path.Combine(empty, "Exporte");
        dialog.AddFolderCommand.Execute(null);
        Assert.Null(dialog.Error);
        Assert.Equal(Path.GetFullPath(export), Assert.Single(dialog.Files), StringComparer.OrdinalIgnoreCase);
    }
}
