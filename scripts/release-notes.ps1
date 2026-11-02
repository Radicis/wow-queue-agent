param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = 'release-notes.md'
)
$ErrorActionPreference = 'Stop'

$lines = Get-Content (Join-Path $PSScriptRoot '..\CHANGELOG.md')
$header = "## [$Version]"
$start = ($lines | Select-String -SimpleMatch $header | Select-Object -First 1).LineNumber
if (-not $start) { throw "CHANGELOG.md has no section '$header'. Add it before tagging." }

$body = @()
foreach ($line in $lines[$start..($lines.Count - 1)]) {
    if ($line -match '^## \[') { break }
    $body += $line
}
$text = ($body -join "`n").Trim()
if (-not $text) { throw "CHANGELOG.md section '$header' is empty." }

Set-Content -Path $Output -Value $text -Encoding utf8
Write-Host "Wrote $Output"
