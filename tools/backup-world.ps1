<#
.SYNOPSIS
  Copies a Valheim world to _backups\<world>-<yyyyMMdd-HHmm> before a test session.
  Finds it in the Steam cloud saves (userdata\<id>\892970\remote\worlds) and in the local saves (worlds_local),
  in the folder format of Valheim 1.0 and in the old <world>.db/.fwl files. Refuses while the game runs: the save
  may be half written. To restore, use the game's own "Manage saves" menu, or put the copied folder back with
  Steam closed.
.EXAMPLE
  ./tools/backup-world.ps1 testo     # copy the world "testo"
  ./tools/backup-world.ps1 -List     # list the copies
#>
param(
    [string]$World,
    [switch]$List
)
. "$PSScriptRoot\_common.ps1"

$BackupDir = Join-Path $RepoRoot '_backups'

if ($List) {
    if (-not (Test-Path $BackupDir)) { Write-Host 'Brak kopii.'; return }
    Get-ChildItem $BackupDir -Directory | Sort-Object Name | ForEach-Object {
        $size = (Get-ChildItem $_.FullName -Recurse -File | Measure-Object Length -Sum).Sum
        '{0,-45} {1,12:N0} B' -f $_.Name, $size
    }
    return
}
if (-not $World) { throw 'Podaj nazwe swiata, np. ./tools/backup-world.ps1 testo' }
if (Get-Process valheim -ErrorAction SilentlyContinue) { throw 'Gra jest uruchomiona - zapis swiata moze byc w polowie. Zamknij gre i powtorz.' }

$roots = @()
$steamUsers = Join-Path ${env:ProgramFiles(x86)} 'Steam\userdata'
if (Test-Path $steamUsers) {
    $roots += Get-ChildItem $steamUsers -Directory | ForEach-Object { Join-Path $_.FullName '892970\remote\worlds' }
}
$roots += Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'

$stamp = Get-Date -Format 'yyyyMMdd-HHmm'
$copied = 0
foreach ($root in $roots | Where-Object { Test-Path $_ }) {
    $kind = if ($root -like '*remote*') { 'cloud' } else { 'local' }
    $target = Join-Path $BackupDir "$World-$kind-$stamp"
    $folder = Join-Path $root $World
    $files = @(Get-ChildItem $root -File -Filter "$World.*" -ErrorAction SilentlyContinue)
    if (Test-Path $folder -PathType Container) {
        # Valheim 1.0: one folder per world.
        New-Item -ItemType Directory -Force $target | Out-Null
        Copy-Item (Join-Path $folder '*') $target -Recurse
    }
    elseif ($files.Count -gt 0) {
        # Older saves: <world>.db, <world>.fwl and their .old copies.
        New-Item -ItemType Directory -Force $target | Out-Null
        $files | Copy-Item -Destination $target
    }
    else {
        continue
    }
    $size = (Get-ChildItem $target -Recurse -File | Measure-Object Length -Sum).Sum
    Write-Host ('Kopia ({0}): {1}  ({2:N0} B)' -f $kind, $target, $size)
    $copied++
}
if ($copied -eq 0) { throw "Nie znaleziono swiata '$World' w zapisach Steam ani lokalnych." }
