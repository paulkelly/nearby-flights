# Refreshes src/FlightRadar.Core/Enrichment/airlines.json: ICAO airline code -> airline name, used to show the
# airline for callsigns like "EIN123" (Aer Lingus). Run it from anywhere, then review the diff and commit:
#
#   pwsh scripts/update-airlines.ps1        (or: powershell -File scripts/update-airlines.ps1)
#
# Source: Virtual Radar Server's community "standing data" (CC0, public domain):
# https://github.com/vradarserver/standing-data/tree/main/airlines/schema-01
#
# Why a committed snapshot rather than downloading at startup: the API starts the same way every time (no network
# dependency, nothing to fail), tests see fixed data, and airline names change rarely. One file per line, sorted,
# so a refresh shows up as a readable diff.

$ErrorActionPreference = 'Stop'

$source = 'https://raw.githubusercontent.com/vradarserver/standing-data/main/airlines/schema-01/airlines.csv'
$target = Join-Path $PSScriptRoot '..\src\FlightRadar.Core\Enrichment\airlines.json'

Write-Host "Downloading $source"
$csv = (Invoke-WebRequest -Uri $source -UseBasicParsing).Content
$csv = $csv.TrimStart([char]0xFEFF) # byte-order mark, which would otherwise end up in the first column's name

$airlines = $csv | ConvertFrom-Csv |
    # Only airlines with a 3-letter ICAO code: those are what callsigns start with. (Codes like "1B" are IATA.)
    Where-Object { $_.ICAO -cmatch '^[A-Z]{3}$' -and $_.Name.Trim() } |
    Sort-Object ICAO

# One "CODE": "Name" pair per line. ConvertTo-Json escapes each name properly (quotes, backslashes, non-ASCII).
# Some names arrive HTML-encoded ("D &amp; D Aviation"), so decode them first.
$lines = foreach ($airline in $airlines) {
    $name = [System.Net.WebUtility]::HtmlDecode($airline.Name).Trim()
    # Windows PowerShell 5.1 escapes & ' < > as & etc. Valid JSON, but unreadable in a diff; undo it.
    $quoted = (ConvertTo-Json $name) -replace '\\u0026', '&' -replace '\\u0027', "'" -replace '\\u003c', '<' -replace '\\u003e', '>'
    '  "{0}": {1}' -f $airline.ICAO, $quoted
}
$json = "{`n" + ($lines -join ",`n") + "`n}`n"

# UTF-8 without a byte-order mark (Windows PowerShell's Set-Content would add one).
$fullPath = [System.IO.Path]::GetFullPath($target)
[System.IO.File]::WriteAllText($fullPath, $json, [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $($airlines.Count) airlines to $fullPath"
