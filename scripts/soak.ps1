<#
.SYNOPSIS
  Soak test helper for Sway on Windows (mirrors scripts/soak.sh on the Mac): records Sway's CPU, memory, threads and
  handles while you use it all day, and Sway's own log lines, then summarizes both.

.DESCRIPTION
  scripts\soak.ps1 sample [-Out soak.csv] [-Interval 30]   run until Ctrl+C (default .\soak-logs\soak-<date>.csv)
  scripts\soak.ps1 report -Out soak.csv                     summary of a finished or running sample

  Sampling only reads process statistics and copies new lines of %LocalAppData%\Sway\Logs\sway.log into <out>.log
  (that file rotates at 1 MB, so a day of lines would be lost otherwise). It never touches the camera or Sway itself.
  A pid change in the middle of a run means Sway quit or crashed, and the report says so.
  Works in Windows PowerShell 5.1 and PowerShell 7.
#>
[CmdletBinding()]
param(
  [Parameter(Position = 0, Mandatory = $true)][ValidateSet('sample', 'report')][string]$Mode,
  [Parameter(Position = 1)][string]$Out,
  [int]$Interval = 30
)

$ErrorActionPreference = 'Stop'
$ProcessName = 'Sway'
$SwayLog = Join-Path $env:LOCALAPPDATA 'Sway\Logs\sway.log'
$Culture = [System.Globalization.CultureInfo]::InvariantCulture

function Get-PowerSource {
  try {
    Add-Type -AssemblyName System.Windows.Forms -ErrorAction Stop
    $line = [System.Windows.Forms.SystemInformation]::PowerStatus.PowerLineStatus
    if ($line -eq 'Online') { return 'ac' }
    if ($line -eq 'Offline') { return 'battery' }
  } catch { }
  return ''
}

# New lines of sway.log since the last call. $State.Pos is the byte offset already copied; a shorter file means it rotated.
function Copy-NewLogLines([hashtable]$State, [string]$Destination) {
  try {
    if (-not (Test-Path -LiteralPath $SwayLog)) { return }
    $length = (Get-Item -LiteralPath $SwayLog).Length
    $chunks = @()
    if ($length -lt $State.Pos) {
      $older = $SwayLog + '.1'
      if (Test-Path -LiteralPath $older) { $chunks += Read-FromOffset $older $State.Pos }
      $State.Pos = 0
    }
    $chunks += Read-FromOffset $SwayLog $State.Pos
    $State.Pos = $length
    $text = ($chunks -join '')
    if ($text.Length -gt 0) { [System.IO.File]::AppendAllText($Destination, $text, [System.Text.Encoding]::UTF8) }
  } catch { }
}

function Read-FromOffset([string]$Path, [long]$Offset) {
  $stream = New-Object System.IO.FileStream($Path, 'Open', 'Read', 'ReadWrite')
  try {
    if ($Offset -gt $stream.Length) { return '' }
    [void]$stream.Seek($Offset, 'Begin')
    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8)
    return $reader.ReadToEnd()
  } finally { $stream.Dispose() }
}

function Invoke-Sample {
  if (-not $Out) { $Out = Join-Path 'soak-logs' ('soak-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmm')) }
  $dir = Split-Path -Parent $Out
  if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
  'time,pid,cpu_percent,rss_mb,threads,handles,power' | Set-Content -LiteralPath $Out -Encoding ASCII
  $logFile = [System.IO.Path]::ChangeExtension($Out, 'log')
  Set-Content -LiteralPath $logFile -Value '' -NoNewline
  $state = @{ Pos = 0L }
  if (Test-Path -LiteralPath $SwayLog) { $state.Pos = (Get-Item -LiteralPath $SwayLog).Length }  # only lines from now on
  Write-Host "Sampling Sway every $Interval s into $Out and its log lines into $logFile (Ctrl+C to stop)."
  $cores = [Environment]::ProcessorCount
  while ($true) {
    $stamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'
    $process = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($process) {
      # Two readings one second apart: the CPU over that second, as a percentage of ONE core (like top on the Mac).
      $t1 = $process.TotalProcessorTime.TotalSeconds
      $clock = [System.Diagnostics.Stopwatch]::StartNew()
      Start-Sleep -Seconds 1
      $process.Refresh()
      $elapsed = $clock.Elapsed.TotalSeconds
      $cpu = [math]::Round((($process.TotalProcessorTime.TotalSeconds - $t1) / $elapsed) * 100, 1)
      $rss = [math]::Round($process.WorkingSet64 / 1MB, 1)
      $row = '{0},{1},{2},{3},{4},{5},{6}' -f $stamp, $process.Id, $cpu.ToString($Culture), $rss.ToString($Culture), $process.Threads.Count, $process.HandleCount, (Get-PowerSource)
      Add-Content -LiteralPath $Out -Value $row -Encoding ASCII
    } else {
      Add-Content -LiteralPath $Out -Value ('{0},0,0,0,0,0,' -f $stamp) -Encoding ASCII
    }
    Copy-NewLogLines $state $logFile
    Start-Sleep -Seconds $Interval
  }
}

function Get-Percentile([double[]]$Sorted, [double]$P) {
  if ($Sorted.Count -eq 0) { return 0 }
  $index = [math]::Ceiling($Sorted.Count * $P) - 1
  return $Sorted[[math]::Max(0, [math]::Min($Sorted.Count - 1, $index))]
}

function Invoke-Report {
  if (-not $Out) { throw 'usage: soak.ps1 report -Out <soak.csv>' }
  if (-not (Test-Path -LiteralPath $Out)) { Write-Error "No such file: $Out"; exit 2 }
  $rows = @(Import-Csv -LiteralPath $Out)
  if ($rows.Count -eq 0) { Write-Host 'No samples.'; return }
  $running = @($rows | Where-Object { $_.pid -ne '0' })
  $gaps = $rows.Count - $running.Count
  Write-Host "Soak report: $Out"
  Write-Host ("From {0} to {1}" -f $rows[0].time, $rows[-1].time)
  Write-Host ''
  if ($running.Count -eq 0) { Write-Host 'No samples with Sway running.'; return }

  $cpu = @($running | ForEach-Object { [double]::Parse($_.cpu_percent, $Culture) })
  $sorted = @($cpu | Sort-Object)
  $mean = ($cpu | Measure-Object -Average).Average
  $max = ($cpu | Measure-Object -Maximum).Maximum
  Write-Host ("Samples with Sway running: {0} ({1} with Sway not running)" -f $running.Count, $gaps)
  Write-Host ("CPU of one core: mean {0:F1}%, 95th percentile {1:F1}%, max {2:F1}%" -f $mean, (Get-Percentile $sorted 0.95), $max)
  foreach ($group in ($running | Group-Object power)) {
    $label = if ($group.Name) { $group.Name } else { 'unknown power' }
    $groupMean = ($group.Group | ForEach-Object { [double]::Parse($_.cpu_percent, $Culture) } | Measure-Object -Average).Average
    Write-Host ("  on {0}: mean {1:F1}% over {2} samples" -f $label, $groupMean, $group.Count)
  }

  $times = @($running | ForEach-Object { [datetime]::ParseExact($_.time, 'yyyy-MM-dd HH:mm:ss', $Culture) })
  $mem = @($running | ForEach-Object { [double]::Parse($_.rss_mb, $Culture) })
  $memMax = ($mem | Measure-Object -Maximum).Maximum
  Write-Host ("Memory (working set): first {0:F1} MB, last {1:F1} MB, max {2:F1} MB (change {3:+0.0;-0.0} MB)" -f $mem[0], $mem[-1], $memMax, ($mem[-1] - $mem[0]))
  Write-Host ("Threads at the end: {0}; handles at the end: {1} (first sample {2})" -f $running[-1].threads, $running[-1].handles, $running[0].handles)

  # Memory trend per hour: least-squares slope over the whole run, and the same per whole hour for a view of the shape.
  $hours = ($times[-1] - $times[0]).TotalHours
  if ($hours -gt 0.25 -and $mem.Count -ge 3) {
    $x = @($times | ForEach-Object { ($_ - $times[0]).TotalHours })
    $meanX = ($x | Measure-Object -Average).Average
    $meanY = ($mem | Measure-Object -Average).Average
    $num = 0.0; $den = 0.0
    for ($i = 0; $i -lt $x.Count; $i++) { $num += ($x[$i] - $meanX) * ($mem[$i] - $meanY); $den += [math]::Pow($x[$i] - $meanX, 2) }
    if ($den -gt 0) {
      Write-Host ("Memory trend: {0:+0.0;-0.0} MB per hour (a steady climb over hours is a leak; flat is fine)" -f ($num / $den))
    }
    $hourly = @($running | Group-Object { [int][math]::Floor(([datetime]::ParseExact($_.time, 'yyyy-MM-dd HH:mm:ss', $Culture) - $times[0]).TotalHours) })
    foreach ($bucket in $hourly) {
      $avg = ($bucket.Group | ForEach-Object { [double]::Parse($_.rss_mb, $Culture) } | Measure-Object -Average).Average
      Write-Host ("  hour {0}: mean working set {1:F1} MB" -f ([int]$bucket.Name + 1), $avg)
    }
  }

  $restarts = 0
  for ($i = 1; $i -lt $running.Count; $i++) { if ($running[$i].pid -ne $running[$i - 1].pid) { $restarts++ } }
  if ($restarts -gt 0) { Write-Host "WARNING: Sway restarted $restarts time(s) during the run (quit or crash)" }
  else { Write-Host 'Restarts: none (same process the whole run)' }

  Write-Host ''
  Write-Host "From Sway's log in that period:"
  $logFile = [System.IO.Path]::ChangeExtension($Out, 'log')
  if (-not (Test-Path -LiteralPath $logFile)) {
    Write-Host "  (no $logFile: start a run with 'scripts\soak.ps1 sample' to capture log lines)"
    return
  }
  $lines = @(Get-Content -LiteralPath $logFile | Where-Object { $_ })
  function Count-Lines([string]$Pattern) { @($lines | Where-Object { $_ -match $Pattern }).Count }
  Write-Host ("  switches focused:          {0}" -f (Count-Lines 'Focused .* on .* via'))
  Write-Host ("  display had no window:     {0}" -f (Count-Lines 'No window on'))
  Write-Host ("  switches that failed:      {0}" -f (Count-Lines 'Switch to .* failed'))
  Write-Host ("  repeat requests held back: {0}" -f (Count-Lines 'Not asking again for'))
  Write-Host ("  focus taken back:          {0}" -f (Count-Lines 'Focus left .* after Sway focused'))
  Write-Host ("  camera interrupted:        {0}" -f (Count-Lines 'Camera status: Interrupted'))
  Write-Host ("  camera failed:             {0}" -f (Count-Lines 'Camera status: Failed'))
  Write-Host ("  camera running again:      {0}" -f (Count-Lines 'Camera status: Running'))
  Write-Host ("  suspended (sleep/lock):    {0}" -f (Count-Lines 'Camera status: Suspended'))
  Write-Host ("  camera unplugged / back:   {0} / {1}" -f (Count-Lines 'chosen camera is unplugged'), (Count-Lines 'chosen camera is connected again'))
  Write-Host ("  display or desktop holds:  {0} / {1}" -f (Count-Lines 'Display hold started'), (Count-Lines 'Space hold started'))
  Write-Host ("  pose source failures:      {0}" -f (Count-Lines 'Pose source failed'))

  # Likely false switches: a switch to display B followed within 3 s by a switch back to display A.
  $switches = @($lines | Where-Object { $_ -match '^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d) Focused .* on (.+) via ' } | ForEach-Object {
      $null = $_ -match '^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d) Focused .* on (.+) via '
      [pscustomobject]@{ Time = [datetime]::ParseExact($Matches[1], 'yyyy-MM-dd HH:mm:ss', $Culture); Display = $Matches[2] }
    })
  $reversals = 0
  for ($i = 2; $i -lt $switches.Count; $i++) {
    if ($switches[$i].Display -eq $switches[$i - 2].Display -and $switches[$i].Display -ne $switches[$i - 1].Display -and ($switches[$i].Time - $switches[$i - 1].Time).TotalSeconds -le 3) { $reversals++ }
  }
  Write-Host ("  quick reversals (A to B to A within 3 s, a likely false switch): {0}" -f $reversals)
  Write-Host ''
  Write-Host 'Add your own notes: any switch you did not want, and the time it happened.'
}

switch ($Mode) {
  'sample' { Invoke-Sample }
  'report' { Invoke-Report }
}
