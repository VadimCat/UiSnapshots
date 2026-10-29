<#
.SYNOPSIS
  Runs UI Snapshots scenarios in a task-owned Unity Editor through the Unity CLI and waits for the result.
.EXAMPLE
  ui-snap.ps1 -Project D:\Codex\Game\worktrees\task -Scenario popups,hud -Viewports phones -Label after -Compare latest:before
  ui-snap.ps1 -Project <path> -List
  ui-snap.ps1 -Project <path> -CompareRuns <beforeRunDir>,<afterRunDir>
  ui-snap.ps1 -Project <path> -Approve <runDir> [-Shots "popups/*@iphone-*"]      # promote frames to approved references
  ui-snap.ps1 -Project <path> -AddReference mockup.png -Scenario popups -Capture pause [-Viewport iphone] [-Skin Original]
.NOTES
  Exit codes: 0 all shots taken and clean; 2 finished with warnings (unsettled frames, failed expects, step warnings,
  frames that differ from an approved reference);
  1 failed, cancelled or CLI error; 3 timed out while waiting (the run continues in the Editor; use -Status / -Cancel).
#>
param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string[]]$Scenario = @(),
    [string[]]$Viewports = @(),
    [string[]]$Skins = @(),
    [string]$Label = '',
    [string]$Output = '',
    [string]$Compare = '',
    [string[]]$CompareRuns = @(),
    [string]$Approve = '',
    [string]$Shots = '*',
    [string]$AddReference = '',
    [string]$Capture = '',
    [string]$Viewport = '',
    [string]$Skin = '',
    [int]$TimeoutSec = 1800,
    [switch]$List,
    [switch]$Validate,
    [switch]$Status,
    [switch]$Cancel,
    [switch]$NoWait
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
$env:UNITY_NO_CRASH_REPORT = '1'; $env:UNITY_NO_CLI_INVOKED_TELEMETRY = '1'; $env:UNITY_NO_CONSENT_PROMPT = '1'
$Project = (Resolve-Path $Project).Path
$unity = Join-Path $env:LOCALAPPDATA 'Unity\bin\unity.exe'
if (-not (Test-Path $unity)) { $unity = (Get-Command unity -ErrorAction Stop).Source }
$shim = Join-Path $PSScriptRoot 'UiSnapCliEntry.cs'

function Split-List([string[]]$values) { @($values | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }

function Invoke-Entry([string]$entry, [object[]]$arguments = @(), [int]$retries = 1) {
    $argsJson = (ConvertTo-Json -InputObject @($arguments) -Compress -Depth 6)
    if ($arguments.Count -eq 0) { $argsJson = '[]' }
    $quoted = $argsJson.Replace('"', '\"')
    for ($i = 0; $i -lt [Math]::Max(1, $retries); $i++) {
        $raw = & $unity command --project-path $Project --timeout 120 run_script --file $shim --entry "UiSnapCliEntry.$entry" --args $quoted --format json 2>&1 | Out-String
        try {
            $j = $raw | ConvertFrom-Json
            if ($j.success -and $j.data.result.success -ne $false) { return ($j.data.result.result | ConvertFrom-Json) }
        } catch { }
        if ($i -lt $retries - 1) { Start-Sleep -Seconds 3 }
    }
    throw "Unity CLI call $entry failed: $raw"
}

if ($List) { Invoke-Entry 'List' | ConvertTo-Json -Depth 6; exit 0 }
if ($Status) { Invoke-Entry 'Status' | ConvertTo-Json -Depth 4; exit 0 }
if ($Cancel) { Invoke-Entry 'Cancel' | ConvertTo-Json; exit 0 }
if ($Validate) {
    $r = Invoke-Entry 'Validate' @((Split-List $Scenario) -join ',')
    $r | ConvertTo-Json -Depth 5
    if ($r.ok) { exit 0 } else { exit 1 }
}
if ($CompareRuns.Count -gt 0) {
    $runs = Split-List $CompareRuns
    if ($runs.Count -ne 2) { throw '-CompareRuns needs <before>,<after>' }
    $r = Invoke-Entry 'Compare' @((Resolve-Path $runs[0]).Path.Replace('\','/'), (Resolve-Path $runs[1]).Path.Replace('\','/'))
    $r | ConvertTo-Json -Depth 4
    if ($r.ok) { exit 0 } else { exit 1 }
}

if ($Approve) {
    $r = Invoke-Entry 'Approve' @((Resolve-Path $Approve).Path.Replace('\','/'), $Shots)
    $r | ConvertTo-Json -Depth 4
    if ($r.ok) { exit 0 } else { exit 1 }
}
if ($AddReference) {
    $names = Split-List $Scenario
    if ($names.Count -ne 1 -or -not $Capture) { throw '-AddReference needs exactly one -Scenario and -Capture.' }
    $r = Invoke-Entry 'AddReference' @((Resolve-Path $AddReference).Path.Replace('\','/'), $names[0], $Capture, $Viewport, $Skin)
    $r | ConvertTo-Json -Depth 4
    if ($r.ok) { exit 0 } else { exit 1 }
}

$request = [ordered]@{
    scenarios = @(Split-List $Scenario)
    viewports = @(Split-List $Viewports)
    skins     = @(Split-List $Skins)
    label     = $Label
    output    = $Output
    compare   = $Compare
}
if ($request.scenarios.Count -eq 0) { throw 'Pass -Scenario (or -List to see what exists).' }
# The request travels as a file: nested JSON does not survive Windows PowerShell native argument quoting.
$requestDir = Join-Path $Project 'Temp\UiSnapshots'
New-Item -ItemType Directory -Force -Path $requestDir | Out-Null
$requestFile = Join-Path $requestDir 'request.json'
[IO.File]::WriteAllText($requestFile, ($request | ConvertTo-Json -Depth 4), (New-Object Text.UTF8Encoding $false))
$start = Invoke-Entry 'Start' @('Temp/UiSnapshots/request.json')
if (-not $start.ok) { Write-Output "start failed: $($start.error)"; exit 1 }
Write-Output "run $($start.runId): $($start.passes) pass(es) -> $($start.output)"
if ($NoWait) { exit 0 }

$deadline = (Get-Date).AddSeconds($TimeoutSec)
$last = ''
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 3
    try { $s = Invoke-Entry 'Status' @() 3 } catch { continue } # domain reloads make single calls fail
    if ($s.Progress -and $s.Progress -ne $last) { Write-Output ("[{0}] {1}" -f $s.Phase, $s.Progress); $last = $s.Progress }
    if ($s.Phase -in @('done', 'failed', 'cancelled')) {
        Write-Output "report: $($s.Report)"
        if ($s.CompareSummary) { Write-Output "compare: $($s.CompareSummary)" }
        if ($s.Error) { Write-Output "error: $($s.Error)" }
        if ($s.Phase -ne 'done' -or $s.Errors -gt 0) { exit 1 }
        if ($s.Warnings -gt 0 -or $s.Unsettled -gt 0 -or $s.FailedExpectations -gt 0 -or $s.ReferenceMismatches -gt 0) { exit 2 }
        exit 0
    }
}
Write-Output "timed out after $TimeoutSec s; the run continues in the Editor (use -Status or -Cancel)."
exit 3
