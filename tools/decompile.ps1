<#
.SYNOPSIS
  Decompiles a game/plugin assembly once into _ref/decompiled/<name>/ (one .cs file per type),
  so the source can be searched with grep instead of being decompiled again each time.
  Skips the work when the DLL has not changed since the last run (stamp = size + mtime).
.EXAMPLE
  ./tools/decompile.ps1                                  # assembly_valheim (default)
  ./tools/decompile.ps1 assembly_utils
  ./tools/decompile.ps1 -Path "...\BepInEx\plugins\Jotunn\Jotunn.dll"
  ./tools/decompile.ps1 -Force
#>
param(
    [Parameter(Position = 0)][string]$Assembly = 'assembly_valheim',
    [string]$Path,
    [switch]$Force
)
. "$PSScriptRoot\_common.ps1"

$dll = if ($Path) { (Resolve-Path $Path).Path } else { Join-Path $ManagedDir "$Assembly.dll" }
if (-not (Test-Path $dll)) { throw "Nie znaleziono: $dll" }

$name  = [IO.Path]::GetFileNameWithoutExtension($dll)
$out   = Join-Path $RefDir "decompiled\$name"
$stamp = Join-Path $out '.stamp'
$info  = Get-Item $dll
$sig   = "$($info.Length)|$($info.LastWriteTimeUtc.Ticks)"

if (-not $Force -and (Test-Path $stamp) -and ((Get-Content $stamp -Raw).Trim() -eq $sig)) {
    Write-Host "Aktualne: $out (DLL bez zmian)"
    return
}

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null

Push-Location $RepoRoot
try {
    dotnet ilspycmd -p -o $out -r $ManagedDir -r (Join-Path $BepInExDir 'core') -r $info.DirectoryName $dll | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd zakonczyl sie kodem $LASTEXITCODE" }
} finally { Pop-Location }

Set-Content $stamp $sig
$count = (Get-ChildItem $out -Recurse -Filter *.cs).Count
Write-Host "Zdekompilowano $name -> $out ($count plikow .cs)"
