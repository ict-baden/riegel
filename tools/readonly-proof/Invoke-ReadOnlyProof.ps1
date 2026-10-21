# SPDX-License-Identifier: MIT
<#
.SYNOPSIS
    Nur-Lesen-Nachweis des Sammlers (spec 4.3, 8.9) - NUR auf Wegwerf-Runnern von GitHub Actions.

.DESCRIPTION
    Vergleicht den Zustand des Runners vor und nach jedem Lauf: secedit /export, auditpol /backup, reg export der
    Schluessel aus spec 8.9, alle Dienste mit StartMode UND State, wuauserv, Get-MpPreference, geplante Aufgaben
    (Pfad, Name, Zustand), FwPolicy2 und den Hash von Registry.pol.

    Ablauf:
      1. Zustand S0, dann jedes Ziel einmal zum Aufwaermen.
      2. Kalibrierung: Nulllauf ohne Sammler. Einstellungen, die sich dabei von selbst aendern, werden ausgeschlossen
         und gemeldet; Laufzustaende von Diensten nur genannt (sie werden trotzdem bewertet).
      3. Je Ziel ein gemessener Lauf mit Vorher/Nachher, den dabei entstandenen Ereignis-IDs des
         Sicherheitsprotokolls, gestarteten Kindprozessen und den Defender-Erkennungen; danach eine zweite Kalibrierung.
      4. Wiederholung: Jeder Wechsel eines Dienst-Laufzustands (beim Aufwaermen oder in einem gemessenen Lauf), den
         Windows nicht nach seiner Bauart selbst vornimmt (Starttrigger, Wartungsdienste, Leerlauf-Ende eines
         Bedarfsdienstes), wird nachgeprueft: jedes Ziel noch dreimal, jeweils erst, wenn der Dienst wieder im alten
         Zustand ist, danach 20 s Beobachtung. Jede erneute Abweichung und jeder Dienst, der nicht zurueckkehrt, laesst
         den Nachweis scheitern; nur ein Wechsel, den kein Ziel wiederholt, gilt als Windows (auf windows-2025 startet
         Windows VSS und swprv 11 bis 13 Minuten nach dem Boot selbst).
    Eine geplante Aufgabe, die sich aendert, gilt nur als Windows, wenn die Aenderung in $knownTaskChanges steht und das
    Sicherheitsprotokoll (4698-4702) sie dem dort genannten Programm ausserhalb aller Laeufe zuschreibt (PrintJobCleanup-
    Task schaltet sich nach ihrem Lauf selbst ab, 4701 von taskhostw.exe); sonst scheitert der Nachweis.
    Jede andere Abweichung einer Einstellung (in jeder Phase, auch waehrend der Wiederholungen) laesst den Nachweis
    scheitern (Exitcode 1). Der Text geht nach -Out (UTF-8, LF). Regeln und Belege: docs/collector.md, Abschnitt
    "How the proof tells the collector from Windows".

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

# Aenderungen geplanter Aufgaben, die Windows selbst vornimmt (Belege in docs/collector.md): Aufgabe, Art der Aenderung,
# Ereignis-ID und das Programm, das sie laut Sicherheitsprotokoll vornimmt. Nur diese werden entschuldigt.
$knownTaskChanges = @(
    # Server 2025: die Aufgabe schaltet sich nach ihrem ersten Lauf selbst ab (4701 vom Aufgabenhost taskhostw.exe)
    [pscustomobject]@{ Task = '\Microsoft\Windows\Printing\PrintJobCleanupTask'; Key = 'State'; Before = 'enabled'; After = 'disabled'; Event = 4701; Image = 'taskhostw.exe' }
)

# Programm einer PID zum Zeitpunkt $at: das juengste Ereignis 4688 mit dieser PID davor (PIDs werden wiederverwendet);
# $null ohne Ereignis (Prozessueberwachung aus oder Prozess aelter als zwei Stunden)
function Get-ImageAt([int] $id, [datetime] $at) {
    try { $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4688; StartTime = $at.AddHours(-2); EndTime = $at } -ErrorAction Stop) }
    catch { return $null }
    # Get-WinEvent liefert die neuesten Ereignisse zuerst
    foreach ($e in $events) {
        $d = @{}
        foreach ($n in ([xml] $e.ToXml()).Event.EventData.Data) { $d[$n.Name] = $n.InnerText }
        try { if ([Convert]::ToInt32($d['NewProcessId'], 16) -eq $id) { return [IO.Path]::GetFileName([string] $d['NewProcessName']) } } catch { }
    }
    return $null
}

# Wer hat eine geplante Aufgabe geaendert? Sicherheitsprotokoll 4698 (angelegt), 4699 (geloescht), 4700 (aktiviert),
# 4701 (deaktiviert), 4702 (geaendert) mit ClientProcessId und ParentProcessId (Windows 10 1809 / Server 2019 und neuer,
# wenn "Andere Objektzugriffsereignisse" ueberwacht werden); 0 = nicht protokolliert.
function Get-TaskChangers([string] $taskPath, [datetime] $from, [datetime] $to) {
    try { $events = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4698, 4699, 4700, 4701, 4702; StartTime = $from; EndTime = $to } -ErrorAction Stop) }
    catch { return @() }
    foreach ($e in $events) {
        $d = @{}
        foreach ($n in ([xml] $e.ToXml()).Event.EventData.Data) { $d[$n.Name] = $n.InnerText }
        if ([string] $d['TaskName'] -ne $taskPath) { continue }
        $client = 0
        [void] [int]::TryParse([string] $d['ClientProcessId'], [ref] $client)
        $parent = 0
        [void] [int]::TryParse([string] $d['ParentProcessId'], [ref] $parent)
        $image = if ($client) { Get-ImageAt $client $e.TimeCreated } else { $null }
        $who = if ($image) { "$image ($client)" } elseif ($client) { "process $client (no event 4688)" } else { 'no ClientProcessId' }
        [pscustomobject]@{ Id = $e.Id; Pid = $client; Parent = $parent; Image = $image; Label = "event $($e.Id) by $who" }
    }
}

# Aenderung einer geplanten Aufgabe ($key task:<Pfad>[:State], Werte vorher/nachher) im Fenster: ByWindows nur, wenn jedes
# Ereignis dazu in $knownTaskChanges steht und weder der Client- noch dessen Elternprozess zu einem Lauf gehoert.
# Text nennt die Ereignisse und Programme (leer: nichts protokolliert - dann zaehlt die Aenderung).
function Get-TaskChange([string] $key, [string] $before, [string] $after, [datetime] $from, [datetime] $to, $trees) {
    $path = $key.Substring(5) -replace ':State$', ''
    $kind = if ($key -like '*:State') { 'State' } else { 'present' }
    $changers = @(Get-TaskChangers $path $from $to)
    $byWindows = $changers.Count -gt 0
    foreach ($c in $changers) {
        $known = @($knownTaskChanges | Where-Object { $_.Task -eq $path -and $_.Key -eq $kind -and $_.Before -eq $before -and $_.After -eq $after -and
                                                      $_.Event -eq $c.Id -and $c.Image -and $_.Image -eq $c.Image })
        if ($c.Pid -eq 0 -or $known.Count -eq 0) { $byWindows = $false }
        foreach ($t in $trees) { if ($t.Contains([int] $c.Pid) -or ($c.Parent -and $t.Contains([int] $c.Parent))) { $byWindows = $false } }
    }
    [pscustomobject]@{ ByWindows = $byWindows; Text = (@($changers | ForEach-Object Label | Sort-Object -Unique) -join ', ') }
}

# Beurteilt eine geaenderte Aufgabe: $true, wenn sie als Aenderung von Windows aufgefuehrt wurde; sonst nennt sie die
# Ereignisse und der Aufrufer zaehlt sie als Fehler
function Test-TaskChangeByWindows([string] $key, $before, $after, [datetime] $from, [datetime] $to, $trees, [string] $label) {
    $change = Get-TaskChange $key ([string] $before[$key]) ([string] $after[$key]) $from $to $trees
    if ($change.ByWindows) { Line "  scheduled task changed by Windows (Security log: $($change.Text); a known change, not a process of the runs): $label$(Describe $before $after $key)"; return $true }
    Line "  scheduled task changed; Security log: $(if ($change.Text) { "$($change.Text) - not a known change by Windows, or a process of the runs" } else { 'no event 4698-4702 for it' })"
    return $false
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

# Regeln fuer den Laufzustand eines Dienstes (nicht wuauserv): Text, wenn Windows den Wechsel nach seiner Bauart selbst
# vornimmt (aufgefuehrt, nicht gezaehlt), sonst $null - dann entscheidet die Wiederholung
function Get-ServiceRule([string] $key, $before, $after) {
    $name = $key.Split(':')[1]
    $modeKey = "svc:$($name):StartMode"
    if ($before[$modeKey] -ne $after[$modeKey]) { return $null }
    # Starttrigger (sc qtriggerinfo): Windows startet den Dienst bei Bedarf, z. B. gpsvc, wenn gpresult ihn anspricht
    if (Test-TriggerStart $name) { return 'on-demand start/stop by Windows (service has a start trigger, StartMode unchanged)' }
    # Wartungsdienste von Windows Update und Windows Installer: Windows startet und beendet sie nach eigenem Zeitplan
    if ($name -in 'WaaSMedicSvc', 'UsoSvc', 'TrustedInstaller') { return 'Windows servicing service changed state on its own (StartMode unchanged; listed, not counted)' }
    # Bedarfsdienst, den Windows im Leerlauf beendet
    if ([string] $before[$key] -eq 'Running' -and [string] $after[$key] -eq 'Stopped' -and [string] $after[$modeKey] -eq 'Manual') {
        return 'demand-start service ended by Windows when idle (StartMode Manual, unchanged)'
    }
    return $null
}
function Get-ServiceState([string] $key) {
    $svc = Get-CimInstance Win32_Service -Filter "Name='$($key.Split(':')[1])'" -Property State -ErrorAction SilentlyContinue
    if ($null -eq $svc) { return 'absent' }
    return [string] $svc.State
}

# 1. Aufwaermen
$longest = 0
$warmFrom = Get-Date
$warmTrees = [Collections.Generic.List[object]]::new()
$allTrees = [Collections.Generic.List[object]]::new() # Prozessbaeume aller Laeufe (Zuordnung von Aufgabenaenderungen)
foreach ($t in $Target) {
    $run = Invoke-Traced $t (Join-Path $work "warmup-$t")
    $logged = Get-ChildrenFromSecurityLog $run.Start $run.End $run.Pid
    foreach ($tree in $run.Tree, $logged.Pids) { $warmTrees.Add($tree); $allTrees.Add($tree) }
    $longest = [Math]::Max($longest, $run.Ms)
}
$s1 = Get-RunnerState
$warmTo = Get-Date
# Dienstwechsel, die die Wiederholung (4.) prueft: Target, Key, Text, Before, After
$recheck = [Collections.Generic.List[object]]::new()
Line '== Warm-up (first run of every target)'
foreach ($key in @(Compare-State $s0 $s1)) {
    $text = Describe $s0 $s1 $key
    if ($key -like 'task:*' -and (Test-TaskChangeByWindows $key $s0 $s1 $warmFrom $warmTo $warmTrees '')) { continue }
    if (-not (Test-Runtime $key)) { Fail "configuration changed during warm-up: $text"; continue }
    if ($key -eq 'svc:wuauserv:State') { Line "  note (V1, see the measured runs): $text"; continue }
    $rule = Get-ServiceRule $key $s0 $s1
    if ($rule) { Line "  $rule`: $text"; continue }
    # Ein Wechsel beim Aufwaermen ist im gemessenen Lauf unsichtbar, solange er anhaelt: die Wiederholung prueft ihn
    Line "  service state changed (judged by repetition below): $text"
    $recheck.Add([pscustomobject]@{ Target = 'warm-up'; Key = $key; Text = $text; Before = [string] $s0[$key]; After = [string] $s1[$key] })
}
Line ''

# 2. Kalibrierung: Einstellungen, die sich von selbst aendern, werden ausgeschlossen (mit Warnung). Ein Dienst, der sich
# hier aendert, kann ebenso eine spaete Folge des Aufwaermens sein: ohne Regel prueft ihn die Wiederholung
$pause = [Math]::Max(30000, 2 * $longest)
$calStart = Get-Date
Start-Sleep -Milliseconds $pause
$calEnd = Get-Date
$s2 = Get-RunnerState
$excluded = @(Compare-State $s1 $s2)
Line "== Calibration (null run, $([Math]::Round($pause / 1000)) s without the collector)"
if ($excluded.Count -eq 0) { Line '  nothing changed on its own' }
foreach ($key in $excluded) {
    $text = Describe $s1 $s2 $key
    if (-not (Test-Runtime $key)) { Line "  excluded (changes on its own): $text"; Line "  WARNING: a configuration value changes on its own on this runner: $key"; continue }
    if ($key -eq 'svc:wuauserv:State') { Line "  changed on its own (V1, see below): $text"; continue }
    $rule = Get-ServiceRule $key $s1 $s2
    if ($rule) { Line "  $rule`: $text"; continue }
    Line "  service state changed without a target (judged by repetition below): $text"
    $recheck.Add([pscustomobject]@{ Target = 'calibration'; Key = $key; Text = $text; Before = [string] $s1[$key]; After = [string] $s2[$key] })
}
Line "  Security log event IDs without the collector (background): $(Get-SecurityEventIds $calStart $calEnd)"
Line ''

# 3. Gemessene Laeufe
$previous = $s2
$previousFrom = $calEnd
$wuChanged = $false
$measured = [Collections.Generic.List[object]]::new()
foreach ($t in $Target) {
    $run = Invoke-Traced $t (Join-Path $work "run-$t")
    $start = $run.Start; $end = $run.End; $children = $run.Children
    $logged = Get-ChildrenFromSecurityLog $start $end $run.Pid
    $allTrees.Add($run.Tree)
    $allTrees.Add($logged.Pids)
    $afterFrom = Get-Date
    $after = Get-RunnerState
    $afterTo = Get-Date
    # Ausgeschlossen bleiben nur Einstellungen aus der Kalibrierung und wuauserv, wenn er sich dort von selbst aenderte (V1)
    $changes = @(Compare-State $previous $after | Where-Object { $_ -notin $excluded -or ((Test-Runtime $_) -and $_ -ne 'svc:wuauserv:State') })
    foreach ($key in $changes) {
        $measured.Add([pscustomobject]@{ Target = $t; Key = $key; Text = (Describe $previous $after $key); Before = [string] $previous[$key]; After = [string] $after[$key]
                                         BeforeState = $previous; AfterState = $after; From = $previousFrom; To = $afterTo })
    }
    Line "== Measured run: $t (exit code $($run.ExitCode), $($run.Ms) ms)"
    if ($run.ExitCode -notin 0, 1) { Fail "$t exited with $($run.ExitCode)" }
    if (-not (Test-OutputFolder $t $run.Out)) { Fail "$t wrote something else than its export and checksum: $((Get-ChildItem $run.Out -Force | ForEach-Object Name) -join ', ')" }
    else { Line '  written: only the export and its .sha256 (builtin: only its bundle folder)' }
    if ($changes.Count -eq 0) { Line '  no difference' }
    else { foreach ($key in $changes) { Line "  difference (judged below): $(Describe $previous $after $key)" } }
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

# Zweite Kalibrierung nach den Laeufen: wie die erste; ein Dienstwechsel ohne Regel kann eine spaete Folge des letzten
# Laufs sein und geht in die Wiederholung
$cal2Start = Get-Date
Start-Sleep -Milliseconds $pause
$cal2End = Get-Date
$s9 = Get-RunnerState
$excluded2 = @(Compare-State $previous $s9)
Line "== Second calibration (null run, $([Math]::Round($pause / 1000)) s after the measured runs)"
if ($excluded2.Count -eq 0) { Line '  nothing changed on its own' }
foreach ($key in $excluded2) {
    $text = Describe $previous $s9 $key
    if (-not (Test-Runtime $key)) { Line "  changed on its own: $text"; Line "  WARNING: a configuration value changes on its own on this runner: $key"; continue }
    if ($key -eq 'svc:wuauserv:State') { Line "  changed on its own (V1, see below): $text"; continue }
    $rule = Get-ServiceRule $key $previous $s9
    if ($rule) { Line "  $rule`: $text"; continue }
    Line "  service state changed without a target (judged by repetition below): $text"
    $recheck.Add([pscustomobject]@{ Target = 'second calibration'; Key = $key; Text = $text; Before = [string] $previous[$key]; After = [string] $s9[$key] })
}
Line "  Security log event IDs without the collector (background): $(Get-SecurityEventIds $cal2Start $cal2End)"
Line ''
Line '== Differences of the measured runs'
foreach ($m in $measured) {
    # Geplante Aufgabe: nur eine bekannte Aenderung durch Windows, belegt im Sicherheitsprotokoll, wird entschuldigt
    if ($m.Key -like 'task:*' -and (Test-TaskChangeByWindows $m.Key $m.BeforeState $m.AfterState $m.From $m.To $allTrees "$($m.Target): ")) { continue }
    if (-not (Test-Runtime $m.Key)) { Fail "$($m.Target) changed: $($m.Text)"; continue }
    # wuauserv bleibt streng (V1); aendert er sich auch in der zweiten Kalibrierung von selbst, ist V1 hier nicht pruefbar
    if ($m.Key -eq 'svc:wuauserv:State') {
        if ($m.Key -in $excluded2) { Line "  wuauserv changed on its own in the second calibration as well (V1 not verifiable): $($m.Target): $($m.Text)" }
        else { $wuChanged = $true; Fail "$($m.Target) changed: $($m.Text)" }
        continue
    }
    $rule = Get-ServiceRule $m.Key $m.BeforeState $m.AfterState
    if ($rule) { Line "  $rule`: $($m.Target): $($m.Text)"; continue }
    Line "  service state changed (judged by repetition below): $($m.Target): $($m.Text)"
    $recheck.Add($m)
}
if ($measured.Count -eq 0) { Line '  none' }
Line ''

# 4. Wiederholung. Ein Wechsel, den ein Ziel verursacht, kommt bei jedem Lauf wieder. Erst wenn jeder gepruefte Dienst
# wieder im alten Zustand ist (VSS endet nach drei Minuten Leerlauf, swprv drei Minuten danach), laeuft jedes Ziel noch
# einmal; danach wird der Dienst 20 s lang beobachtet (laenger, als der gemessene Lauf bis zum Lesen der Dienste braucht).
# Jede Abweichung vom alten Zustand ist ein Fehler, auch eine spaetere: Ist der Dienst vor dem naechsten Lauf nicht
# mehr im alten Zustand, zaehlt der vorige Lauf nicht. Erst drei saubere Laeufe jedes Ziels machen den Wechsel zu
# Hintergrund; ein Dienst, der nicht zurueckkehrt, bleibt ein Fehler.
if ($recheck.Count -gt 0) {
    Line '== Repetition of service state changes (every target 3 times, each run with the service in its previous state)'
    $repFrom = $cal2End # ab dem Lesen von $s9
    $checks = @($recheck | Group-Object Key | ForEach-Object { $_.Group[0] })
    $valid = @{}
    $failed = @{}
    $lastClean = @{}
    foreach ($c in $checks) { foreach ($t in $Target) { $valid["$($c.Key)|$t"] = 0 } }
    function Test-Done($c) { $failed.ContainsKey($c.Key) -or -not ($Target | Where-Object { $valid["$($c.Key)|$_"] -lt 3 }) }
    # Ist der Dienst nicht mehr im alten Zustand, zaehlt der letzte saubere Lauf nicht (spaete Aenderung)
    function Undo-LateChange($c, [string] $state) {
        if ($state -ne $c.Before -and $lastClean.ContainsKey($c.Key)) {
            $valid["$($c.Key)|$($lastClean[$c.Key])"]--
            Line "    $($c.Key.Split(':')[1]) was '$state' before the next run: the previous run of $($lastClean[$c.Key]) is not counted"
            $lastClean.Remove($c.Key)
        }
    }
    for ($round = 1; $round -le 5 -and @($checks | Where-Object { -not (Test-Done $_) }).Count -gt 0; $round++) {
        $pending = @($checks | Where-Object { -not (Test-Done $_) })
        foreach ($c in $pending) { Undo-LateChange $c (Get-ServiceState $c.Key) }
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
            $pre = @{}
            foreach ($c in $pending) { $pre[$c.Key] = Get-ServiceState $c.Key; Undo-LateChange $c $pre[$c.Key] }
            $run = Invoke-Traced $t (Join-Path $work "repeat$round-$t")
            $allTrees.Add($run.Tree)
            $deviation = @{}
            $watch = [Diagnostics.Stopwatch]::StartNew()
            do {
                foreach ($c in $pending) {
                    if (-not $deviation.ContainsKey($c.Key)) { $now = Get-ServiceState $c.Key; if ($now -ne $c.Before) { $deviation[$c.Key] = $now } }
                }
                Start-Sleep -Milliseconds 500
            } while ($watch.Elapsed.TotalSeconds -lt 20)
            if ($run.ExitCode -notin 0, 1) { Fail "$t exited with $($run.ExitCode) in repetition round $round" }
            if (-not (Test-OutputFolder $t $run.Out)) { Fail "$t wrote something else than its export and checksum in repetition round $round" }
            $notes = foreach ($c in $pending) {
                $name = $c.Key.Split(':')[1]
                if ($failed.ContainsKey($c.Key)) { "$name already failed"; continue }
                if ($pre[$c.Key] -ne $c.Before) { "$name was '$($pre[$c.Key])' before (not counted)"; continue }
                if ($deviation.ContainsKey($c.Key)) {
                    Fail "$t changed: $($c.Key): '$($c.Before)' -> '$($deviation[$c.Key])' (repeated in repetition round $round)"
                    $failed[$c.Key] = $true
                    "$name REPEATED ('$($deviation[$c.Key])')"
                    continue
                }
                $valid["$($c.Key)|$t"]++
                $lastClean[$c.Key] = $t
                "$name unchanged for 20 s"
            }
            Line "    $t (exit code $($run.ExitCode), $($run.Ms) ms): $(@($notes) -join '; ')"
        }
    }
    foreach ($c in $checks) { if (-not $failed.ContainsKey($c.Key)) { Undo-LateChange $c (Get-ServiceState $c.Key) } }
    foreach ($c in $checks) {
        if ($failed.ContainsKey($c.Key)) { continue }
        $short = @($Target | Where-Object { $valid["$($c.Key)|$_"] -lt 3 })
        if ($short.Count -eq 0) { Line "  not reproducible: no target repeated it in 3 runs each - a change by Windows (listed, not counted): $($c.Target): $($c.Text)" }
        else { Fail "$($c.Target) changed: $($c.Text) (could not be repeated three times for: $($short -join ', '))" }
    }
    # Die Wiederholungen selbst: Einstellungen, Aufgaben und %TEMP%\riegel-* wie bei jedem Lauf
    $s10 = Get-RunnerState
    $repTo = Get-Date
    foreach ($key in @(Compare-State $s9 $s10)) {
        if ($key -like 'task:*' -and (Test-TaskChangeByWindows $key $s9 $s10 $repFrom $repTo $allTrees 'repetitions: ')) { continue }
        if (-not (Test-Runtime $key)) { Fail "configuration changed during the repetitions: $(Describe $s9 $s10 $key)" }
        else { Line "  service state after the repetitions (listed; the repetitions judged the checked services): $(Describe $s9 $s10 $key)" }
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
