# Refreshes src/FlightRadar.Core/Enrichment/airports.json: airports with scheduled passenger service (those with an
# IATA code), with positions and elevations. Used to work out "From Dublin" / "To Dublin" / "At Dublin" when a
# flight has no route on record. Run it from anywhere, then review the diff and commit:
#
#   pwsh scripts/update-airports.ps1        (or: powershell -File scripts/update-airports.ps1)
#
# Source: Virtual Radar Server's community "standing data" (CC0, public domain), via adsb.lol's mirror:
# https://vrs-standing-data.adsb.lol/airports.csv
#
# Only airports with an IATA code: about 6,400 of the 34,000 in the source. The rest are mostly airfields, heliports
# and military bases without scheduled flights, and next to a big airport they'd steal the match (Dublin Airport
# has Casement Air Base and Weston Airport within 15 km). One airport per line, sorted, for readable diffs.

$ErrorActionPreference = 'Stop'

$source = 'https://vrs-standing-data.adsb.lol/airports.csv'
$target = Join-Path $PSScriptRoot '..\src\FlightRadar.Core\Enrichment\airports.json'
$invariant = [System.Globalization.CultureInfo]::InvariantCulture

Write-Host "Downloading $source"
$csv = (Invoke-WebRequest -Uri $source -UseBasicParsing).Content
$csv = $csv.TrimStart([char]0xFEFF) # byte-order mark, which would otherwise end up in the first column's name

$airports = $csv | ConvertFrom-Csv |
    Where-Object {
        $_.ICAO -cmatch '^[A-Z0-9]{4}$' -and $_.IATA -cmatch '^[A-Z0-9]{3}$' -and
        $_.Latitude -and $_.Longitude -and $_.Name.Trim()
    } |
    Sort-Object ICAO

# Windows PowerShell 5.1 escapes & ' < > as & etc. Valid JSON, but unreadable in a diff; undo it.
function Quote([string] $text) {
    if (-not $text) { return 'null' }
    $decoded = [System.Net.WebUtility]::HtmlDecode($text).Trim()
    (ConvertTo-Json $decoded) -replace '\\u0026', '&' -replace '\\u0027', "'" -replace '\\u003c', '<' -replace '\\u003e', '>'
}

function Number([string] $text) {
    # Parsed and re-printed with "." whatever the machine's locale, so the file is the same on every machine.
    [double]::Parse($text, $invariant).ToString('R', $invariant)
}

$lines = foreach ($airport in $airports) {
    $elevation = if ($airport.AltitudeFeet) { Number $airport.AltitudeFeet } else { '0' }
    '  {{"icao":"{0}","iata":"{1}","name":{2},"city":{3},"country":{4},"lat":{5},"lon":{6},"elevationFt":{7}}}' -f `
        $airport.ICAO, $airport.IATA, (Quote $airport.Name), (Quote $airport.Location), (Quote $airport.CountryISO2),
        (Number $airport.Latitude), (Number $airport.Longitude), $elevation
}
$json = "[`n" + ($lines -join ",`n") + "`n]`n"

# UTF-8 without a byte-order mark (Windows PowerShell's Set-Content would add one).
$fullPath = [System.IO.Path]::GetFullPath($target)
[System.IO.File]::WriteAllText($fullPath, $json, [System.Text.UTF8Encoding]::new($false))

Write-Host "Wrote $($airports.Count) airports to $fullPath"
