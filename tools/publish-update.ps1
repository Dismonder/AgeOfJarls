<#
.SYNOPSIS
  Publishes a mod version to its update page on Cloudflare Pages, from which the mod updates itself (Core/AutoUpdate):
  builds the Thunderstore zip (tools/package.ps1), writes manifest.json (version, zip URL, SHA-256) and index.html
  from cloud/<project>/template.html, and deploys the folder with wrangler (logged in with `npx wrangler login`).
.EXAMPLE
  ./tools/publish-update.ps1 AgeOfJarls                 # package + deploy
  ./tools/publish-update.ps1 AgeOfJarls -NoDeploy       # only prepare cloud/aoj-updates/site
#>
param(
    [Parameter(Mandatory, Position = 0)][string]$Name,
    [string]$Project = 'aoj-updates',
    [switch]$NoDeploy
)
. "$PSScriptRoot\_common.ps1"

& "$PSScriptRoot\package.ps1" $Name
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne $null) { throw 'Pakowanie nieudane' }

$csproj  = Join-Path $RepoRoot "mods\$Name\$Name.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip     = Join-Path $RepoRoot "dist\$Name-$version.zip"
if (-not (Test-Path $zip)) { throw "Brak paczki: $zip" }

$cloudDir = Join-Path $RepoRoot "cloud\$Project"
$siteDir  = Join-Path $cloudDir 'site'
$template = Join-Path $cloudDir 'template.html'
if (-not (Test-Path $template)) { throw "Brak szablonu: $template" }
if (Test-Path $siteDir) { Remove-Item $siteDir -Recurse -Force }
New-Item -ItemType Directory -Force $siteDir | Out-Null

$zipName = Split-Path $zip -Leaf
Copy-Item $zip (Join-Path $siteDir $zipName)
# A fixed name for the big download button and for links that should not go stale.
Copy-Item $zip (Join-Path $siteDir "$Name-latest.zip")
# Artwork shared with the GitHub README (docs/media).
$media = Join-Path $RepoRoot 'docs\media'
if (Test-Path $media) {
    New-Item -ItemType Directory -Force (Join-Path $siteDir 'media') | Out-Null
    Copy-Item (Join-Path $media '*.png') (Join-Path $siteDir 'media')
}
$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$baseUrl = "https://$Project.pages.dev"
$date = Get-Date -Format 'yyyy-MM-dd HH:mm'

$changelog = Get-Content (Join-Path $RepoRoot "mods\$Name\Package\CHANGELOG.md") -Raw
$manifest = [ordered]@{
    name    = $Name
    version = $version
    url     = "$baseUrl/$zipName"
    sha256  = $sha
    date    = $date
    notes   = ($changelog -split '\r?\n## ' | Select-Object -First 2 | Select-Object -Last 1) -replace '^\s*\d+\.\d+\.\d+\s*', ''
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content (Join-Path $siteDir 'manifest.json') -Encoding utf8NoBOM

$escaped = [System.Net.WebUtility]::HtmlEncode($changelog)
$html = (Get-Content $template -Raw).
    Replace('{{VERSION}}', $version).
    Replace('{{DATE}}', $date).
    Replace('{{ZIP}}', $zipName).
    Replace('{{SHA256}}', $sha).
    Replace('{{CHANGELOG}}', $escaped)
Set-Content (Join-Path $siteDir 'index.html') $html -Encoding utf8NoBOM

# The mod reads manifest.json straight from the edge: never a stale copy.
@"
/manifest.json
  Cache-Control: no-cache, max-age=0
  Access-Control-Allow-Origin: *
/*.zip
  Cache-Control: public, max-age=3600
"@ | Set-Content (Join-Path $siteDir '_headers') -Encoding utf8NoBOM

Write-Host "Strona: $siteDir ($Name $version, $zipName, sha256 $sha)"
if ($NoDeploy) { return }

Push-Location $RepoRoot
try {
    npx --yes wrangler@latest pages deploy $siteDir --project-name $Project --branch main --commit-dirty=true
    if ($LASTEXITCODE -ne 0) { throw 'wrangler pages deploy nieudany' }
}
finally { Pop-Location }
Write-Host "Opublikowano: $baseUrl (manifest: $baseUrl/manifest.json)"
