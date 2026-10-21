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
      4. Wiederholung: Ein Dienst, der in einem gemessenen Lauf ohne Starttrigger seinen Laufzustand wechselte, wird
         bis zu dreimal nachgeprueft - jeweils erst, wenn er wieder im alten Zustand ist. Kehrt der Wechsel wieder oder
         laesst sich nicht wiederholen, scheitert der Nachweis; nur ein nicht reproduzierbarer Wechsel gilt als
         Hintergrund (auf windows-2025 startet Windows VSS und swprv etwa 11 Minuten nach dem Boot selbst).
    Eine geplante Aufgabe, die sich aendert, gilt nur dann als Windows, wenn das Sicherheitsprotokoll (4698-4702) die
    Aenderung einem Prozess ausserhalb des Prozessbaums der Laeufe zuschreibt (z. B. taskhostw.exe, wenn
    PrintJobCleanupTask sich nach ihrem Lauf selbst abschaltet); sonst scheitert der Nachweis.
    Jede andere Abweichung einer Einstellung (in jeder Phase) und jede nicht kalibrierte Abweichung eines gemessenen
    Laufs laesst den Nachweis scheitern (Exitcode 1). Der Text geht nach -Out (UTF-8, LF). Begruendung und Belege:
    docs/collector.md, Abschnitt "How the proof tells the collector from Windows".

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
function Test-Runtime([string] $key) { $key -like 'svc:*:State' }

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
    # Get-MpPreference scheitert gelegentlich kurz (z. B. waehrend Defender Signaturen laedt): bis zu fuenf Versuche
    $mp = $null
    for ($try = 1; $try -le 5 -and $null -eq $mp; $try++) {
        try { $mp = Get-MpPreference -ErrorAction Stop } catch { if ($try -lt 5) { Start-Sleep -Seconds 10 } }
    }
    if ($null -ne $mp) {
        foreach ($p in $mp.CimInstanceProperties) { $s["mp:$($p.Name)"] = (@($p.Value) | ForEach-Object { [string] $_ }) -join ';' }
    } else { $s['mp'] = 'unavailable' }
    foreach ($task in Get-ScheduledTask) {
        # Ob eine Aufgabe gerade laeuft (Running/Queued/Ready), entscheidet ihr Zeitplan, nicht der Sammler: verglichen
        # wird, ob sie da und ob sie aktiviert ist
        $s["task:$($task.TaskPath)$($task.TaskName)"] = 'present'
        $s["task:$($task.TaskPath)$($task.TaskName):State"] = if ([string] $task.State -eq 'Disabled') { 'disabled' } else { 'enabled' }
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
    # Arbeitsordner des Sammlers: genau „riegel-“ und 8 Hexziffern
    $s['temp:riegel-*'] = (@(Get-ChildItem ([IO.Path]::GetTempPath()) -Directory -Filter 'riegel-*' -ErrorAction SilentlyContinue | Where-Object Name -match '^riegel-[0-9a-f]{8}$' | ForEach-Object Name) -join ';')
    return $s
}

function Compare-State($before, $after) {
    $changes = [Collections.Generic.List[string]]::new()
    $mpGap = $before['mp'] -eq 'unavailable' -or $after['mp'] -eq 'unavailable'
    foreach ($key in @($before.Keys) + @($after.Keys) | Sort-Object -Unique) {
        if ($mpGap -and $key -like 'mp:*') { continue }
        $a = $before[$key]; $b = $after[$key]
        if ($a -ne $b) { $changes.Add($key) }
    }
    return $changes.ToArray()
}

function Describe($before, $after, [string] $key) {
    $a = $before[$key]; $b = $after[$key]
    # Mehrzeilige Werte (reg export, secedit, auditpol): die geaenderten Zeilen nennen, damit ein Befund pruefbar ist
    if ($null -ne $a -and $null -ne $b -and ($a.Contains("`n") -or $b.Contains("`n"))) {
        $lines = @(Compare-Object @($a -split "`r?`n") @($b -split "`r?`n") | Select-Object -First 6 |
            ForEach-Object { "$($_.SideIndicator) $(([string] $_.InputObject).Trim())" })
        if ($lines.Count -gt 0) { return "$key`: changed lines: $($lines -join ' | ')" }
    }
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

# Kindprozesse laut Sicherheitsprotokoll (Ereignis 4688, wenn die Prozessueberwachung an ist) - zweite, von WMI
# unabhaengige Quelle; $null, wenn das Protokoll nicht lesbar ist. Audited = das Ziel selbst steht als 4688 darin.
function Get-ChildrenFromSecurityLog([datetime] $from, [datetime] $to, [int] $rootPid) {
    try { $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4688; StartTime = $from; EndTime = $to } -ErrorAction Stop) }
    catch { $events = @() }
    $created = foreach ($e in $events) {
        $d = @{}
        # InnerText statt '#text': leere <Data>-Elemente haben keinen Textknoten (StrictMode)
        foreach ($n in ([xml] $e.ToXml()).Event.EventData.Data) { $d[$n.Name] = $n.InnerText }
        try {
            [pscustomobject]@{ Pid = [Convert]::ToInt32($d['NewProcessId'], 16); Parent = [Convert]::ToInt32($d['ProcessId'], 16); Name = [IO.Path]::GetFileName([string] $d['NewProcessName']) }
        } catch { }
    }
    $tree = [Collections.Generic.HashSet[int]]::new()
    [void] $tree.Add($rootPid)
    $names = [Collections.Generic.List[string]]::new()
    do {
        $added = $false
        foreach ($c in $created) { if ($tree.Contains($c.Parent) -and $tree.Add($c.Pid)) { $names.Add($c.Name); $added = $true } }
    } while ($added)
    [pscustomobject]@{ Audited = [bool] (@($created | Where-Object { $_.Pid -eq $rootPid }).Count); Names = $names.ToArray(); Pids = $tree }
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

# Ein Lauf mit WMI-Prozessverfolgung: Tree = das Ziel und alle Nachkommen (PIDs), Children = deren Namen
function Invoke-Traced([string] $target, [string] $outDir) {
    $sourceId = "riegel-proc-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
    Register-CimIndicationEvent -ClassName Win32_ProcessStartTrace -SourceIdentifier $sourceId | Out-Null
    $start = Get-Date
    $run = Invoke-Target $target $outDir
    $end = Get-Date
    Start-Sleep -Seconds 2
    $events = @(Get-Event -SourceIdentifier $sourceId -ErrorAction SilentlyContinue)
    Unregister-Event -SourceIdentifier $sourceId
    $events | Remove-Event
    $tree = [Collections.Generic.HashSet[int]]::new()
    [void] $tree.Add([int] $run.Pid)
    $children = [Collections.Generic.List[string]]::new()
    do {
        $added = $false
        foreach ($e in $events) {
            $n = $e.SourceEventArgs.NewEvent
            if ($tree.Contains([int] $n.ParentProcessID) -and $tree.Add([int] $n.ProcessID)) { $children.Add([string] $n.ProcessName); $added = $true }
        }
    } while ($added)
    $run | Add-Member -NotePropertyMembers @{ Tree = $tree; Children = $children.ToArray(); Start = $start; End = $end } -PassThru
}

# Name eines Prozesses: laufend (Get-Process) oder aus seinem Ereignis 4688 der letzten zwei Stunden
function Get-ProcessLabel([int] $id) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p) { return "$($p.ProcessName).exe ($id)" }
    try {
        foreach ($e in @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4688; StartTime = (Get-Date).AddHours(-2) } -ErrorAction Stop)) {
            $d = @{}
            foreach ($n in ([xml] $e.ToXml()).Event.EventData.Data) { $d[$n.Name] = $n.InnerText }
            try { if ([Convert]::ToInt32($d['NewProcessId'], 16) -eq $id) { return "$([IO.Path]::GetFileName([string] $d['NewProcessName'])) ($id)" } } catch { }
        }
    } catch { }
    return "process $id"
}

# Wer hat eine geplante Aufgabe geaendert? Sicherheitsprotokoll 4698 (angelegt), 4699 (geloescht), 4700 (aktiviert),
# 4701 (deaktiviert), 4702 (geaendert) mit ClientProcessId (Windows 10 1809 / Server 2019 und neuer, wenn "Andere
# Objektzugriffsereignisse" ueberwacht werden). Ergebnis: je Ereignis Id, Pid (0 = nicht protokolliert) und Name.
function Get-TaskChangers([string] $taskPath, [datetime] $from, [datetime] $to) {
    try { $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4698, 4699, 4700, 4701, 4702; StartTime = $from; EndTime = $to } -ErrorAction Stop) }
    catch { return @() }
    foreach ($e in $events) {
        $d = @{}
        foreach ($n in ([xml] $e.ToXml()).Event.EventData.Data) { $d[$n.Name] = $n.InnerText }
        if ([string] $d['TaskName'] -ne $taskPath) { continue }
        $client = 0
        [void] [int]::TryParse([string] $d['ClientProcessId'], [ref] $client)
        [pscustomobject]@{ Id = $e.Id; Pid = $client; Name = $(if ($client) { Get-ProcessLabel $client } else { 'no ClientProcessId' }) }
    }
}

# Aenderung einer geplanten Aufgabe ($key task:<Pfad>[:State]) im Fenster: ByWindows, wenn das Sicherheitsprotokoll sie
# nur Prozessen ausserhalb von $trees zuschreibt; Text nennt die Ereignisse und Prozesse (leer: nichts protokolliert)
function Get-TaskChange([string] $key, [datetime] $from, [datetime] $to, $trees) {
    $path = $key.Substring(5) -replace ':State$', ''
    $changers = @(Get-TaskChangers $path $from $to)
    $byWindows = $changers.Count -gt 0
    foreach ($c in $changers) {
        if ($c.Pid -eq 0) { $byWindows = $false }
        foreach ($t in $trees) { if ($t.Contains([int] $c.Pid)) { $byWindows = $false } }
    }
    [pscustomobject]@{ ByWindows = $byWindows; Text = (($changers | ForEach-Object { "event $($_.Id) by $($_.Name)" } | Sort-Object -Unique) -join ', ') }
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

# Installationen und Wartung (MSI-Test, Windows-Update-Stapel) wecken Dienste: erst warten, bis sie abgeklungen sind
$busy = 'msiexec', 'TiWorker', 'TrustedInstaller', 'MoUsoCoreWorker', 'wuauclt', 'SetupHost'
$quiet = [Diagnostics.Stopwatch]::StartNew()
while ($quiet.Elapsed.TotalSeconds -lt 300 -and @(Get-Process -Name $busy -ErrorAction SilentlyContinue).Count -gt 0) { Start-Sleep -Seconds 5 }
$still = @(Get-Process -Name $busy -ErrorAction SilentlyContinue | ForEach-Object Name | Sort-Object -Unique)
# Danach, bis Dienste und Aufgaben zweimal hintereinander gleich sind (Folgestarts nach einer Installation)
function Get-Quick { (@(Get-CimInstance Win32_Service -Property Name, State | ForEach-Object { "$($_.Name)=$($_.State)" }) +
    @(Get-ScheduledTask | ForEach-Object { "$($_.TaskPath)$($_.TaskName)=$([string] $_.State -eq 'Disabled')" }) | Sort-Object) -join "`n" }
$stable = [Diagnostics.Stopwatch]::StartNew()
$last = Get-Quick
do {
    Start-Sleep -Seconds 10
    $now = Get-Quick
    $same = $now -eq $last
    $last = $now
} while (-not $same -and $stable.Elapsed.TotalSeconds -lt 120)
Line "Settle: waited $([Math]::Round($quiet.Elapsed.TotalSeconds)) s for installer activity to end$(if ($still) { " (still running: $($still -join ', '))" }); services and tasks $(if ($same) { 'stable' } else { 'not yet stable' }) after $([Math]::Round($stable.Elapsed.TotalSeconds)) s"
Line ''

$detectionsBefore = @(try { Get-MpThreatDetection } catch { @() }).Count
$s0 = Get-RunnerState
$wu0 = "$($s0['svc:wuauserv:StartMode'])/$($s0['svc:wuauserv:State'])"

# 1. Aufwaermen
$longest = 0
$warmFrom = Get-Date
$warmTrees = [Collections.Generic.List[object]]::new()
$allTrees = [Collections.Generic.List[object]]::new() # Prozessbaeume aller Laeufe (Zuordnung von Aufgabenaenderungen)
foreach ($t in $Target) {
    $run = Invoke-Traced $t (Join-Path $work "warmup-$t")
    $warmTrees.Add($run.Tree)
    $warmTrees.Add((Get-ChildrenFromSecurityLog $run.Start $run.End $run.Pid).Pids)
    $allTrees.Add($run.Tree)
    $longest = [Math]::Max($longest, $run.Ms)
}
$s1 = Get-RunnerState
$warmTo = Get-Date
$warmCandidates = [Collections.Generic.List[string]]::new()
Line '== Warm-up (first run of every target)'
foreach ($key in @(Compare-State $s0 $s1)) {
    $text = Describe $s0 $s1 $key
    if ($key -like 'task:*') {
        $change = Get-TaskChange $key $warmFrom $warmTo $warmTrees
        if ($change.ByWindows) { Line "  scheduled task changed by Windows (Security log: $($change.Text); not a process of the runs): $text"; continue }
        Line "  scheduled task changed; Security log: $(if ($change.Text) { "$($change.Text) - a process of the runs or not attributable" } else { 'no event 4698-4702 for it' })"
    }
    if (-not (Test-Runtime $key)) { Fail "configuration changed during warm-up: $text"; continue }
    # Laufzustaende im (nicht kalibrierten) Aufwaermen: aufgelistet; Starts ohne Trigger prueft die Wiederholung
    $name = $key.Split(':')[1]
    if ($name -eq 'wuauserv') { Line "  note (V1, see the measured runs): $text"; continue }
    if (Test-TriggerStart $name) { Line "  on-demand start/stop by Windows (service has a start trigger, StartMode unchanged): $text"; continue }
    if ($name -in 'WaaSMedicSvc', 'UsoSvc', 'TrustedInstaller' -and -not (Compare-State $s0 $s1 | Where-Object { $_ -eq "svc:$($name):StartMode" })) {
        Line "  Windows servicing service changed state on its own (StartMode unchanged; listed, not counted): $text"; continue
    }
    if ([string] $s1[$key] -eq 'Stopped') { Line "  service stopped during warm-up (a reading program cannot stop a service; listed): $text"; continue }
    # Ein Start beim Aufwaermen ist im gemessenen Lauf unsichtbar, solange der Dienst noch laeuft: Wiederholung unten
    Line "  service started without a start trigger (judged by repetition below): $text"
    $warmCandidates.Add($key)
}
Line ''

# 2. Kalibrierung
$pause = [Math]::Max(30000, 2 * $longest)
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
$previousFrom = $calEnd
$wuChanged = $false
$measured = [Collections.Generic.List[object]]::new()
$excludedStarts = [Collections.Generic.List[object]]::new()
foreach ($t in $Target) {
    $run = Invoke-Traced $t (Join-Path $work "run-$t")
    $start = $run.Start; $end = $run.End; $children = $run.Children
    $logged = Get-ChildrenFromSecurityLog $start $end $run.Pid
    $allTrees.Add($run.Tree)
    $allTrees.Add($logged.Pids)
    $afterFrom = Get-Date
    $after = Get-RunnerState
    $afterTo = Get-Date
    $changes = @(Compare-State $previous $after | Where-Object { $_ -notin $excluded })
    foreach ($key in @(Compare-State $previous $after | Where-Object { $_ -in $excluded -and $_ -like 'svc:*:State' -and $_ -ne 'svc:wuauserv:State' })) {
        $name = $key.Split(':')[1]
        if ([string] $after[$key] -ne 'Stopped' -and $name -notin 'WaaSMedicSvc', 'UsoSvc', 'TrustedInstaller' -and -not (Test-TriggerStart $name)) {
            $excludedStarts.Add([pscustomobject]@{ Target = $t; Key = $key; Text = (Describe $previous $after $key); Before = $previous[$key]; After = $after[$key] })
        }
    }
    foreach ($key in $changes) {
        $startMode = if ($key -like 'svc:*:State') { $after[($key -replace ':State$', ':StartMode')] } else { $null }
        $measured.Add([pscustomobject]@{ Target = $t; Key = $key; Text = (Describe $previous $after $key); Before = $previous[$key]; After = $after[$key]; StartMode = $startMode
                                         From = $previousFrom; To = $afterTo })
    }
    Line "== Measured run: $t (exit code $($run.ExitCode), $($run.Ms) ms)"
    if ($run.ExitCode -notin 0, 1) { Fail "$t exited with $($run.ExitCode)" }
    if (-not (Test-OutputFolder $t $run.Out)) { Fail "$t wrote something else than its export and checksum: $((Get-ChildItem $run.Out -Force | ForEach-Object Name) -join ', ')" }
    else { Line '  written: only the export and its .sha256 (builtin: only its bundle folder)' }
    if ($changes.Count -eq 0) { Line '  no difference after calibration' }
    else { foreach ($key in $changes) { Line "  difference (judged after the second calibration): $(Describe $previous $after $key)" } }
    $own = @($children | Where-Object { $_ -ne 'conhost.exe' })
    Line "  child processes (WMI process trace): $(if ($children.Count) { ($children | Sort-Object) -join ', ' } else { 'none' })"
    $ownLogged = @($logged.Names | Where-Object { $_ -ne 'conhost.exe' })
    if ($logged.Audited) { Line "  child processes (Security log 4688): $(if ($logged.Names.Count) { ($logged.Names | Sort-Object) -join ', ' } else { 'none' })" }
    else { Line '  child processes (Security log 4688): process-creation auditing is off on this runner' }
    if ($t -eq 'minimal' -and ($own.Count -gt 0 -or $ownLogged.Count -gt 0)) { Fail "--minimal started processes: $((@($own) + @($ownLogged) | Sort-Object -Unique) -join ', ')" }
    if ($t -eq 'minimal' -and $own.Count -eq 0 -and $ownLogged.Count -eq 0) { Line '  --minimal started no process' }
    if ($after['temp:riegel-*'] -ne $previous['temp:riegel-*']) { Fail "$t left a %TEMP%\riegel-* folder: $($after['temp:riegel-*'])" }
    Line "  Security log event IDs during the run (V2): $(Get-SecurityEventIds $start $end)"
    Line ''
    $previous = $after
    $previousFrom = $afterFrom
}

# Zweite Kalibrierung nach den Laeufen: was sich dann von selbst aendert, war auch vorher Hintergrund
$cal2Start = Get-Date
Start-Sleep -Milliseconds $pause
$cal2End = Get-Date
$s9 = Get-RunnerState
$excluded2 = @(Compare-State $previous $s9)
Line "== Second calibration (null run, $([Math]::Round($pause / 1000)) s after the measured runs)"
if ($excluded2.Count -eq 0) { Line '  nothing changed on its own' }
foreach ($key in $excluded2) { Line "  excluded (changes on its own): $(Describe $previous $s9 $key)" }
Line "  Security log event IDs without the collector (background): $(Get-SecurityEventIds $cal2Start $cal2End)"
Line ''
Line '== Differences of the measured runs'
# Laufzustaende, die sich auch in der zweiten Kalibrierung von selbst aendern, sind Hintergrund; Einstellungen nie
$open = @($measured | Where-Object { -not (Test-Runtime $_.Key) -or $_.Key -notin $excluded2 })
$recheck = [Collections.Generic.List[object]]::new()
foreach ($m in $measured) {
    if ($m.Key -like 'svc:wuauserv:*' -and $open -contains $m) { $wuChanged = $true }
    # Geplante Aufgabe: nur wenn das Sicherheitsprotokoll die Aenderung einem Prozess ausserhalb aller Laeufe zuschreibt
    # (z. B. taskhostw.exe, wenn PrintJobCleanupTask sich nach ihrem Lauf selbst abschaltet)
    if ($open -contains $m -and $m.Key -like 'task:*') {
        $change = Get-TaskChange $m.Key $m.From $m.To $allTrees
        if ($change.ByWindows) { Line "  scheduled task changed by Windows (Security log: $($change.Text); not a process of the runs): $($m.Target): $($m.Text)"; continue }
        Line "  scheduled task changed; Security log: $(if ($change.Text) { "$($change.Text) - a process of the runs or not attributable" } else { 'no event 4698-4702 for it' })"
    }
    # Dienst mit Starttrigger (StartMode unveraendert): Windows startet ihn bei Bedarf (z. B. gpsvc oder RemoteRegistry,
    # wenn gpresult sie anspricht) und beendet ihn im Leerlauf - kein Eingriff des Sammlers; wuauserv bleibt streng (V1)
    $service = if ($m.Key -like 'svc:*:State') { $m.Key.Split(':')[1] } else { $null }
    if ($open -contains $m -and $service -and $service -ne 'wuauserv' -and (Test-TriggerStart $service)) {
        Line "  on-demand start/stop by Windows (service has a start trigger, StartMode unchanged): $($m.Target): $($m.Text)"
        continue
    }
    # Wartungsdienste von Windows Update und Windows Installer (Update-Medic, Update-Orchestrator, Installer): Windows
    # startet und beendet sie nach eigenem Zeitplan; aufgelistet, nicht gezaehlt, solange der Starttyp gleich bleibt
    if ($open -contains $m -and $service -in 'WaaSMedicSvc', 'UsoSvc', 'TrustedInstaller' -and
        -not ($measured | Where-Object { $_.Key -eq "svc:$($service):StartMode" })) {
        Line "  Windows servicing service changed state on its own (StartMode unchanged; listed, not counted): $($m.Target): $($m.Text)"
        continue
    }
    if ($open -contains $m -and $service -and $service -ne 'wuauserv' -and $m.Before -eq 'Running' -and $m.After -eq 'Stopped' -and
        $m.StartMode -eq 'Manual' -and -not ($measured | Where-Object { $_.Key -eq "svc:$($service):StartMode" })) {
        Line "  demand-start service ended by Windows when idle (StartMode Manual, unchanged; a reading program cannot stop a service): $($m.Target): $($m.Text)"
        continue
    }
    # Anderer Laufzustand eines Dienstes ohne Starttrigger (Starttyp unveraendert, nicht wuauserv): erst die
    # Wiederholung entscheidet (auf windows-2025 startet Windows VSS und swprv etwa 11 Minuten nach dem Boot selbst)
    if ($open -contains $m -and $service -and $service -ne 'wuauserv' -and
        -not ($measured | Where-Object { $_.Key -eq "svc:$($service):StartMode" })) {
        Line "  service state changed without a start trigger (judged by repetition below): $($m.Target): $($m.Text)"
        $recheck.Add($m)
        continue
    }
    if ($open -contains $m) { Fail "$($m.Target) changed: $($m.Text)" }
    else { Line "  background (also changed on its own in the second calibration): $($m.Target): $($m.Text)" }
}
if ($measured.Count -eq 0) { Line '  none' }
Line ''

# 4. Wiederholung. Ein Zustandswechsel, den ein Ziel verursacht, kommt bei jedem Lauf wieder. Gepruefte Dienste: die
# offenen Wechsel der gemessenen Laeufe und die Wechsel beim Aufwaermen, die bis zu den gemessenen Laeufen anhielten
# (dort waeren sie unsichtbar: VSS endet erst nach drei Minuten Leerlauf, die Kalibrierung dauert 30 s). Erst wenn jeder
# Dienst wieder im alten Zustand ist, laeuft jedes Ziel noch einmal, mit dem Zustand der Dienste direkt davor und
# danach. Drei gueltige Wiederholungen je Ziel ohne den Wechsel machen ihn zu Hintergrund; jeder erneute Wechsel, und
# jeder Dienst, der nicht in den alten Zustand zurueckkehrt, bleibt ein Fehler.
foreach ($key in $warmCandidates) {
    if (-not ($recheck | Where-Object { $_.Key -eq $key })) {
        $recheck.Add([pscustomobject]@{ Target = 'warm-up'; Key = $key; Text = (Describe $s0 $s1 $key); Before = $s0[$key]; After = $s1[$key] })
    }
}
# Ein Start in einem gemessenen Lauf bei einem Dienst, den die Kalibrierung ausgeschlossen hat, wird ebenfalls wiederholt
foreach ($m in $excludedStarts) {
    if (-not ($recheck | Where-Object { $_.Key -eq $m.Key })) { Line "  start of a calibrated service in a measured run (judged by repetition below): $($m.Target): $($m.Text)"; $recheck.Add($m) }
}
function Get-ServiceState([string] $key) { [string] (Get-CimInstance Win32_Service -Filter "Name='$($key.Split(':')[1])'" -Property State).State }
if ($recheck.Count -gt 0) {
    Line '== Repetition of service state changes (every target 3 times, each run with the service in its previous state)'
    $checks = @($recheck | Group-Object Key | ForEach-Object { $_.Group[0] })
    $valid = @{}
    $failed = @{}
    foreach ($c in $checks) { foreach ($t in $Target) { $valid["$($c.Key)|$t"] = 0 } }
    function Test-Done($c) { $failed.ContainsKey($c.Key) -or -not ($Target | Where-Object { $valid["$($c.Key)|$_"] -lt 3 }) }
    for ($round = 1; $round -le 5 -and @($checks | Where-Object { -not (Test-Done $_) }).Count -gt 0; $round++) {
        $pending = @($checks | Where-Object { -not (Test-Done $_) })
        $wait = [Diagnostics.Stopwatch]::StartNew()
        do {
            $notBack = @($pending | Where-Object { (Get-ServiceState $_.Key) -ne $_.Before })
            if ($notBack.Count -gt 0) { Start-Sleep -Seconds 5 }
        } while ($notBack.Count -gt 0 -and $wait.Elapsed.TotalSeconds -lt 480)
        foreach ($c in $notBack) { Fail "$($c.Target) changed: $($c.Text) (not repeatable: the service did not return to '$($c.Before)' within 480 s)"; $failed[$c.Key] = $true }
        $pending = @($pending | Where-Object { $notBack -notcontains $_ })
        if ($pending.Count -eq 0) { break }
        Line "  round $round (waited $([Math]::Round($wait.Elapsed.TotalSeconds)) s for $(($pending | ForEach-Object { $_.Key.Split(':')[1] + '=' + $_.Before }) -join ', '))"
        foreach ($t in $Target) {
            $pre = @{}; foreach ($c in $pending) { $pre[$c.Key] = Get-ServiceState $c.Key }
            $run = Invoke-Traced $t (Join-Path $work "repeat$round-$t")
            $allTrees.Add($run.Tree)
            $post = @{}; foreach ($c in $pending) { $post[$c.Key] = Get-ServiceState $c.Key }
            if ($run.ExitCode -notin 0, 1) { Fail "$t exited with $($run.ExitCode) in repetition round $round" }
            if (-not (Test-OutputFolder $t $run.Out)) { Fail "$t wrote something else than its export and checksum in repetition round $round" }
            $notes = foreach ($c in $pending) {
                $name = $c.Key.Split(':')[1]
                if ($failed.ContainsKey($c.Key)) { continue }
                if ($pre[$c.Key] -ne $c.Before) { "$name was '$($pre[$c.Key])' before (not counted)"; continue }
                if ($post[$c.Key] -eq $c.After) { Fail "$t changed: $($c.Key): '$($c.Before)' -> '$($c.After)' (repeated in repetition round $round)"; $failed[$c.Key] = $true; "$name REPEATED"; continue }
                $valid["$($c.Key)|$t"]++
                "$name unchanged"
            }
            Line "    $t (exit code $($run.ExitCode), $($run.Ms) ms): $(@($notes) -join '; ')"
        }
    }
    foreach ($c in $checks) {
        if ($failed.ContainsKey($c.Key)) { continue }
        $short = @($Target | Where-Object { $valid["$($c.Key)|$_"] -lt 3 })
        if ($short.Count -eq 0) { Line "  not reproducible: no target repeated it in 3 runs each - a change by Windows (listed, not counted): $($c.Target): $($c.Text)" }
        else { Fail "$($c.Target) changed: $($c.Text) (could not be repeated three times for: $($short -join ', '))" }
    }
    Line ''
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
if ($excluded -contains 'svc:wuauserv:State' -or $excluded2 -contains 'svc:wuauserv:State') { Line '  V1 not verifiable on this runner: wuauserv changed on its own during calibration' }
elseif (-not $wuChanged) { Line '  untouched by every measured run' }
Line ''
Line ("RESULT: " + $(if ($failures.Count -eq 0) { 'PASS - no configuration or state difference after calibration' } else { "FAIL - $($failures.Count) difference(s)" }))

# Relativ zum PowerShell-Standort aufloesen, nicht zum Prozessverzeichnis
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)
New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
[IO.File]::WriteAllText($target, ($report -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
$report | ForEach-Object { Write-Host $_ }
exit $(if ($failures.Count -eq 0) { 0 } else { 1 })
