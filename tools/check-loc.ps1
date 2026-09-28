<#
.SYNOPSIS
  Checks a mod's translations (Assets\Localization\<Language>\*.json): every $token the C# code uses exists in every
  language, and all languages have the same keys. Tokens built at runtime ("$aoj_job_" + name) are reported as
  prefixes, and a prefix with no key at all is an error. Exit code 1 when something is missing.
.EXAMPLE
  ./tools/check-loc.ps1 AgeOfJarls
#>
param([Parameter(Mandatory)][string]$Name)
. "$PSScriptRoot\_common.ps1"

$mod = Join-Path $RepoRoot "mods\$Name"
$locDir = Join-Path $mod 'Assets\Localization'
if (-not (Test-Path $locDir)) { throw "Brak tlumaczen: $locDir" }

$languages = [ordered]@{}
# Valheim fills in $1, $2...: a {0} (C# style) would reach the player as it is.
$placeholders = [System.Collections.Generic.List[string]]::new()
foreach ($dir in Get-ChildItem $locDir -Directory) {
    $keys = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($file in Get-ChildItem $dir.FullName -Filter *.json) {
        $table = Get-Content $file.FullName -Raw | ConvertFrom-Json -AsHashtable
        $table.Keys | ForEach-Object { [void]$keys.Add($_) }
        $table.GetEnumerator() | Where-Object { $_.Value -match '\{\d+\}' } | ForEach-Object { $placeholders.Add("[$($dir.Name)] $($_.Key)") }
    }
    $languages[$dir.Name] = $keys
}
$allKeys = [System.Collections.Generic.HashSet[string]]::new()
$languages.Values | ForEach-Object { $allKeys.UnionWith($_) }

# The mod's own token prefixes (e.g. "aoj_"), so vanilla tokens such as $KEY_Use or $piece_* are left alone.
$prefixes = $allKeys | ForEach-Object { ($_ -split '_')[0] + '_' } | Sort-Object -Unique
$pattern = '\$((?:' + (($prefixes | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')[A-Za-z0-9_]*)'
$sources = Get-ChildItem $mod -Recurse -Filter *.cs | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
$used = $sources | Select-String -Pattern $pattern -AllMatches | ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[1].Value } } |
    Sort-Object -Unique
$dynamic = @($used | Where-Object { $_.EndsWith('_') })
$static = @($used | Where-Object { -not $_.EndsWith('_') })

$problems = 0
foreach ($lang in $languages.Keys) {
    $missing = @($static | Where-Object { -not $languages[$lang].Contains($_) })
    if ($missing.Count -gt 0) { Write-Host "[$lang] brakuje: $($missing -join ', ')"; $problems += $missing.Count }
    $extra = @($allKeys | Where-Object { -not $languages[$lang].Contains($_) })
    if ($extra.Count -gt 0) { Write-Host "[$lang] nie ma kluczy z innych jezykow: $($extra -join ', ')"; $problems += $extra.Count }
}
foreach ($prefix in $dynamic) {
    if (-not ($allKeys | Where-Object { $_.StartsWith($prefix) })) { Write-Host "prefiks bez kluczy: $prefix"; $problems++ }
}
$unused = @($allKeys | Where-Object { $key = $_; -not ($static -contains $key) -and -not ($dynamic | Where-Object { $key.StartsWith($_) }) })

Write-Host ("Jezyki: {0} | kluczy: {1} | tokenow w kodzie: {2} (+{3} prefiksow) | nieuzywanych: {4}" -f
    ($languages.Keys -join ', '), $allKeys.Count, $static.Count, $dynamic.Count, $unused.Count)
if ($unused.Count -gt 0) { Write-Host "nieuzywane (informacyjnie): $($unused -join ', ')" }
if ($placeholders.Count -gt 0) { Write-Host "parametry {0} zamiast `$1: $($placeholders -join ', ')"; $problems += $placeholders.Count }
if ($problems -gt 0) { Write-Host "Problemow: $problems"; exit 1 }
Write-Host 'OK'
