// SPDX-License-Identifier: MIT
using System.Globalization;
using System.Text.Json;
using Riegel.Core.Model;
using Riegel.Core.Parsing;

namespace Riegel.Core.Baselines;

/// <summary>
/// Die CSV-Vorlage für Baselines (spec 2.15, <c>docs/assets/baseline-template.csv</c>): Spalten
/// <c>id,title,severity,type,key,value,op,expected,valueType</c>, eine Vorgabe je Zeile, Typen <c>registry</c>,
/// <c>service</c>, <c>auditSubcategory</c> und <c>userRight</c>. UTF-8 (mit oder ohne BOM), Trennzeichen „,“ oder „;“,
/// Listen in <c>expected</c> durch „;“ getrennt (in Anführungszeichen, wenn „;“ das Trennzeichen ist). Jede ungültige
/// Zeile ist ein Fehler mit Zeilennummer (RGL-0284) – eine halb gelesene Baseline gibt es nicht.
/// </summary>
internal static class BaselineCsvReader
{
    internal static readonly string[] Columns = ["id", "title", "severity", "type", "key", "value", "op", "expected", "valueType"];

    private static readonly string[] StartModes = ["Boot", "System", "Auto", "Manual", "Disabled"];

    internal static List<RuleDraft> Read(byte[] bytes, string source, ReadLimits l)
    {
        var span = bytes.AsSpan();
        if (TextFiles.DetectBom(span) == TextFiles.Bom.Utf8) span = span[3..];
        else if (TextFiles.DetectBom(span) != TextFiles.Bom.None) throw RglCodesParsing.Error(RglCodesParsing.InvalidEncoding, source, "UTF-8");
        var text = TextFiles.DecodeUtf8(span, source);
        List<CsvText.Row> rows;
        try
        {
            rows = CsvText.Parse(text, CsvText.DetectDelimiter(text), l.MaxBaselineCsvRows + 1, l.MaxStringChars);
        }
        catch (InvalidDataException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.BaselineTooLarge, source, l.MaxBaselineCsvRows);
        }
        catch (FormatException)
        {
            throw RglCodesParsing.Error(RglCodesParsing.BaselineCsvInvalid, source, 0, "\"");
        }
        if (rows.Count == 0) throw RglCodesParsing.Error(RglCodesParsing.BaselineCsvInvalid, source, 1, "header");
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows[0].Fields.Count; i++)
        {
            var name = rows[0].Fields[i].Trim();
            if (!Columns.Contains(name, StringComparer.OrdinalIgnoreCase) || !index.TryAdd(name, i))
                throw RglCodesParsing.Error(RglCodesParsing.BaselineCsvInvalid, source, rows[0].Line, name);
        }
        foreach (var column in Columns)
            if (!index.ContainsKey(column)) throw RglCodesParsing.Error(RglCodesParsing.BaselineCsvInvalid, source, rows[0].Line, column);
        var drafts = new List<RuleDraft>();
        foreach (var row in rows.Skip(1))
        {
            if (row.Fields.Count > Columns.Length) throw Bad(source, row, "columns");
            string F(string column) => index[column] < row.Fields.Count ? row.Fields[index[column]].Trim() : "";
            foreach (var column in Columns)
                if (TextFiles.HasControlCharacters(F(column))) throw Bad(source, row, column);
            var title = F("title");
            if (title.Length == 0) throw Bad(source, row, "title");
            var id = F("id");
            if (id.Length > 0 && !BaselineImporter.RuleIdPattern().IsMatch(id)) throw Bad(source, row, "id");
            var severity = F("severity").ToLowerInvariant() switch
            {
                "" or "medium" => Severity.Medium,
                "low" => Severity.Low,
                "high" => Severity.High,
                _ => throw Bad(source, row, "severity"),
            };
            var (check, category) = F("type") switch
            {
                "registry" => (Registry(row, F, source), "system"),
                "service" => (Service(row, F, source), "services"),
                "auditSubcategory" => (Audit(row, F, source), RuleCategories.Audit),
                "userRight" => (UserRight(row, F, source), "userRights"),
                _ => throw Bad(source, row, "type"),
            };
            drafts.Add(new RuleDraft(id.Length > 0 ? id : null, title, severity, category, check, null, null,
                source + ":" + row.Line.ToString(CultureInfo.InvariantCulture)));
        }
        return drafts;
    }

    private static CheckSpec Registry(CsvText.Row row, Func<string, string> f, string source)
    {
        var key = f("key");
        if (key.StartsWith(@"HKEY_LOCAL_MACHINE\", StringComparison.OrdinalIgnoreCase)) key = @"HKLM\" + key[19..];
        if (!key.StartsWith(@"HKLM\", StringComparison.OrdinalIgnoreCase) || key.Length < 6) throw Bad(source, row, "key");
        key = key.TrimEnd('\\');
        var type = f("valueType") switch
        {
            "dword" => RegValueType.Dword,
            "qword" => RegValueType.Qword,
            "sz" => RegValueType.Sz,
            "expand_sz" => RegValueType.ExpandSz,
            "multi_sz" => RegValueType.MultiSz,
            _ => throw Bad(source, row, "valueType"),
        };
        var opText = f("op");
        var op = opText.Length == 0 ? CheckOperator.Eq : ParseOperator(opText) ?? throw Bad(source, row, "op");
        var expectedText = f("expected");
        JsonElement? expected;
        if (op is CheckOperator.Exists or CheckOperator.NotExists)
        {
            if (expectedText.Length > 0) throw Bad(source, row, "expected");
            expected = null;
        }
        else
        {
            bool list = op is CheckOperator.In or CheckOperator.NotIn or CheckOperator.ContainsAll or CheckOperator.ContainsNone || type == RegValueType.MultiSz;
            if (op is CheckOperator.ContainsAll or CheckOperator.ContainsNone && type != RegValueType.MultiSz) throw Bad(source, row, "op");
            if (op is CheckOperator.Lt or CheckOperator.Le or CheckOperator.Gt or CheckOperator.Ge or CheckOperator.BitsSet or CheckOperator.BitsClear
                && type is not (RegValueType.Dword or RegValueType.Qword)) throw Bad(source, row, "op");
            if (op is CheckOperator.AgeDaysLe or CheckOperator.AgeDaysGt) throw Bad(source, row, "op");
            var parts = list ? Split(expectedText) : [expectedText];
            if (parts.Count == 0 && type != RegValueType.MultiSz) throw Bad(source, row, "expected");
            var values = new List<JsonElement>();
            foreach (var part in parts)
            {
                switch (type)
                {
                    case RegValueType.Dword:
                        if (!CanonicalValue.TryParseInteger(part, out var d) || d > uint.MaxValue) throw Bad(source, row, "expected");
                        values.Add(JsonValues.Number(d));
                        break;
                    case RegValueType.Qword:
                        if (!CanonicalValue.TryParseInteger(part, out var q)) throw Bad(source, row, "expected");
                        values.Add(JsonValues.Text(q.ToString(CultureInfo.InvariantCulture)));
                        break;
                    default:
                        values.Add(JsonValues.Text(part));
                        break;
                }
            }
            expected = list ? JsonValues.Build(w =>
            {
                w.WriteStartArray();
                foreach (var v in values) v.WriteTo(w);
                w.WriteEndArray();
            }) : values[0];
        }
        return new RegistryCheck(key, f("value"), type, null, op, expected, WhenMissing.Fail, null, null, null);
    }

    /// <summary>
    /// key = Dienstname, value = <c>startMode</c> (Standard) oder <c>state</c>, op = <c>in</c> oder <c>eq</c>, expected =
    /// erlaubte Werte („;“). Ein nicht installierter Dienst erfüllt eine Vorgabe, die nur Disabled bzw. Stopped erlaubt;
    /// sonst ist er ein Befund.
    /// </summary>
    private static CheckSpec Service(CsvText.Row row, Func<string, string> f, string source)
    {
        var name = f("key");
        if (name.Length == 0 || name.Contains('\\', StringComparison.Ordinal)) throw Bad(source, row, "key");
        if (f("valueType").Length > 0) throw Bad(source, row, "valueType");
        if (f("op").Length > 0 && !f("op").Equals("in", StringComparison.Ordinal) && !f("op").Equals("eq", StringComparison.Ordinal)) throw Bad(source, row, "op");
        var allowed = Split(f("expected"));
        if (allowed.Count == 0 || (f("op") == "eq" && allowed.Count != 1)) throw Bad(source, row, "expected");
        switch (f("value"))
        {
            case "" or "startMode":
                var modes = new List<string>();
                foreach (var a in allowed)
                    modes.Add(StartModes.FirstOrDefault(m => m.Equals(a, StringComparison.OrdinalIgnoreCase)) ?? throw Bad(source, row, "expected"));
                return new ServiceCheck(name, modes, null, modes.All(m => m == "Disabled") ? AbsentOutcome.Pass : AbsentOutcome.Fail);
            case "state":
                return new ServiceCheck(name, null, allowed, allowed.All(s => s.Equals("Stopped", StringComparison.OrdinalIgnoreCase)) ? AbsentOutcome.Pass : AbsentOutcome.Fail);
            default:
                throw Bad(source, row, "value");
        }
    }

    /// <summary>key = GUID der Unterkategorie, expected = success, failure, successAndFailure, includesSuccess, includesFailure, noAuditing.</summary>
    private static CheckSpec Audit(CsvText.Row row, Func<string, string> f, string source)
    {
        var raw = f("key");
        var guid = AuditCsvParser.NormalizeGuid(raw.StartsWith('{') ? raw : "{" + raw + "}") ?? throw Bad(source, row, "key");
        if (f("valueType").Length > 0) throw Bad(source, row, "valueType");
        if (f("op").Length > 0 && f("op") != "eq") throw Bad(source, row, "op");
        var expected = f("expected") switch
        {
            "success" => AuditExpectation.Success,
            "failure" => AuditExpectation.Failure,
            "successAndFailure" => AuditExpectation.SuccessAndFailure,
            "includesSuccess" => AuditExpectation.IncludesSuccess,
            "includesFailure" => AuditExpectation.IncludesFailure,
            "noAuditing" => AuditExpectation.NoAuditing,
            _ => throw Bad(source, row, "expected"),
        };
        return new AuditSubcategoryCheck(guid, expected);
    }

    /// <summary>key = Recht (SeDebugPrivilege), op = Modus (mustInclude, mustNotInclude, onlyFrom, empty), expected = SIDs („;“).</summary>
    private static CheckSpec UserRight(CsvText.Row row, Func<string, string> f, string source)
    {
        var right = f("key");
        if (!BaselineImporter.UserRightPattern().IsMatch(right)) throw Bad(source, row, "key");
        if (f("valueType").Length > 0) throw Bad(source, row, "valueType");
        var mode = f("op") switch
        {
            "mustInclude" => UserRightMode.MustInclude,
            "mustNotInclude" => UserRightMode.MustNotInclude,
            "onlyFrom" => UserRightMode.OnlyFrom,
            "empty" => UserRightMode.Empty,
            _ => throw Bad(source, row, "op"),
        };
        var sids = Split(f("expected")).Select(s => s.StartsWith("*S-", StringComparison.Ordinal) ? s[1..] : s).ToList();
        if (sids.Any(s => !BaselineImporter.SidPattern().IsMatch(s))) throw Bad(source, row, "expected");
        if ((mode == UserRightMode.Empty) != (sids.Count == 0)) throw Bad(source, row, "expected");
        return new UserRightCheck(right, mode, sids);
    }

    private static CheckOperator? ParseOperator(string text) => text switch
    {
        "eq" => CheckOperator.Eq,
        "ne" => CheckOperator.Ne,
        "lt" => CheckOperator.Lt,
        "le" => CheckOperator.Le,
        "gt" => CheckOperator.Gt,
        "ge" => CheckOperator.Ge,
        "in" => CheckOperator.In,
        "notIn" => CheckOperator.NotIn,
        "bitsSet" => CheckOperator.BitsSet,
        "bitsClear" => CheckOperator.BitsClear,
        "containsAll" => CheckOperator.ContainsAll,
        "containsNone" => CheckOperator.ContainsNone,
        "exists" => CheckOperator.Exists,
        "notExists" => CheckOperator.NotExists,
        _ => null,
    };

    private static List<string> Split(string text) =>
        [.. text.Split(';').Select(s => s.Trim()).Where(s => s.Length > 0)];

    private static RiegelException Bad(string source, CsvText.Row row, string column) =>
        RglCodesParsing.Error(RglCodesParsing.BaselineCsvInvalid, source, row.Line, column);
}
