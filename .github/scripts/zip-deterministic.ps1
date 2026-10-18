<#
.SYNOPSIS
    Packt einen Ordner reproduzierbar als ZIP (spec 4.3): Einträge nach Pfad sortiert (ordinal, mit „/“), feste
    Zeitstempel, keine Ordnereinträge. Gleiche Dateien ergeben mit derselben .NET-Version dieselbe ZIP.

.PARAMETER Source
    Ordner, dessen Inhalt gepackt wird (ohne den Ordner selbst).

.PARAMETER Destination
    ZIP-Datei; wird ersetzt.

.PARAMETER SumsFile
    Optional: Name einer Prüfsummendatei (sha256sum-Format, LF), die vorher in den Ordner geschrieben wird und alle
    übrigen Dateien nennt (z. B. SHA256SUMS.txt im Sammler-Paket).

.EXAMPLE
    ./.github/scripts/zip-deterministic.ps1 -Source out/collector-bundle -Destination dist/Riegel-Collector.zip -SumsFile SHA256SUMS.txt
#>
param(
    [Parameter(Mandatory)] [string] $Source,
    [Parameter(Mandatory)] [string] $Destination,
    [string] $SumsFile = ''
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression

$root = (Resolve-Path $Source).Path.TrimEnd('\', '/')
function Relative([string] $full) { $full.Substring($root.Length + 1).Replace('\', '/') }

if ($SumsFile) {
    $sumsPath = Join-Path $root $SumsFile
    if (Test-Path $sumsPath) { Remove-Item $sumsPath }
    [string[]] $names = Get-ChildItem $root -File -Recurse | ForEach-Object { Relative $_.FullName }
    [Array]::Sort($names, [StringComparer]::Ordinal)
    $lines = foreach ($name in $names) {
        "$((Get-FileHash (Join-Path $root $name) -Algorithm SHA256).Hash.ToLower())  $name"
    }
    [IO.File]::WriteAllText($sumsPath, ($lines -join "`n") + "`n", [Text.Encoding]::ASCII)
}

[string[]] $entries = Get-ChildItem $root -File -Recurse | ForEach-Object { Relative $_.FullName }
[Array]::Sort($entries, [StringComparer]::Ordinal)
$fixed = [DateTimeOffset]::new(2026, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
# Relativ zum PowerShell-Standort auflösen, nicht zum Prozess-Verzeichnis (das .NET sonst nimmt)
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destination)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
if (Test-Path $target) { Remove-Item $target }
$stream = [IO.File]::Open($target, [IO.FileMode]::CreateNew)
try {
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $entries) {
            $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = $fixed
            $out = $entry.Open()
            try {
                $bytes = [IO.File]::ReadAllBytes((Join-Path $root $name))
                $out.Write($bytes, 0, $bytes.Length)
            }
            finally { $out.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}
finally { $stream.Dispose() }
Write-Host "$Destination`: $($entries.Count) Dateien, SHA-256 $((Get-FileHash $target -Algorithm SHA256).Hash.ToLower())"
