<#
.SYNOPSIS
  Builds mods/<Name> in Release and creates a Thunderstore-ready zip in dist/.
  The version comes from <Version> in the .csproj and is written into Package\manifest.json.
  Does not upload anything.
.EXAMPLE
  ./tools/package.ps1 BetterPortals
#>
param([Parameter(Mandatory, Position = 0)][string]$Name)
. "$PSScriptRoot\_common.ps1"

$modDir  = Join-Path $RepoRoot "mods\$Name"
$csproj  = Join-Path $modDir "$Name.csproj"
$pkgDir  = Join-Path $modDir 'Package'
if (-not (Test-Path $csproj)) { throw "Brak projektu: $csproj" }

$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version w .csproj musi byc w formacie x.y.z (jest: '$version')" }

dotnet build $csproj -c Release -p:DeployToGame=false -v q -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build nieudany' }

# Optional preloader patcher shipped with the mod: mods/<Name>.Patcher -> patchers/<Name>/ in the zip.
$patcherDir = Join-Path $RepoRoot "mods\$Name.Patcher"
$patcherProj = Join-Path $patcherDir "$Name.Patcher.csproj"
if (Test-Path $patcherProj) {
    dotnet build $patcherProj -c Release -p:DeployToGame=false -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build patchera nieudany' }
}

$manifestPath = Join-Path $pkgDir 'manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version_number = $version
$manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8NoBOM

# Thunderstore rules that cause an upload to be rejected.
$problems = @()
if ($manifest.name -notmatch '^[A-Za-z0-9_]+$') { $problems += 'manifest.name: tylko litery, cyfry i _' }
if ($manifest.description.Length -gt 250)      { $problems += 'manifest.description: max 250 znakow' }
$iconPath = Join-Path $pkgDir 'icon.png'
if (-not (Test-Path $iconPath)) { $problems += 'brak Package\icon.png' }
else {
    Add-Type -AssemblyName System.Drawing
    $img = [System.Drawing.Image]::FromFile($iconPath)
    if ($img.Width -ne 256 -or $img.Height -ne 256) { $problems += "icon.png musi miec 256x256 (ma $($img.Width)x$($img.Height))" }
    $img.Dispose()
}
if (-not (Test-Path (Join-Path $pkgDir 'README.md'))) { $problems += 'brak Package\README.md' }
if ($problems) { throw "Paczka niezgodna z Thunderstore:`n - " + ($problems -join "`n - ") }

$stage = Join-Path $RepoRoot "dist\$Name"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage "plugins\$Name") | Out-Null
Copy-Item (Join-Path $pkgDir '*') $stage -Recurse
Copy-Item (Join-Path $modDir 'bin\Release\*') (Join-Path $stage "plugins\$Name") -Recurse
if (Test-Path $patcherProj) {
    New-Item -ItemType Directory -Force (Join-Path $stage "patchers\$Name") | Out-Null
    Copy-Item (Join-Path $patcherDir 'bin\Release\*') (Join-Path $stage "patchers\$Name") -Recurse
}

$zip = Join-Path $RepoRoot "dist\$Name-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

Write-Host "Paczka: $zip"
