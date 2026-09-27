# Shared paths for tool scripts. Override the game path with $env:VALHEIM_DIR.
$ErrorActionPreference = 'Stop'
$RepoRoot   = Split-Path -Parent $PSScriptRoot
$ValheimDir = if ($env:VALHEIM_DIR) { $env:VALHEIM_DIR } else { 'C:\Program Files (x86)\Steam\steamapps\common\Valheim' }
$ManagedDir = Join-Path $ValheimDir 'valheim_Data\Managed'
$BepInExDir = Join-Path $ValheimDir 'BepInEx'
$PluginsDir = Join-Path $BepInExDir 'plugins'
$LogFile    = Join-Path $BepInExDir 'LogOutput.log'
$RefDir     = Join-Path $RepoRoot '_ref'
