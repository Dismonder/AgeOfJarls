<#
.SYNOPSIS
  Releases a mod version everywhere with the very same zip: the Thunderstore package (tools/package.ps1), the update
  page on Cloudflare Pages (tools/publish-update.ps1, from which the mod updates itself), a git tag with a GitHub
  release carrying the zip, and Thunderstore (tools/publish-thunderstore.ps1, token from %USERPROFILE%\.thunderstore-token).
  Expects the version already bumped in mods/<Name>/<Name>.csproj and described in Package/CHANGELOG.md, and a clean
  working tree apart from that (the script commits the version files itself).
.EXAMPLE
  ./tools/release.ps1 AgeOfJarls                 # everything
  ./tools/release.ps1 AgeOfJarls -NoThunderstore # skip Thunderstore (no token yet)
  ./tools/release.ps1 AgeOfJarls -NoGitHub       # page + Thunderstore only
#>
param(
    [Parameter(Mandatory, Position = 0)][string]$Name,
    [switch]$NoThunderstore,
    [switch]$NoGitHub,
    [string]$Remote = 'origin'
)
. "$PSScriptRoot\_common.ps1"

$csproj  = Join-Path $RepoRoot "mods\$Name\$Name.csproj"
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "Version w .csproj musi byc x.y.z (jest '$version')" }
$tag = "v$version"
$changelog = Get-Content (Join-Path $RepoRoot "mods\$Name\Package\CHANGELOG.md") -Raw
if ($changelog -notmatch "(?m)^## $([regex]::Escape($version))\s*$") { throw "CHANGELOG.md nie ma sekcji '## $version'" }

Push-Location $RepoRoot
try {
    if (git tag -l $tag) { throw "Tag $tag juz istnieje - podbij wersje w $csproj" }

    # 1. Tests, package, update page (the page's zip is THE zip of this release).
    dotnet test (Join-Path $RepoRoot "tests\$Name.Tests") -v q -nologo 2>&1 | Select-String -Pattern 'Powodzenie|Passed|Niepowodzenie|Failed' | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { throw 'Testy nie przeszly' }
    & "$PSScriptRoot\publish-update.ps1" $Name
    $zip = Join-Path $RepoRoot "dist\$Name-$version.zip"
    if (-not (Test-Path $zip)) { throw "Brak paczki: $zip" }
    $sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()

    # 2. Version files the scripts touched go into the release commit; the tag marks it. thunderstore.toml gets the
    #    version here, before the commit, so the Thunderstore step later changes nothing.
    $toml = Join-Path $RepoRoot 'thunderstore.toml'
    if (Test-Path $toml) {
        $text = [regex]::Replace((Get-Content $toml -Raw), 'versionNumber = "[^"]*"', "versionNumber = `"$version`"")
        Set-Content $toml $text -Encoding utf8NoBOM
    }
    git add -- $csproj "mods/$Name/Package/manifest.json" "mods/$Name/Package/CHANGELOG.md" thunderstore.toml 2>$null
    $staged = git diff --cached --name-only
    if ($staged) {
        git commit -q -m "$Name $version`n`nCo-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
        if ($LASTEXITCODE -ne 0) { throw 'git commit nieudany' }
    }
    git tag $tag
    if (-not $NoGitHub) {
        git push -q $Remote HEAD --tags
        if ($LASTEXITCODE -ne 0) { throw 'git push nieudany' }
        $section = ($changelog -split '(?m)^## ')[1]
        $notes = "## " + $section.Trim() + "`n`nSHA-256: ``$sha```n`nPobieranie i instalacja: https://aoj-updates.pages.dev (mod aktualizuje sie sam z tej strony).`nDownload and install guide: https://aoj-updates.pages.dev (the mod updates itself from this page)."
        $notesFile = Join-Path $env:TEMP "$Name-$version-notes.md"
        Set-Content $notesFile $notes -Encoding utf8NoBOM
        gh release create $tag $zip --title "$Name $version" --notes-file $notesFile
        if ($LASTEXITCODE -ne 0) { throw 'gh release create nieudany' }
    }

    # 3. Thunderstore: the same zip.
    if (-not $NoThunderstore) {
        & "$PSScriptRoot\publish-thunderstore.ps1" $Name
    }
    Write-Host "Wydano $Name $version ($sha)"
}
finally { Pop-Location }
