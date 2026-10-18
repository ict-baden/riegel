#Requires -Version 7.0
<#
.SYNOPSIS
Vergleicht einen auf diesem PC aufgezeichneten Export mit den Erwartungen des Orakels (nur lesend).

.DESCRIPTION
Hilfe für den Integrationsschritt (spec 8.4): ruft genau einmal
"Riegel.Cli.exe check --snapshot <Export> --format json --no-store" auf und vergleicht Status und Flags je Regel mit
expected-results.json. Das Skript erfasst nichts, schreibt keine Datei und ändert nichts am Rechner; es gibt nur die
Unterschiede aus.

Der Export muss außerhalb des Repos liegen: Aufzeichnungen dieses PCs bleiben lokal (spec 8.1) und werden nur nach
Freigabe einer anonymisierten Fassung committet.

Arten von Unterschieden:
  Widerspruch        feste Erwartung (pass/fail/notApplicable/info/notCheckable) und anderes Ergebnis
  any außerhalb      Erwartung "any" mit erlaubten Status, Ergebnis nicht darunter
  Flag               verlangtes Flag fehlt oder verbotenes Flag ist gesetzt
  fehlt              keine Ergebnis-Zeile für die Regel
  Lücke entschieden  Erwartung notCheckable nur, weil die Recherche den Wert nicht nennt (researchGap); die
                     Aufzeichnung entscheidet die Regel – nur zur Information, kein Fehler

.PARAMETER SnapshotPath
Export dieses PCs (.snapshot.json oder .snapshot.json.gz), außerhalb des Repos.

.PARAMETER CliPath
Pfad zu Riegel.Cli.exe. Standard: die neueste gebaute Riegel.Cli.exe unter src/Riegel.Cli/bin, sonst aus PATH.

.PARAMETER Role
admin, nonadmin oder auto (Standard: aus "isAdmin" des Exports).

.PARAMETER ExpectedPath
Erwartungen. Standard: expected-results.json neben diesem Skript.

.EXAMPLE
pwsh -File tests/Riegel.Tests/Oracle/Compare-LocalRecording.ps1 -SnapshotPath D:\Aufzeichnung\PC_20261018T080000Z.snapshot.json

.NOTES
Exitcode 0: keine Unterschiede außer "Lücke entschieden"; 1: Unterschiede; 3: Aufruf nicht möglich.
Die Auswertung der CLI nimmt das heutige Datum; die Erwartungen gelten für 2026-10-18 (LIFE-01/02 können später
anders ausfallen).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $SnapshotPath,
    [string] $CliPath,
    [ValidateSet('auto', 'admin', 'nonadmin')] [string] $Role = 'auto',
    [string] $ExpectedPath = (Join-Path $PSScriptRoot 'expected-results.json')
)

$ErrorActionPreference = 'Stop'

try {
    $repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..' '..' '..'))
    $snapshotFull = [System.IO.Path]::GetFullPath($SnapshotPath)
    if (-not (Test-Path -LiteralPath $snapshotFull -PathType Leaf)) { throw "Export nicht gefunden: $snapshotFull" }
    $rootPrefix = $repoRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    if ($snapshotFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Der Export muss außerhalb des Repos liegen ($repoRoot): Aufzeichnungen dieses PCs gehören nicht ins Repo."
    }

    # Kopf des Exports lesen (auch .gz), nur für die Rolle
    $stream = [System.IO.File]::OpenRead($snapshotFull)
    try {
        if ($snapshotFull.EndsWith('.gz', [System.StringComparison]::OrdinalIgnoreCase)) {
            $stream = [System.IO.Compression.GZipStream]::new($stream, [System.IO.Compression.CompressionMode]::Decompress)
        }
        $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::UTF8)
        try { $header = $reader.ReadToEnd() | ConvertFrom-Json -Depth 100 } finally { $reader.Dispose() }
    }
    finally { $stream.Dispose() }
    if ($Role -eq 'auto') { $Role = if ($header.isAdmin) { 'admin' } else { 'nonadmin' } }

    # Riegel.Cli.exe finden
    if (-not $CliPath) {
        $built = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'src' 'Riegel.Cli' 'bin') -Recurse -Filter 'Riegel.Cli.exe' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending)
        if ($built.Count -gt 0) { $CliPath = $built[0].FullName }
        elseif ($onPath = Get-Command 'Riegel.Cli.exe' -ErrorAction SilentlyContinue) { $CliPath = $onPath.Source }
        else { throw 'Riegel.Cli.exe nicht gefunden: erst "dotnet build Riegel.sln -c Release" ausführen oder -CliPath angeben.' }
    }
    $cli = (Resolve-Path -LiteralPath $CliPath).Path

    $expected = Get-Content -LiteralPath $ExpectedPath -Raw -Encoding utf8 | ConvertFrom-Json -Depth 100
    $table = $expected.snapshots.$Role.results
    if ($null -eq $table) { throw "Keine Erwartungen für die Rolle '$Role' in $ExpectedPath." }
}
catch {
    Write-Error $_.Exception.Message -ErrorAction Continue
    exit 3
}

Write-Host "Orakel: $Role · Export: $snapshotFull"
Write-Host "CLI:    $cli"

# Der einzige Aufruf: auswerten und als JSON ausgeben, nichts ablegen
$output = & $cli check --snapshot $snapshotFull --format json --no-store
$cliExit = $LASTEXITCODE
if ($cliExit -eq 3 -or -not $output) {
    Write-Error "Riegel.Cli.exe check endete mit $cliExit (UNKNOWN); Meldung siehe oben." -ErrorAction Continue
    exit 3
}
$run = ($output -join "`n") | ConvertFrom-Json -Depth 100
$collected = if ($run.collectedAtUtc -is [datetime]) { $run.collectedAtUtc.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') } else { $run.collectedAtUtc }
Write-Host ("Lauf:   {0}, erfasst {1}, Admin {2}, Katalog {3}, CLI-Exitcode {4}" -f $run.hostName, $collected, $run.isAdmin,
    $run.catalogue.version, $cliExit)
if ([bool]$run.isAdmin -ne ($Role -eq 'admin')) {
    Write-Warning "Der Export ist isAdmin=$($run.isAdmin), verglichen wird mit den Erwartungen für '$Role'."
}

$results = @{}
foreach ($r in @($run.results)) {
    if ($r -and $r.ruleSetId -eq 'riegel') { $results[$r.ruleId] = $r }
}

$rows = [System.Collections.Generic.List[object]]::new()
foreach ($property in $table.PSObject.Properties) {
    $id = $property.Name
    $x = $property.Value
    $allowed = @($x.allowed | Where-Object { $_ })
    $mustHave = @($x.flags | Where-Object { $_ })
    if (-not $results.ContainsKey($id)) {
        $rows.Add([pscustomobject]@{ Regel = $id; Erwartet = $x.status; Ergebnis = '-'; Art = 'fehlt'; Flags = ''; Grund = ''; Begruendung = $x.why })
        continue
    }
    $result = $results[$id]
    $actual = [string]$result.status
    $flags = @($result.flags | Where-Object { $_ })
    $kinds = [System.Collections.Generic.List[string]]::new()
    if ($x.status -eq 'any') {
        if ($allowed.Count -gt 0 -and $allowed -notcontains $actual) { $kinds.Add('any außerhalb') }
    }
    elseif ($actual -ne $x.status) {
        $kinds.Add($(if ($x.researchGap) { 'Lücke entschieden' } else { 'Widerspruch' }))
    }
    $statusFits = $x.status -eq $actual -or ($x.status -eq 'any' -and ($allowed.Count -eq 0 -or $allowed -contains $actual))
    $flagNotes = [System.Collections.Generic.List[string]]::new()
    if ($statusFits) {
        foreach ($f in $mustHave) { if ($flags -notcontains $f) { $flagNotes.Add("-$f") } }
        foreach ($f in @($x.absentFlags | Where-Object { $_ })) { if ($flags -contains $f) { $flagNotes.Add("+$f") } }
    }
    foreach ($f in @($expected.globalAbsentFlags | Where-Object { $_ })) { if ($flags -contains $f) { $flagNotes.Add("+$f") } }
    if ($flagNotes.Count -gt 0) { $kinds.Add('Flag') }
    if ($kinds.Count -gt 0) {
        $rows.Add([pscustomobject]@{
                Regel       = $id
                Erwartet    = $(if ($allowed.Count -gt 0) { 'any:' + ($allowed -join '|') } else { $x.status })
                Ergebnis    = $actual
                Art         = $kinds -join ', '
                Flags       = $flagNotes -join ' '
                Grund       = [string]$result.reasonCode
                Begruendung = $x.why
            })
    }
}

$blocking = @($rows | Where-Object { $_.Art -ne 'Lücke entschieden' })
if ($rows.Count -eq 0) {
    Write-Host 'Keine Unterschiede.'
}
else {
    $rows | Sort-Object Art, Regel | Format-Table -AutoSize -Wrap Regel, Erwartet, Ergebnis, Art, Flags, Grund, Begruendung | Out-Host
}
$gaps = @($rows | Where-Object { $_.Art -eq 'Lücke entschieden' }).Count
Write-Host ("{0} Unterschied(e), davon {1} 'Lücke entschieden' (nur Information). Erwartungen ändern nur Autor oder Prüfung der Integration, mit Begründung im Commit." -f $rows.Count, $gaps)
exit $(if ($blocking.Count -gt 0) { 1 } else { 0 })
