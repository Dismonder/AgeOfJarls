<#
.SYNOPSIS
  Prints a short, filtered view of BepInEx\LogOutput.log instead of the whole file.
  Default: warnings, errors and exceptions (with the stack trace lines that follow them).
.EXAMPLE
  ./tools/log.ps1                    # errors/warnings, last 40 hits
  ./tools/log.ps1 -Mod BetterPortals # everything logged by that mod + all errors
  ./tools/log.ps1 -All -Last 80      # raw tail of the log
#>
param(
    [string]$Mod,
    [int]$Last = 40,
    [switch]$All
)
. "$PSScriptRoot\_common.ps1"

if (-not (Test-Path $LogFile)) { throw "Brak logu: $LogFile (gra jeszcze nie byla uruchomiona z BepInEx?)" }
$lines = Get-Content $LogFile

if ($All) { $lines | Select-Object -Last $Last; return }

$pattern = '^\[(Error|Fatal|Warning)|Exception|^\s+at |^\s+Stack trace'
if ($Mod) { $pattern = "$pattern|$([regex]::Escape($Mod))" }

$hits = $lines | Select-String -Pattern $pattern
Write-Host "Log: $((Get-Item $LogFile).LastWriteTime)  |  trafien: $($hits.Count)  |  pokazuje ostatnie $Last"
$hits | Select-Object -Last $Last | ForEach-Object { "{0,6}: {1}" -f $_.LineNumber, $_.Line }
