# Notices of the shipped .NET runtimes

Riegel's self-contained packages (`Riegel-<version>-x64.msi`, `Riegel.exe`, `riegel-<version>-win-x64.exe`,
`riegel-<version>-linux-x64`, `riegel-<version>-linux-arm64`) contain the .NET 10 runtime and, for the app, the Windows
Desktop runtime (WPF). Their own third-party notices ship with Riegel, unchanged:

| File | Source |
|---|---|
| `THIRD-PARTY-NOTICES-dotnet-runtime.txt` | https://github.com/dotnet/runtime/blob/v10.0.13/THIRD-PARTY-NOTICES.TXT (identical to `THIRD-PARTY-NOTICES.TXT` in the runtime packs `Microsoft.NETCore.App.Runtime.*` 10.0.13) |
| `THIRD-PARTY-NOTICES-dotnet-wpf.txt` | https://github.com/dotnet/wpf/blob/v10.0.13/THIRD-PARTY-NOTICES.TXT (the Windows Desktop runtime pack contains no notices file) |

Both files are copied next to `Riegel.exe` and `Riegel.Cli.exe` (MSI, ZIP) and embedded in `Riegel.Core`, so
`--licenses` and the info page show them in the single-file programs too. The CI compares the runtime file with the
notices of the runtime packs it actually publishes; when a new .NET patch changes them, replace the file from the
matching tag and update section 12 of `THIRD-PARTY-NOTICES.txt`.
