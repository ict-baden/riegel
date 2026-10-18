[Riegel](../../README.md) › RuleTool

# RuleTool

**Contents:** [Commands](#commands) · [Exit codes](#exit-codes)

Developer tool for the rule catalogue. It is not shipped and its output is English only. Build it with the solution
(`dotnet build Riegel.sln`) and run it from the repository root:

```powershell
dotnet tools/RuleTool/bin/Release/net10.0/RuleTool.dll <command>
```

## Commands

| Command | What it does |
|---|---|
| `rules-doc [--repo <dir>] [--out <file>] [--check]` | Generates [docs/rules.md](../../docs/rules.md) from all rule files under `rules/`: one table per report category with ID, English and German title, severity, kind, references and the sources that back the expected value. The output is deterministic; `--check` compares instead of writing (a test runs it). |
| `verify-sources <folder>... [--repo <dir>]` | Reads local copies of the upstream sources (text, HTML, XML, JSON, CSV, XLSX and ZIP containers, one level deep) and reports, per rule, every ACSC section, MSB setting name, STIG ID, Grundschutz++ and ATT&CK identifier that does not appear in them. Use it after a new upstream release. |
| `coverage [--repo <dir>] [--plan <file>]` | Lists the evidence keys of every rule (spec 2.3, column "Evidence") that `contracts/collection-plan.json` does not collect. Fallback sources of a check type are marked. |

The tool only reads the repository and the given folders; `rules-doc` writes only `docs/rules.md` or the `--out` file.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Everything in order |
| 1 | Findings: `docs/rules.md` out of date (RGL-0303), references not found (RGL-0305), evidence keys outside the plan (RGL-0306) |
| 3 | Invalid command line (RGL-0300), repository or file not found or unreadable (RGL-0301, RGL-0302, RGL-0304, RGL-0307) |
