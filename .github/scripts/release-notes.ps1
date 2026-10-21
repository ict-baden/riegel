# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Schreibt den Text eines Tag-Releases: den englischen Teil des Versionsabschnitts aus CHANGELOG.md, Version und
    SHA-256 des Sammlers und die Liste der Dateien.

.DESCRIPTION
    Statt „--generate-notes“: Das listete deutsche PR-Titel und nannte Beitragende als „New Contributor“. CHANGELOG.md
    bleibt deutsch, Release-Texte sind englisch: Jeder Versionsabschnitt endet deshalb mit „### English“, und das ist
    der Release-Text (wie in Zutritt). Fehlt der Teil oder ist er leer, bricht das Skript ab; die CI ruft es bei einem
    Tag vor dem Bauen auf, damit kein Release ohne Text entsteht. Der Test „Jede_Version_hat_einen_englischen_Release_
    Text“ prüft dasselbe schon im Pull Request.

.PARAMETER Since
    Ältestes Tag mit denselben Sammler-Bytes („Collector unchanged since …“), leer wenn der Sammler neu ist.

.EXAMPLE
    ./.github/scripts/release-notes.ps1 -Version 0.1.3 -Collector 0.1.0 -CollectorSha256 042f… -Since v0.1.0
#>
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Collector = '',
    [string] $CollectorSha256 = '',
    [string] $Since = '',
    [switch] $Signed,
    [string] $Changelog = (Join-Path $PSScriptRoot '../../CHANGELOG.md'),
    [string] $Repository = 'ict-baden/riegel'
)
$ErrorActionPreference = 'Stop'

$lines = @(Get-Content -LiteralPath $Changelog -Encoding utf8)
# Abschnitt „## [0.1.3] – 2026-10-21“ bis zum nächsten „## “
$start = -1
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match "^## \[$([regex]::Escape($Version))\]") { $start = $i; break }
}
if ($start -lt 0) { throw "CHANGELOG.md hat keinen Abschnitt '## [$Version]'." }
$end = $lines.Count
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '^## ') { $end = $i; break }
}
$english = -1
for ($i = $start + 1; $i -lt $end; $i++) {
    if ($lines[$i] -match '^### English\s*$') { $english = $i; break }
}
if ($english -lt 0) { throw "Im CHANGELOG-Abschnitt $Version fehlt '### English' (der englische Release-Text)." }
# Bereich nur, wenn nach der Überschrift noch Zeilen kommen (5..4 zählte in PowerShell rückwärts)
$body = if ($english + 1 -lt $end) {
    (@($lines[($english + 1)..($end - 1)] | Where-Object { $_ -notmatch '^\s*<!--.*-->\s*$' }) -join "`n").Trim()
} else { '' }
if (-not $body) { throw "Der Abschnitt '### English' von $Version ist leer." }

$blob = "https://github.com/$Repository/blob/v$Version"
$signedText = if ($Signed) { 'The programs are code-signed.' }
              else { "The programs are not code-signed yet – no customer pilot on OT or validated machines yet; check every file against SHA256SUMS.txt ([how]($blob/docs/installation.md#verifying-the-download))." }
$collectorText = if ($Collector -and $CollectorSha256) {
    "Collector $Collector, SHA-256 (unsigned) ``$CollectorSha256``.$(if ($Since) { " Collector unchanged since $Since." })"
} else { '' }
$intro = (@($collectorText, $signedText, 'On OT and validated machines use these tagged releases, never `latest`.') | Where-Object { $_ }) -join ' '
$s = "-$Version"
$text = @"
$body

#### Downloads

$intro

- **Riegel$s-x64.msi** – evaluation app for all users (Intune/GPO) – not the way onto OT machines
- **Riegel.exe** – desktop app as a single file, no installation, no .NET runtime needed
- **Riegel$s-win-x64-framework.zip** – small variant with ``Riegel.Cli.exe`` and ``collector\``, needs the .NET 10 Desktop Runtime
- **riegel$s-win-x64.exe** – command line for Windows as a single file (scripts, scheduled checks)
- **riegel$s-linux-x64** / **riegel$s-linux-arm64** – command line for Linux (auditors, CI); arm64 without runtime test
- **Riegel-Collector$s.zip** – portable collector for OT and legacy machines (Windows 7 SP1 to 11), installs nothing
- Riegel-Collector-allowlist$s.zip – AppLocker data for the collector
- Riegel-Collector-ReadOnlyProof$s.txt – read-only proof of this build
- Riegel$s-sbom.cdx.json – bill of materials (CycloneDX)
- SHA256SUMS.txt – SHA-256 checksums of all files (``sha256sum -c``)

All changes: [CHANGELOG.md]($blob/CHANGELOG.md) (German).
"@
$text.Trim()
