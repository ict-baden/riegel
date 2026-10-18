// SPDX-License-Identifier: MIT
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Riegel.Collector.Collection;
using Riegel.Tests.TestSupport;

namespace Riegel.Tests.Windows.Collector;

/// <summary>
/// Registry-Attrappe für die Erfassungslogik: ein Baum aus Schlüsseln mit Werten; einzelne Pfade können verweigert
/// sein (Fehler 5). Nie die echte Registry – Tests schreiben nichts und lesen das System nur in LocalReadOnly-Tests.
/// </summary>
internal sealed class FakeRegistry : IRegistry
{
    private readonly Dictionary<string, List<RegValue>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Schlüssel (ohne „HKLM\“) samt aller Elternschlüssel anlegen.</summary>
    public FakeRegistry Key(string path, params RegValue[] values)
    {
        var parts = path.Split('\\');
        for (int i = 1; i <= parts.Length; i++)
        {
            string p = string.Join('\\', parts.Take(i));
            if (!_keys.ContainsKey(p)) _keys[p] = [];
        }
        _keys[path].AddRange(values);
        return this;
    }

    public FakeRegistry Deny(string path)
    {
        Key(path);
        _denied.Add(path);
        return this;
    }

    public static RegValue Dword(string name, uint value) => new(name, RegTypes.Dword, BitConverter.GetBytes(value));

    public static RegValue Qword(string name, ulong value) => new(name, RegTypes.Qword, BitConverter.GetBytes(value));

    public static RegValue Sz(string name, string value, int type = RegTypes.Sz) => new(name, type, Encoding.Unicode.GetBytes(value + "\0"));

    public static RegValue Multi(string name, params string[] values) =>
        new(name, RegTypes.MultiSz, Encoding.Unicode.GetBytes(string.Join("\0", values) + "\0\0"));

    public static RegValue Binary(string name, byte[] data) => new(name, RegTypes.Binary, data);

    public int Open(string path, out IRegistryKey key)
    {
        key = null!;
        if (_denied.Contains(path)) return 5;
        if (!_keys.ContainsKey(path)) return 2;
        key = new FakeKey(this, path);
        return 0;
    }

    private sealed class FakeKey(FakeRegistry owner, string path) : IRegistryKey
    {
        public void Dispose() { }

        public int SubKeyNames(List<string> names)
        {
            string prefix = path + "\\";
            foreach (var k in owner._keys.Keys)
                if (k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && k.IndexOf('\\', prefix.Length) < 0)
                    names.Add(k[prefix.Length..]);
            return 0;
        }

        public int Values(List<RegValue> values)
        {
            values.AddRange(owner._keys[path]);
            return 0;
        }
    }
}

/// <summary>Überwachungsrichtlinie ohne Adminrechte bzw. mit dem Null-Zeiger-Fall des Prototyps.</summary>
internal sealed class FakeAudit(bool privilege, bool query, int error) : IAuditPolicy
{
    public bool Restored { get; private set; }

    public bool EnableSecurityPrivilege(out int e)
    {
        e = privilege ? 0 : error;
        return privilege;
    }

    public bool Query(List<Guid> subCategories, List<int> settings, out int e)
    {
        e = query ? 0 : error;
        if (!query) return false;
        subCategories.Add(new Guid("0CCE9215-69AE-11D9-BED3-505054503030"));
        settings.Add(3);
        subCategories.Add(new Guid("0CCE9216-69AE-11D9-BED3-505054503030"));
        settings.Add(5); // Bit 4 (POLICY_AUDIT_EVENT_NONE) wird ausgeblendet
        return true;
    }

    public void RestorePrivilege() => Restored = true;
}

/// <summary>Benutzerrechte: öffnen und aufzählen mit vorgegebenen Fehlern.</summary>
internal sealed class FakeLsa(int openError, int enumerateError) : ILsaRights
{
    public bool Closed { get; private set; }

    public bool Open(out int error)
    {
        error = openError;
        return openError == 0;
    }

    public int Enumerate(string right, List<string> sids)
    {
        if (enumerateError != 0) return enumerateError;
        if (right == "SeDebugPrivilege") sids.Add("S-1-5-32-544");
        return 0;
    }

    public void Close() => Closed = true;
}

/// <summary>Plan und gebauter Sammler für die Tests.</summary>
internal static class CollectorTestFiles
{
    public static Plan ContractPlan() => Plan.Parse(File.ReadAllText(RepoPaths.CollectionPlan));

    /// <summary>Ein Plan mit nur den angegebenen Registry-Einträgen (Rest wie im Vertrag).</summary>
    public static Plan RegistryPlan(int maxValues, params PlanRegistry[] entries)
    {
        var plan = ContractPlan();
        plan.Registry.Clear();
        plan.Registry.AddRange(entries);
        plan.MaxValuesTotal = maxValues;
        return plan;
    }

    public static PlanRegistry Entry(string path, string kind = "recursive", int depth = 8, string[]? values = null) =>
        new() { Path = path, Kind = kind, MaxDepth = depth, Values = values, Mode = "always" };

    /// <summary>Pfad der gebauten net35-Exe (gleiche Konfiguration wie der Testlauf).</summary>
    public static string CollectorExe => typeof(CollectorTestFiles).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .First(a => a.Key == "RiegelCollectorExe").Value!;

    /// <summary>Startet die gebaute Exe und liefert Exitcode, Ausgabe und Fehlerausgabe.</summary>
    public static (int Exit, string Output, string Error) Run(params string[] args)
    {
        var info = new ProcessStartInfo(CollectorExe)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(180_000)) { process.Kill(); throw new TimeoutException("Sammler läuft zu lange"); }
        return (process.ExitCode, output.Result, error.Result);
    }
}
