# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Nur-Lesen-Nachweis des Sammlers (spec 4.3, 8.9) - NUR auf Wegwerf-Runnern von GitHub Actions.

.DESCRIPTION
    Vergleicht den Zustand des Runners vor und nach jedem Lauf: secedit /export, auditpol /backup, reg export der
    Schluessel aus spec 8.9, alle Dienste mit StartMode UND State, wuauserv, Get-MpPreference, geplante Aufgaben
    (Pfad, Name, Zustand), FwPolicy2 und den Hash von Registry.pol.

    Ablauf:
      1. Zustand S0, dann jedes Ziel einmal zum Aufwaermen (Erstlauf-Effekte wie ein von Windows per Trigger
         gestarteter Dienst werden hier sichtbar und aufgelistet).
      2. Kalibrierung: Nulllauf ohne Sammler; was sich dabei von selbst aendert, wird ausgeschlossen und genannt.
      3. Je Ziel ein gemessener Lauf mit Vorher/Nachher, den dabei entstandenen Ereignis-IDs des
         Sicherheitsprotokolls, gestarteten Kindprozessen und den Defender-Erkennungen.
    Jede Abweichung einer Einstellung (in jeder Phase) und jede nicht kalibrierte Abweichung eines gemessenen Laufs
    laesst den Nachweis scheitern (Exitcode 1). Der Text geht nach -Out (UTF-8, LF).

.PARAMETER Target
    collector, deep (--deep), minimal (--minimal, darf keinen Prozess starten), cli (Riegel.Cli.exe collect),
    builtin (collect-builtin.cmd); mehrere mit Komma.

.PARAMETER Exe
    Programm je Ziel: eines fuer alle oder genau so viele wie Ziele.

.PARAMETER Out
    Textdatei des Nachweises.

.EXAMPLE
    ./tools/readonly-proof/Invoke-ReadOnlyProof.ps1 -Target collector -Exe out/collector-bundle/Riegel.Collector.exe -Out dist/Riegel-Collector-ReadOnlyProof.txt
#>
param(
    [Parameter(Mandatory)] [string[]] $Target,
    [Parameter(Mandatory)] [string[]] $Exe,
    [Parameter(Mandatory)] [string] $Out
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 3

if ($env:GITHUB_ACTIONS -ne 'true') {
    [Console]::Error.WriteLine('Invoke-ReadOnlyProof.ps1 runs only on GitHub-hosted runners (GITHUB_ACTIONS=true), never on a workstation.')
    exit 2
}
$Target = @($Target | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$Exe = @($Exe | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Exe.Count -ne 1 -and $Exe.Count -ne $Target.Count) { throw '-Exe: ein Pfad fuer alle Ziele oder einer je Ziel.' }
foreach ($t in $Target) { if ($t -notin 'collector', 'deep', 'minimal', 'cli', 'builtin') { throw "Unbekanntes Ziel: $t" } }
$exes = @{}
for ($i = 0; $i -lt $Target.Count; $i++) { $exes[$Target[$i]] = (Resolve-Path ($Exe[[Math]::Min($i, $Exe.Count - 1)])).Path }

$work = Join-Path ($env:RUNNER_TEMP ?? [IO.Path]::GetTempPath()) ("riegel-proof-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null
$report = [Collections.Generic.List[string]]::new()
$failures = [Collections.Generic.List[string]]::new()
function Line([string] $text = '') { $report.Add($text) }
function Fail([string] $text) { $failures.Add($text); $report.Add("FAIL: $text") }

# ---------------------------------------------------------------- Zustand des Runners

# Schluessel der Zustands-Tabelle: Einstellungen (config) und Laufzustaende (runtime: Dienst- und Aufgabenzustand)
function Test-Runtime([string] $key) { $key -like 'svc:*:State' -or $key -like 'task:*:State' }

$script:stateNo = 0
function Get-RunnerState {
    $script:stateNo++
    $n = $script:stateNo
    $s = [ordered]@{}
    $inf = Join-Path $work "s$n-secedit.inf"
    & secedit.exe /export /cfg $inf /log (Join-Path $work "s$n-secedit.log") /quiet | Out-Null
    $s['secedit'] = if (Test-Path $inf) { (Get-Content $inf -Raw -Encoding Unicode) } else { "exit $LASTEXITCODE" }
    $csv = Join-Path $work "s$n-audit.csv"
    & auditpol.exe /backup "/file:$csv" | Out-Null
    $s['auditpol'] = if (Test-Path $csv) { (Get-Content $csv -Raw) } else { "exit $LASTEXITCODE" }
    $i = 0
    foreach ($key in 'HKLM\SOFTWARE\Policies', 'HKLM\SYSTEM\CurrentControlSet\Control\Lsa',
                     'HKLM\SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters',
                     'HKLM\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy') {
        $i++
        $file = Join-Path $work "s$n-reg$i.reg"
        & reg.exe export $key $file /y | Out-Null
        $s["reg:$key"] = if (Test-Path $file) { (Get-Content $file -Raw -Encoding Unicode) } else { "exit $LASTEXITCODE" }
    }
    foreach ($svc in Get-CimInstance Win32_Service -Property Name, StartMode, State) {
        $s["svc:$($svc.Name):StartMode"] = [string] $svc.StartMode
        $s["svc:$($svc.Name):State"] = [string] $svc.State
    }
    try {
        $mp = Get-MpPreference
        foreach ($p in $mp.CimInstanceProperties) { $s["mp:$($p.Name)"] = (@($p.Value) | ForEach-Object { [string] $_ }) -join ';' }
    } catch { $s['mp'] = 'unavailable' }
    foreach ($task in Get-ScheduledTask) {
        $s["task:$($task.TaskPath)$($task.TaskName)"] = 'present'
        $s["task:$($task.TaskPath)$($task.TaskName):State"] = [string] $task.State
    }
    try {
        $fw = New-Object -ComObject HNetCfg.FwPolicy2
        foreach ($p in 1, 2, 4) {
            $s["fw:$($p):enabled"] = [string] $fw.FirewallEnabled($p)
            $s["fw:$($p):inbound"] = [string] $fw.DefaultInboundAction($p)
            $s["fw:$($p):outbound"] = [string] $fw.DefaultOutboundAction($p)
        }
        $rules = foreach ($r in $fw.Rules) { "$($r.Name)|$($r.Enabled)|$($r.Direction)|$($r.Action)|$($r.Protocol)|$($r.LocalPorts)|$($r.RemoteAddresses)|$($r.ApplicationName)|$($r.ServiceName)|$($r.Profiles)" }
        $sorted = [string[]] @($rules)
        [Array]::Sort($sorted, [StringComparer]::Ordinal)
        $s['fw:rules'] = "$($sorted.Count) " + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($sorted -join "`n")))
    } catch { $s['fw'] = "error $($_.Exception.Message)" }
    $pol = Join-Path $env:SystemRoot 'System32\GroupPolicy\Machine\Registry.pol'
    $s['registry.pol'] = if (Test-Path $pol) { (Get-FileHash $pol -Algorithm SHA256).Hash } else { 'absent' }
    $s['temp:riegel-*'] = (@(Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'riegel-*' -ErrorAction SilentlyContinue | ForEach-Object Name) -join ';')
    return $s
}

function Compare-State($before, $after) {
    $changes = [Collections.Generic.List[string]]::new()
    foreach ($key in @($before.Keys) + @($after.Keys) | Sort-Object -Unique) {
        $a = $before[$key]; $b = $after[$key]
        if ($a -ne $b) { $changes.Add($key) }
    }
    return $changes.ToArray()
}

function Describe($before, $after, [string] $key) {
    $a = $before[$key]; $b = $after[$key]
    if ($null -ne $a -and $a.Length -gt 80) { $a = '<' + $a.Length + ' chars>' }
    if ($null -ne $b -and $b.Length -gt 80) { $b = '<' + $b.Length + ' chars>' }
    "$key`: '$a' -> '$b'"
}

# Dienst mit Starttrigger (sc qtriggerinfo): Windows startet ihn bei Bedarf selbst, z. B. gpsvc bei einem RPC-Aufruf
function Test-TriggerStart([string] $service) {
    $info = & sc.exe qtriggerinfo $service 2>$null
    return ($info -join "`n") -match 'START SERVICE'
}

# Ereignis-IDs des Sicherheitsprotokolls in einem Zeitfenster (V2)
function Get-SecurityEventIds([datetime] $from, [datetime] $to) {
    try {
        $ids = Get-WinEvent -FilterHashtable @{ LogName = 'Security'; StartTime = $from; EndTime = $to } -ErrorAction Stop |
            Group-Object Id | Sort-Object { [int] $_.Name } | ForEach-Object { "$($_.Name) x$($_.Count)" }
        if ($ids) { return ($ids -join ', ') } else { return 'none' }
    } catch { return "none or not readable ($($_.Exception.Message))" }
}

# ---------------------------------------------------------------- Ziele

function Invoke-Target([string] $target, [string] $outDir) {
    New-Item -ItemType Directory -Force $outDir | Out-Null
    $exe = $exes[$target]
    switch ($target) {
        'collector' { $file = $exe; $arguments = @('--out', $outDir) }
        'deep' { $file = $exe; $arguments = @('--out', $outDir, '--deep') }
        'minimal' { $file = $exe; $arguments = @('--out', $outDir, '--minimal') }
        'cli' { $file = $exe; $arguments = @('collect', '--out', $outDir) }
        # cmd /c entfernt das erste und letzte Anfuehrungszeichen: deshalb die ganze Zeile noch einmal in Anfuehrungszeichen
        'builtin' { $file = Join-Path $env:SystemRoot 'System32\cmd.exe'; $arguments = @('/c', "`"`"$exe`" `"$outDir`"`"") }
    }
    $stdout = Join-Path $work "$target-$([Guid]::NewGuid().ToString('N').Substring(0, 6)).out"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $file -ArgumentList $arguments -NoNewWindow -PassThru -Wait -RedirectStandardOutput $stdout -RedirectStandardError "$stdout.err"
    [pscustomobject]@{ ExitCode = $p.ExitCode; Pid = $p.Id; Ms = $watch.ElapsedMilliseconds; Out = $outDir }
}

function Test-OutputFolder([string] $target, [string] $dir) {
    $entries = @(Get-ChildItem $dir -Force)
    if ($target -eq 'builtin') {
        return ($entries.Count -eq 1 -and $entries[0].PSIsContainer -and $entries[0].Name -like '*.riegel-builtin')
    }
    $files = @($entries | Where-Object { -not $_.PSIsContainer } | ForEach-Object Name | Sort-Object)
    return ($entries.Count -eq 2 -and $files.Count -eq 2 -and $files[0] -like '*.snapshot.json' -and $files[1] -eq "$($files[0]).sha256")
}

# ---------------------------------------------------------------- Ablauf

$os = Get-CimInstance Win32_OperatingSystem
Line 'Riegel collector - read-only proof / Nur-Lesen-Nachweis'
Line "Runner: $($env:ImageOS) $($env:ImageVersion), $($os.Caption) build $($os.BuildNumber), $($env:PROCESSOR_ARCHITECTURE)"
Line "Date (UTC): $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss'))"
foreach ($t in $Target) {
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($exes[$t])
    Line "Target $t`: $(Split-Path $exes[$t] -Leaf) $($info.ProductVersion) SHA-256 $((Get-FileHash $exes[$t] -Algorithm SHA256).Hash.ToLower())"
}
Line 'Compared: secedit /export, auditpol /backup, reg export (Policies, Lsa, LanmanServer\Parameters, FirewallPolicy), all services (StartMode and State), Get-MpPreference, scheduled tasks (path, name, state), HNetCfg.FwPolicy2 (profiles, rules), Registry.pol hash, %TEMP%\riegel-* folders.'
Line ''

$detectionsBefore = @(try { Get-MpThreatDetection } catch { @() }).Count
$s0 = Get-RunnerState
$wu0 = "$($s0['svc:wuauserv:StartMode'])/$($s0['svc:wuauserv:State'])"

# 1. Aufwaermen
$longest = 0
foreach ($t in $Target) {
    $run = Invoke-Target $t (Join-Path $work "warmup-$t")
    $longest = [Math]::Max($longest, $run.Ms)
}
$s1 = Get-RunnerState
Line '== Warm-up (first run of every target)'
foreach ($key in @(Compare-State $s0 $s1)) {
    $text = Describe $s0 $s1 $key
    if (-not (Test-Runtime $key)) { Fail "configuration changed during warm-up: $text"; continue }
    if ($key -like 'svc:*:State') {
        $name = $key.Split(':')[1]
        if ($name -eq 'wuauserv') { Line "  note (V1, see calibration): $text"; continue }
        if (Test-TriggerStart $name) { Line "  on-demand start/stop by Windows (service has a start trigger, StartMode unchanged): $text"; continue }
        Fail "service state changed during warm-up: $text"
        continue
    }
    Line "  task state (not attributed, checked again in the measured runs): $text"
}
Line ''

# 2. Kalibrierung
$pause = [Math]::Max(20000, $longest)
$calStart = Get-Date
Start-Sleep -Milliseconds $pause
$calEnd = Get-Date
$s2 = Get-RunnerState
$excluded = @(Compare-State $s1 $s2)
Line "== Calibration (null run, $([Math]::Round($pause / 1000)) s without the collector)"
if ($excluded.Count -eq 0) { Line '  nothing changed on its own' }
foreach ($key in $excluded) {
    Line "  excluded (changes on its own): $(Describe $s1 $s2 $key)"
    if (-not (Test-Runtime $key)) { Line "  WARNING: a configuration value changes on its own on this runner: $key" }
}
Line "  Security log event IDs without the collector (background): $(Get-SecurityEventIds $calStart $calEnd)"
Line ''

# 3. Gemessene Laeufe
$previous = $s2
$wuChanged = $false
foreach ($t in $Target) {
    $sourceId = "riegel-proc-$t"
    Register-CimIndicationEvent -ClassName Win32_ProcessStartTrace -SourceIdentifier $sourceId | Out-Null
    $start = Get-Date
    $run = Invoke-Target $t (Join-Path $work "run-$t")
    $end = Get-Date
    Start-Sleep -Seconds 2
    $events = @(Get-Event -SourceIdentifier $sourceId -ErrorAction SilentlyContinue)
    Unregister-Event -SourceIdentifier $sourceId
    $events | Remove-Event
    $tree = [Collections.Generic.HashSet[uint32]]::new()
    [void] $tree.Add([uint32] $run.Pid)
    $children = [Collections.Generic.List[string]]::new()
    do {
        $added = $false
        foreach ($e in $events) {
            $n = $e.SourceEventArgs.NewEvent
            if ($tree.Contains([uint32] $n.ParentProcessID) -and $tree.Add([uint32] $n.ProcessID)) { $children.Add([string] $n.ProcessName); $added = $true }
        }
    } while ($added)
    $after = Get-RunnerState
    $changes = @(Compare-State $previous $after | Where-Object { $_ -notin $excluded })
    Line "== Measured run: $t (exit code $($run.ExitCode), $($run.Ms) ms)"
    if ($run.ExitCode -notin 0, 1) { Fail "$t exited with $($run.ExitCode)" }
    if (-not (Test-OutputFolder $t $run.Out)) { Fail "$t wrote something else than its export and checksum: $((Get-ChildItem $run.Out -Force | ForEach-Object Name) -join ', ')" }
    else { Line '  written: only the export and its .sha256 (builtin: only its bundle folder)' }
    foreach ($key in $changes) {
        if ($key -like 'svc:wuauserv:*') { $wuChanged = $true }
        Fail "$t changed: $(Describe $previous $after $key)"
    }
    if ($changes.Count -eq 0) { Line '  no difference after calibration' }
    $own = @($children | Where-Object { $_ -ne 'conhost.exe' })
    Line "  child processes: $(if ($children.Count) { ($children | Sort-Object) -join ', ' } else { 'none' })"
    if ($t -eq 'minimal' -and $own.Count -gt 0) { Fail "--minimal started processes: $($own -join ', ')" }
    if ($after['temp:riegel-*'] -ne $previous['temp:riegel-*']) { Fail "$t left a %TEMP%\riegel-* folder: $($after['temp:riegel-*'])" }
    Line "  Security log event IDs during the run (V2): $(Get-SecurityEventIds $start $end)"
    Line ''
    $previous = $after
}

# Abschluss
$detections = @(try { Get-MpThreatDetection } catch { @() })
$newDetections = $detections.Count - $detectionsBefore
Line '== Defender detections (V3)'
if ($newDetections -gt 0) {
    foreach ($d in $detections | Select-Object -Last $newDetections) { Fail "Defender detection: threat $($d.ThreatID), $(@($d.Resources) -join '; ')" }
} else { Line '  none' }
Line ''
$wuEnd = "$($previous['svc:wuauserv:StartMode'])/$($previous['svc:wuauserv:State'])"
Line '== wuauserv (V1)'
Line "  before: $wu0, after: $wuEnd"
if ($excluded -contains 'svc:wuauserv:State') { Line '  V1 not verifiable on this runner: wuauserv changed on its own during calibration' }
elseif (-not $wuChanged) { Line '  untouched by every measured run' }
Line ''
Line ("RESULT: " + $(if ($failures.Count -eq 0) { 'PASS - no configuration or state difference after calibration' } else { "FAIL - $($failures.Count) difference(s)" }))

# Relativ zum PowerShell-Standort aufloesen, nicht zum Prozessverzeichnis
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
[IO.File]::WriteAllText($target, ($report -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$report | ForEach-Object { Write-Host $_ }
exit $(if ($failures.Count -eq 0) { 0 } else { 1 })
