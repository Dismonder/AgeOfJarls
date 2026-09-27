<#
.SYNOPSIS
  Creates mods/<Name>/ from templates/ModTemplate and adds it to Valheim.slnx.
.EXAMPLE
  ./tools/new-mod.ps1 BetterPortals
  ./tools/new-mod.ps1 CustomSwords -Jotunn
#>
param(
    [Parameter(Mandatory, Position = 0)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]*$')][string]$Name,
    [switch]$Jotunn
)
. "$PSScriptRoot\_common.ps1"

$src = Join-Path $RepoRoot 'templates\ModTemplate'
$dst = Join-Path $RepoRoot "mods\$Name"
if (Test-Path $dst) { throw "Juz istnieje: $dst" }

Copy-Item $src $dst -Recurse
Rename-Item (Join-Path $dst 'ModTemplate.csproj') "$Name.csproj"

Get-ChildItem $dst -Recurse -File | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName)
    $text = $text.Replace('ModTemplate', $Name).Replace('__USE_JOTUNN__', $Jotunn.IsPresent.ToString().ToLower())
    [IO.File]::WriteAllText($_.FullName, $text)
}

if ($Jotunn) {
    $jotunnVersion = (Get-Item (Join-Path $PluginsDir 'Jotunn\Jotunn.dll')).VersionInfo.FileVersion -replace '^(\d+\.\d+\.\d+).*', '$1'
    $manifestPath = Join-Path $dst 'Package\manifest.json'
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifest.dependencies += "ValheimModding-Jotunn-$jotunnVersion"
    $manifest | ConvertTo-Json -Depth 5 | Set-Content $manifestPath -Encoding utf8NoBOM
}

# Placeholder 256x256 icon required by Thunderstore; replace it with a real one before publishing.
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap 256, 256
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
$g.Clear([System.Drawing.Color]::FromArgb(38, 50, 56))
$font = New-Object System.Drawing.Font 'Segoe UI', 96, ([System.Drawing.FontStyle]::Bold)
$fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
$initials = (($Name -creplace '([A-Z])', ' $1').Trim().Split(' ', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_[0] }) -join ''
$g.DrawString($initials.Substring(0, [Math]::Min(2, $initials.Length)).ToUpper(), $font, [System.Drawing.Brushes]::Goldenrod, (New-Object System.Drawing.RectangleF 0, 0, 256, 256), $fmt)
$bmp.Save((Join-Path $dst 'Package\icon.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

$sln = Join-Path $RepoRoot 'Valheim.slnx'
if (-not (Test-Path $sln)) { dotnet new sln -n Valheim -o $RepoRoot --format slnx | Out-Null }
dotnet sln $sln add (Join-Path $dst "$Name.csproj") | Out-Null

Write-Host "Utworzono $dst (Jotunn: $($Jotunn.IsPresent))"
