<#
.SYNOPSIS
    Erzeugt die Bilder der Doku neu: Screenshots der echten App (Demo-Anlage, hell und dunkel) und das Banner.

.DESCRIPTION
    1. Baut Riegel (Release) und rendert mit "Riegel.exe --screenshots" jede Seite und die wichtigsten Dialoge hell
       und dunkel nach docs/assets/screenshots (<seite>-light.png, <seite>-dark.png). Nur die erfundene Demo-Anlage
       "Musterwerk - Linie 3", nichts von diesem Rechner; "Diesen PC pruefen" ist dabei gesperrt.
       Braucht eine angemeldete, verbundene Windows-Sitzung (WPF zeichnet sonst leere Bilder, RGL-0602).
    2. Rendert tools/banner/banner.html mit dem installierten Chrome ueber playwright-core nach docs/assets/banner.png
       (einmal "npm install" in tools/banner).

    Die Bilder entstehen auf Englisch (RIEGEL_LANGUAGE=en) mit einem eigenen Datenordner im Temp-Ordner
    (RIEGEL_DATA_DIR), damit eigene Einstellungen nicht hineinwirken.

    -Review rendert zusaetzlich alle fuenf Sprachen (hell) in einen Temp-Ordner und zeigt die Meldungen der
    Layout-Pruefung ("[layout]": gekuerzte Texte, Richtlinie 13 - 30 % Reserve, Chinesisch einmal pruefen).

    Die Demo-Anlage wird mit der echten Engine und dem eingebetteten Katalog ausgewertet.

.EXAMPLE
    cd tools\banner; npm install; cd ..\screenshots; .\make-assets.ps1
.EXAMPLE
    .\make-assets.ps1 -Review -SkipBanner
#>
param(
    [switch]$Review,
    [switch]$SkipBanner
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$assets = Join-Path $repo 'docs\assets'
$shots = Join-Path $assets 'screenshots'
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("riegel-assets-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $shots, $tmp | Out-Null

$saved = @{}
$data = $null
foreach ($name in 'RIEGEL_LANGUAGE', 'RIEGEL_DATA_DIR') { $saved[$name] = [Environment]::GetEnvironmentVariable($name) }

function Invoke-Screenshots([string]$exe, [string]$language, [string]$target, [string]$theme) {
    $env:RIEGEL_LANGUAGE = $language
    # Riegel.exe ist ein Fensterprogramm: Out-Host wartet auf das Ende und zeigt die Ausgabe
    & $exe --screenshots $target --theme $theme | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Screenshots ($language) fehlgeschlagen ($LASTEXITCODE)" }
}

try {
    dotnet build (Join-Path $repo 'src\Riegel\Riegel.csproj') -c Release -nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Build fehlgeschlagen ($LASTEXITCODE)" }
    $exe = Join-Path $repo 'src\Riegel\bin\Release\net10.0-windows\Riegel.exe'

    # fester Name, weil die Info-Seite den Datenordner zeigt (%TEMP%\Riegel-Screenshots statt eines Zufallsnamens)
    $data = Join-Path ([IO.Path]::GetTempPath()) 'Riegel-Screenshots'
    $env:RIEGEL_DATA_DIR = $data

    # 1. Screenshots auf Englisch, hell und dunkel
    $en = Join-Path $tmp 'en'
    Invoke-Screenshots $exe 'en' $en 'both'
    Get-ChildItem $shots -Filter *.png | Remove-Item -Force
    foreach ($file in Get-ChildItem $en -Filter *.png) {
        if ($file.BaseName -notmatch '^[a-z-]+-(light|dark)$') { continue }
        Copy-Item $file.FullName (Join-Path $shots $file.Name) -Force
    }
    Write-Host "Screenshots: $shots"

    # Pruefung aller Sprachen (nur hell, bleibt im Temp-Ordner)
    if ($Review) {
        $reviewFolder = Join-Path ([IO.Path]::GetTempPath()) 'riegel-review'
        Remove-Item $reviewFolder -Recurse -Force -ErrorAction SilentlyContinue
        foreach ($language in 'de', 'es', 'fr', 'zh-Hans') { Invoke-Screenshots $exe $language (Join-Path $reviewFolder $language) 'light' }
        Write-Host "Bilder zur Durchsicht: $reviewFolder"
    }

    # 2. Banner (nutzt results-light.png aus Schritt 1)
    if (-not $SkipBanner) {
        node (Join-Path $repo 'tools\banner\banner.js')
        if ($LASTEXITCODE -ne 0) { throw "Banner fehlgeschlagen ($LASTEXITCODE)" }
    }
}
finally {
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    if ($data) { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue }
}
