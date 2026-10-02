<#
.SYNOPSIS
  Copies Mod Menu's sources from this workspace into its own GitHub repository checkout (Dismonder/ModMenu), with the
  build files it needs, and commits. This workspace stays the source of truth; the public repo is a mirror.
.EXAMPLE
  ./tools/sync-modmenu-repo.ps1                       # sync into ..\ModMenu and commit
  ./tools/sync-modmenu-repo.ps1 -Push                 # ...and push
#>
param(
    [string]$Target = (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'ModMenu'),
    [switch]$Push
)
. "$PSScriptRoot\_common.ps1"

if (-not (Test-Path (Join-Path $Target '.git'))) { throw "Brak repozytorium: $Target (git clone https://github.com/Dismonder/ModMenu)" }

# Folders are replaced as a whole, so files deleted here disappear there too.
foreach ($dir in 'mods\ModMenu', 'mods\ModMenu.Patcher', 'tests\ModMenu.Tests') {
    $dst = Join-Path $Target $dir
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force }
    New-Item -ItemType Directory -Force $dst | Out-Null
    Get-ChildItem (Join-Path $RepoRoot $dir) -Force | Where-Object { $_.Name -notin 'bin', 'obj' } |
        Copy-Item -Destination $dst -Recurse -Force
}
New-Item -ItemType Directory -Force (Join-Path $Target 'tools') | Out-Null
foreach ($file in 'Directory.Build.props', 'Directory.Build.targets', '.gitignore', 'tools\_common.ps1', 'tools\package.ps1') {
    Copy-Item (Join-Path $RepoRoot $file) (Join-Path $Target $file) -Force
}
Move-Item (Join-Path $Target 'mods\ModMenu\REPO_README.md') (Join-Path $Target 'README.md') -Force
@'
<Solution>
  <Folder Name="/mods/">
    <Project Path="mods/ModMenu/ModMenu.csproj" />
    <Project Path="mods/ModMenu.Patcher/ModMenu.Patcher.csproj" />
  </Folder>
  <Folder Name="/tests/">
    <Project Path="tests/ModMenu.Tests/ModMenu.Tests.csproj" />
  </Folder>
</Solution>
'@ | Set-Content (Join-Path $Target 'ModMenu.slnx') -Encoding utf8NoBOM

Push-Location $Target
try {
    git add -A
    if (git status --porcelain) {
        $version = ([xml](Get-Content 'mods\ModMenu\ModMenu.csproj')).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        git commit -q -m "Mod Menu $version`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
        if ($LASTEXITCODE -ne 0) { throw 'git commit nieudany' }
    }
    if ($Push) {
        git push -q origin HEAD
        if ($LASTEXITCODE -ne 0) { throw 'git push nieudany' }
    }
}
finally { Pop-Location }
Write-Host "Zsynchronizowano: $Target"
