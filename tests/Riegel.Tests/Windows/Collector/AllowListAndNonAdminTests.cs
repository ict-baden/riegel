// SPDX-License-Identifier: MIT
using System.Text.RegularExpressions;
using Riegel.Collector.Collection;
using Riegel.Tests.Infrastructure;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Erlaubnislisten (Werkzeuge und WMI-Methoden) und die Pfade ohne Adminrechte über Attrappen – darunter der
/// Null-Zeiger-Fall von AuditQuerySystemPolicy, an dem der Prototyp abstürzte.
/// </summary>
public sealed partial class AllowListAndNonAdminTests
{
    [Fact]
    public void ToolRunner_kennt_nur_secedit_export_und_gpresult_x()
    {
        Assert.True(ToolRunner.IsAllowed("secedit.exe", ["/export", "/cfg", "{out}", "/log", "{log}", "/quiet"]));
        Assert.True(ToolRunner.IsAllowed("gpresult.exe", ["/scope", "computer", "/x", "{out}", "/f"]));
        Assert.False(ToolRunner.IsAllowed("secedit.exe", ["/configure", "/db", "{out}"]));
        Assert.False(ToolRunner.IsAllowed("secedit.exe", ["/export", "/cfg", "{out}", "/quiet"]));
        Assert.False(ToolRunner.IsAllowed("auditpol.exe", ["/backup", "/file:{out}"]));
        Assert.False(ToolRunner.IsAllowed("cmd.exe", ["/c", "dir"]));
        Assert.False(ToolRunner.IsAllowed("gpresult.exe", ["/scope", "user", "/x", "{out}", "/f"]));
        Assert.False(ToolRunner.IsAllowed(null!, []));
        Assert.Throws<InvalidOperationException>(() => ToolRunner.Run("cmd.exe", ["/c", "dir"], "a", "b", Path.GetTempPath(), 1));
    }

    [Fact]
    public void Plan_nennt_nur_erlaubte_Werkzeuge()
    {
        foreach (var raw in CollectorTestFiles.ContractPlan().Raw.Where(r => r.Kind == "tool"))
            Assert.True(ToolRunner.IsAllowed(raw.Tool, raw.Arguments), raw.Key);
    }

    [Fact]
    public void Befehlszeile_setzt_Dateien_in_Anfuehrungszeichen()
    {
        Assert.Equal(@"/export /cfg ""C:\T\riegel-1\secedit.inf"" /log ""C:\T\riegel-1\secedit.log"" /quiet",
            ToolRunner.CommandLine(["/export", "/cfg", "{out}", "/log", "{log}", "/quiet"], @"C:\T\riegel-1\secedit.inf", @"C:\T\riegel-1\secedit.log"));
        Assert.Throws<ArgumentException>(() => ToolRunner.Run("secedit.exe", ["/export", "/cfg", "{out}", "/log", "{log}", "/quiet"], "a\"b", "c", Path.GetTempPath(), 1));
    }

    [Fact]
    public void WMI_Methoden_brauchen_Code_und_Plan()
    {
        var plan = CollectorTestFiles.ContractPlan();
        foreach (var method in plan.Wmi.SelectMany(w => w.Methods)) Assert.True(WmiMethods.IsAllowed(method.Name, plan), method.Name);
        Assert.False(WmiMethods.IsAllowed("Create", plan));
        // Eine Planänderung allein öffnet keine Methode
        plan.AllowedWmiMethods.Add("Terminate");
        Assert.False(WmiMethods.IsAllowed("Terminate", plan));
        // … und eine aus dem Plan gestrichene ist zu
        plan.AllowedWmiMethods.Remove("GetKeyProtectors");
        Assert.False(WmiMethods.IsAllowed("GetKeyProtectors", plan));
        Assert.Equal(["GetConfiguration", "GetConversionStatus", "GetEncryptionMethod", "GetKeyProtectors", "GetProtectionStatus", "GetSecurityDescriptor"],
            WmiMethods.AllowList);
    }

    [Fact]
    public void Ueberwachungsrichtlinie_ohne_Privileg_ist_Denied()
    {
        var audit = new FakeAudit(privilege: false, query: false, error: 1300);
        var item = SystemCollector.AuditPolicy("api:audit-policy", audit);
        Assert.Equal(Statuses.Denied, item.Status);
        Assert.Equal(1300, item.ErrorCode);
    }

    [Fact]
    public void Ueberwachungsrichtlinie_Nullzeiger_ohne_Admin_stuerzt_nicht()
    {
        // AuditQuerySystemPolicy liefert ohne Recht keine Daten (Prototyp: NullReferenceException) – hier ein Status
        var audit = new FakeAudit(privilege: true, query: false, error: 1314);
        var item = SystemCollector.AuditPolicy("api:audit-policy", audit);
        Assert.Equal(Statuses.Denied, item.Status);
        Assert.Equal(1314, item.ErrorCode);
        Assert.True(audit.Restored);
    }

    [Fact]
    public void Ueberwachungsrichtlinie_als_Admin_mit_GUIDs_und_Bits()
    {
        var audit = new FakeAudit(privilege: true, query: true, error: 0);
        var item = SystemCollector.AuditPolicy("api:audit-policy", audit);
        Assert.Equal(Statuses.Ok, item.Status);
        var value = (JObject)item.Value;
        Assert.Equal(3L, value.Get("{0CCE9215-69AE-11D9-BED3-505054503030}"));
        Assert.Equal(1L, value.Get("{0CCE9216-69AE-11D9-BED3-505054503030}"));
        Assert.True(audit.Restored);
    }

    [Fact]
    public void Benutzerrechte_ohne_Admin_sind_Denied()
    {
        var lsa = new FakeLsa(openError: 0, enumerateError: 5);
        var item = AccountCollector.Rights("api:lsa-rights", ["SeDebugPrivilege"], lsa);
        Assert.Equal(Statuses.Denied, item.Status);
        Assert.True(lsa.Closed);
        Assert.Equal(Statuses.Denied, AccountCollector.Rights("api:lsa-rights", ["SeDebugPrivilege"], new FakeLsa(5, 0)).Status);
        Assert.Equal(Statuses.Error, AccountCollector.Rights("api:lsa-rights", ["SeDebugPrivilege"], new FakeLsa(87, 0)).Status);
    }

    [Fact]
    public void Benutzerrechte_als_Admin_mit_leerer_Liste_fuer_niemand()
    {
        var item = AccountCollector.Rights("api:lsa-rights", ["SeDebugPrivilege", "SeTcbPrivilege"], new FakeLsa(0, 0));
        var value = (JObject)item.Value;
        Assert.Equal(["S-1-5-32-544"], ((List<object>)value.Get("SeDebugPrivilege")).Cast<string>());
        Assert.Empty((List<object>)value.Get("SeTcbPrivilege"));
    }

    [Fact]
    public void Exitcode_1_ohne_Admin_oder_bei_verweigerten_Eintraegen()
    {
        var data = new SnapshotData { IsAdmin = true };
        data.Items.Add(CollectedItem.Ok("api:x", new JObject()));
        data.Items.Add(CollectedItem.Fail("api:y", Statuses.NotCollected, null, "only with --deep"));
        data.Items.Add(CollectedItem.Fail("wmi:z", Statuses.NamespaceMissing, null, "Invalid namespace"));
        Assert.False(data.Incomplete);
        data.Items.Add(CollectedItem.Fail("api:audit-policy", Statuses.Denied, 1314, "Access denied"));
        Assert.True(data.Incomplete);
        Assert.True(new SnapshotData { IsAdmin = false }.Incomplete);
    }

    [Fact]
    public void Prozessstart_mit_Shell_nur_in_Shell_cs_und_nie_im_Erfassungscode()
    {
        var findings = new List<string>();
        foreach (var (file, code) in SourceScanTests.Sources("Riegel.Collector", "Riegel.Platform.Windows"))
        {
            string name = Path.GetFileName(file);
            if (ShellExecute().IsMatch(code) && name != "Shell.cs") findings.Add(file);
            if (file.Contains($"{Path.DirectorySeparatorChar}Collection{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && (ShellExecute().IsMatch(code) || code.Contains("UseShellExecute = true", StringComparison.Ordinal))) findings.Add(file);
            // Löschen nur im eigenen Arbeitsordner, und nur der Sammler-Code darf das
            if (name.Contains("Workspace", StringComparison.Ordinal) && !file.Contains("Riegel.Collector", StringComparison.Ordinal)) findings.Add(file);
        }
        Assert.True(findings.Count == 0, string.Join("\n", findings));
    }

    [Fact]
    public void Registry_nur_ueber_KEY_READ_und_die_64_Bit_Ansicht()
    {
        var native = SourceScanTests.Sources("Riegel.Collector").Single(s => s.File.EndsWith("Native.cs", StringComparison.Ordinal)).Code;
        Assert.Contains("KEY_READ = 0x20019", native, StringComparison.Ordinal);
        Assert.Contains("KEY_WOW64_64KEY = 0x0100", native, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bRegistry(View|Key)\b|Microsoft\.Win32\.Registry", string.Concat(SourceScanTests.Sources("Riegel.Collector").Select(s => s.Code)));
    }

    [GeneratedRegex(@"\bShellExecute(Ex)?\b")]
    private static partial Regex ShellExecute();
}
