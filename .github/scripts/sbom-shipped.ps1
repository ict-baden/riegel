<#
.SYNOPSIS
    Gleicht die CycloneDX-Stückliste an das an, was die Pakete wirklich enthalten (richtlinien/06; aus Zutritt, für
    Riegel um die Linux-Programme und den Sammler erweitert).

.DESCRIPTION
    CycloneDX liest die Paketgraphen aus NuGet und nennt dabei auch Pakete, die NuGet weglässt („pruning“), weil die
    .NET- oder Windows-Desktop-Runtime sie schon enthält. Ausgeliefert werden diese Versionen nie; ein Schwachstellen-
    Scan der Stückliste prüfte die falschen. Maßgeblich sind die deps.json der Publish-Läufe (dieselbe Quelle wie der
    Test ThirdPartyNoticesTests):
    1. Komponenten, die in keiner deps.json als Paket stehen, fliegen raus (samt ihrer Kanten in „dependencies“).
    2. Fehlt ein ausgeliefertes Paket in der Stückliste, bricht das Skript ab – lieber kein Release als eine
       unvollständige Stückliste.
    3. Die Runtime-Pakete (.NET für Windows und Linux x64/arm64, Windows Desktop, Windows-SDK-Projektion) kommen mit
       ihrer echten Version dazu.
    4. Der portable Sammler (Riegel.Collector.exe, net35) steht als eigene Komponente darin – mit dem Vermerk, dass er
       keine Pakete Dritter enthält.

.PARAMETER Sbom
    Die von dotnet-CycloneDX erzeugte JSON-Datei; wird überschrieben.

.PARAMETER DepsJson
    deps.json der self-contained Publish-Läufe: die Oberfläche (win-x64) und die Linux-Programme (linux-x64, linux-arm64).

.PARAMETER CollectorVersion
    Version des Sammlers (src/Riegel.Collector/collector-version.txt).

.EXAMPLE
    ./.github/scripts/sbom-shipped.ps1 -Sbom dist/Riegel-sbom.cdx.json -CollectorVersion 0.1.0 -DepsJson `
        src/Riegel/obj/Release/net10.0-windows10.0.19041.0/win-x64/Riegel.deps.json, `
        src/Riegel.Cli/obj/Release/net10.0/linux-x64/Riegel.Cli.deps.json
#>
param(
    [Parameter(Mandatory)] [string] $Sbom,
    [Parameter(Mandatory)] [string[]] $DepsJson,
    [Parameter(Mandatory)] [string] $CollectorVersion
)
$ErrorActionPreference = 'Stop'

$libraries = foreach ($file in $DepsJson) {
    $deps = Get-Content $file -Raw | ConvertFrom-Json
    $deps.libraries.PSObject.Properties | ForEach-Object {
        $id, $version = $_.Name -split '/', 2
        # Runtime-Pakete heißen in der deps.json „runtimepack.<Paket-Id>“
        [pscustomobject]@{ Id = $id -replace '^runtimepack\.', ''; Version = $version; Type = $_.Value.type; File = $file }
    }
}
$packages = @($libraries | Where-Object Type -eq 'package')
$runtimePacks = @($libraries | Where-Object Type -eq 'runtimepack' | Sort-Object Id, Version -Unique)
if ($packages.Count -eq 0) { throw "Die deps.json-Dateien nennen keine Pakete." }
foreach ($file in $DepsJson) {
    if (-not ($libraries | Where-Object { $_.File -eq $file -and $_.Type -eq 'runtimepack' -and $_.Id -like 'Microsoft.NETCore.App.Runtime.*' })) {
        throw "$file stammt nicht aus einem self-contained Publish (keine .NET-Runtime darin)."
    }
}

function PackageUrl([string] $id, [string] $version) { "pkg:nuget/$id@$version" }
$shipped = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($p in $packages) { [void]$shipped.Add((PackageUrl $p.Id $p.Version)) }

$bom = Get-Content $Sbom -Raw | ConvertFrom-Json -Depth 100
$all = @($bom.components)
$kept = @($all | Where-Object { $shipped.Contains($_.purl) })
$dropped = @($all | Where-Object { -not $shipped.Contains($_.purl) })
$missing = @($shipped | Where-Object { $url = $_; -not ($kept | Where-Object purl -eq $url) })
if ($missing.Count -gt 0) { throw "In der Stückliste fehlen ausgelieferte Pakete: $($missing -join ', ')" }

# Runtime-Pakete: welche Dateien in welchem Paket stecken, steht in THIRD-PARTY-NOTICES.txt
function RuntimeInfo([string] $id) {
    switch -Wildcard ($id) {
        'Microsoft.NETCore.App.Runtime.linux-*' {
            $rid = $id.Substring('Microsoft.NETCore.App.Runtime.'.Length)
            return @{ Text = ".NET runtime for $rid, bundled into riegel-$rid"; Licence = @{ id = 'MIT' } }
        }
        'Microsoft.NETCore.App.Runtime.*' {
            return @{ Text = '.NET runtime, only in the self-contained packages (MSI, Riegel.exe, riegel-win-x64.exe)'; Licence = @{ id = 'MIT' } }
        }
        'Microsoft.WindowsDesktop.App.Runtime.*' {
            return @{ Text = 'Windows Desktop runtime (WPF), only in the self-contained packages (MSI and Riegel.exe)'; Licence = @{ id = 'MIT' } }
        }
        'Microsoft.Windows.SDK.NET.Ref*' {
            return @{ Text = 'C#/WinRT and the Windows SDK projection (WinRT.Runtime.dll, Microsoft.Windows.SDK.NET.dll), in every Windows package'
                      Licence = @{ name = 'MIT (WinRT.Runtime.dll); Microsoft Software License Terms for the Windows SDK (Microsoft.Windows.SDK.NET.dll)'; url = 'https://aka.ms/WinSDKLicenseURL' } }
        }
    }
    throw "Unbekanntes Runtime-Paket $id – in sbom-shipped.ps1 und THIRD-PARTY-NOTICES.txt eintragen."
}
$added = @(foreach ($r in $runtimePacks) {
    $info = RuntimeInfo $r.Id
    $url = PackageUrl $r.Id $r.Version
    [pscustomobject][ordered]@{
        type        = 'framework'
        'bom-ref'   = $url
        publisher   = 'Microsoft'
        name        = $r.Id
        version     = $r.Version
        description = $info.Text
        scope       = 'required'
        licenses    = @(@{ license = $info.Licence })
        purl        = $url
    }
})
# Der Sammler ist eigener Code ohne Pakete (nur mscorlib, System, System.Management aus .NET Framework auf dem Ziel)
$collector = [pscustomobject][ordered]@{
    type        = 'application'
    'bom-ref'   = "riegel-collector@$CollectorVersion"
    publisher   = 'ICT-Baden'
    name        = 'Riegel.Collector'
    version     = $CollectorVersion
    description = 'Portable collector (net35, Riegel-Collector.zip and collector\ in the MSI). Contains no third-party packages; it uses only the .NET Framework already installed on the target.'
    scope       = 'required'
    licenses    = @(@{ license = @{ id = 'MIT' } })
}
$added += $collector

$droppedRefs = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($c in $dropped) { [void]$droppedRefs.Add($c.'bom-ref') }
$root = $bom.metadata.component.'bom-ref'
$dependencies = foreach ($d in @($bom.dependencies)) {
    if ($droppedRefs.Contains($d.ref)) { continue }
    $on = @($d.dependsOn | Where-Object { -not $droppedRefs.Contains($_) })
    if ($d.ref -eq $root) { $on += @($added | ForEach-Object { $_.'bom-ref' }) }
    [pscustomobject][ordered]@{ ref = $d.ref; dependsOn = $on }
}
$dependencies = @($dependencies) + @($added | ForEach-Object { [pscustomobject][ordered]@{ ref = $_.'bom-ref'; dependsOn = @() } })

$bom.components = @($kept) + @($added)
$bom.dependencies = $dependencies
[IO.File]::WriteAllText((Resolve-Path $Sbom), ($bom | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

Write-Host "Stückliste: $($kept.Count) Pakete, $($runtimePacks.Count) Runtime-Pakete, Sammler $CollectorVersion"
if ($dropped.Count -gt 0) {
    Write-Host "Entfernt (nicht ausgeliefert, kommen aus der Runtime): $(($dropped | ForEach-Object { "$($_.name) $($_.version)" }) -join ', ')"
}
