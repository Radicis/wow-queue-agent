# Fails if CHANGELOG.md's [Unreleased] section has no content, so PRs are
# blocked before a missing entry turns into a release-time failure.
$ErrorActionPreference = 'Stop'

$lines = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md')
$header = '## [Unreleased]'
$start = ($lines | Select-String -SimpleMatch $header | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no '$header' section." }

$body = @()
foreach ($line in $lines[$start..($lines.Count - 1)]) {
    if ($line -match '^## \[') { break }
    $body += $line
}
$text = ($body -join "`n").Trim()
if (-not $text) {
    throw "CHANGELOG.md's '$header' section is empty. Add an entry describing this change before merging."
}

Write-Host "CHANGELOG.md '$header' section has content:`n$text"
