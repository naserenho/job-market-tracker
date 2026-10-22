<#
.SYNOPSIS
Publishes the archiver and processor and registers (or updates) the Windows scheduled task that runs
scripts/run-pipeline.cmd every Tuesday and Friday. Re-run this script after changing the code.
#>
[CmdletBinding()]
param(
    [string]$TaskName = 'JobMarketTracker Archiver',
    [DateTime]$At = '07:00'
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dataDir = Join-Path $root 'data'
$logFile = Join-Path $dataDir 'pipeline.log'

foreach ($app in 'Archiver', 'Processor') {
    $out = Join-Path $root ".artifacts\$($app.ToLowerInvariant())"
    dotnet publish (Join-Path $root "src\$app") -c Release -o $out --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $app failed with exit code $LASTEXITCODE" }
}
New-Item -ItemType Directory -Force $dataDir | Out-Null

$pipeline = Join-Path $root 'scripts\run-pipeline.cmd'
# cmd.exe appends all output to one log so failures before any manifest or report is written stay visible.
$action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"`"$pipeline`" >> `"$logFile`" 2>&1`"" -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Tuesday, Friday -At $At
# RunOnlyIfNetworkAvailable: after sleep, a missed run waits for the network instead of failing every request.
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RunOnlyIfNetworkAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Hours 3)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings `
    -Description 'Collects job-market snapshots and rebuilds the GTA job market report. See README.md.' -Force | Out-Null

Write-Host "Registered '$TaskName': Tuesday and Friday at $($At.ToString('HH:mm')), catching up after missed runs."
Write-Host "Runs: $pipeline (working directory $root)"
Write-Host "Log:  $logFile"
