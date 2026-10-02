<#
.SYNOPSIS
  Publishes the mod's Thunderstore zip (dist/<Name>-<version>.zip, from tools/package.ps1) to Thunderstore with the
  official CLI (tcli, `dotnet tool install -g tcli`). Team, community and categories come from mods/<Name>/thunderstore.toml,
  or from thunderstore.toml in the repo root (Age of Jarls). The service-account token is read from a file outside the repository:
  %USERPROFILE%\.thunderstore-token (one line; create it at thunderstore.io -> Teams -> <team> -> Service Accounts).
.EXAMPLE
  ./tools/publish-thunderstore.ps1 AgeOfJarls            # publish the current version's zip
  ./tools/publish-thunderstore.ps1 AgeOfJarls -Package    # build the zip first
#>
param(
    [Parameter(Mandatory, Position = 0)][string]$Name,
    [switch]$Package,
    [string]$TokenFile = (Join-Path $env:USERPROFILE '.thunderstore-token')
)
. "$PSScriptRoot\_common.ps1"

if (-not (Test-Path $TokenFile)) { throw "Brak tokenu: $TokenFile (Thunderstore -> Teams -> zespol -> Service Accounts -> Add, zapisz token w tym pliku)" }
$token = (Get-Content $TokenFile -Raw).Trim()
if ($token.Length -lt 20) { throw "Token w $TokenFile wyglada na pusty" }
if (-not (Get-Command tcli -ErrorAction SilentlyContinue)) { throw 'Brak tcli: dotnet tool install -g tcli' }

if ($Package) { & "$PSScriptRoot\package.ps1" $Name }

$csproj  = Join-Path $RepoRoot "mods\$Name\$Name.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip     = Join-Path $RepoRoot "dist\$Name-$version.zip"
if (-not (Test-Path $zip)) { throw "Brak paczki: $zip (uruchom z -Package albo tools/package.ps1 $Name)" }

# A mod may carry its own mods/<Name>/thunderstore.toml; the root one belongs to Age of Jarls.
$toml = Join-Path $RepoRoot "mods\$Name\thunderstore.toml"
if (-not (Test-Path $toml)) { $toml = Join-Path $RepoRoot 'thunderstore.toml' }
if (-not (Test-Path $toml)) { throw "Brak $toml" }
# Keep the version in thunderstore.toml in step with the csproj (tcli reads the manifest from the zip, but the
# config must not disagree).
$text = Get-Content $toml -Raw
$text = [regex]::Replace($text, 'versionNumber = "[^"]*"', "versionNumber = `"$version`"")
Set-Content $toml $text -Encoding utf8NoBOM

Push-Location $RepoRoot
try {
    tcli publish --config-path $toml --file $zip --token $token
    if ($LASTEXITCODE -ne 0) { throw 'tcli publish nieudany' }
}
finally { Pop-Location }
$namespace = [regex]::Match($text, 'namespace = "([^"]+)"').Groups[1].Value
Write-Host "Opublikowano: https://thunderstore.io/c/valheim/p/$namespace/$Name/"
