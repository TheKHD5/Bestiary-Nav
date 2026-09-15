#Requires -Version 7.2
# Source fields: GarlandTools/Garland.Data/Modules/Mobs.cs and gt.mob.js.
# Mob IDs combine BNpcBase * 10000000000 + BNpcName; z is PlaceName, not TerritoryType.
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$mobUrl = 'https://www.garlandtools.org/db/doc/browse/en/2/mob.json'
$locationUrl = 'https://www.garlandtools.org/db/doc/core/en/3/data.json'
$mobs = Invoke-RestMethod -Uri $mobUrl
$locations = Invoke-RestMethod -Uri $locationUrl
$zones = (Get-Content (Join-Path $repo 'BestiaryNav/farming-areas.json') -Raw | ConvertFrom-Json).areas.location.area | Sort-Object -Unique
$entries = @{}
foreach ($mob in $mobs.browse) {
    if (!$mob.z -or $mob.t -or $locations.locationIndex.([string]$mob.z).name -notin $zones) { continue }
    $nameId = [uint32]([long]$mob.i % 10000000000L)
    if ($nameId -eq 0 -or [string]::IsNullOrWhiteSpace($mob.n)) { continue }
    $minimum = 0; $maximum = 0
    if ($mob.l -match '^(\d+)(?: - (\d+))?$') {
        $minimum = [int]$Matches[1]
        $maximum = if ($Matches[2]) { [int]$Matches[2] } else { $minimum }
    }
    if ($minimum -lt 0 -or $maximum -gt 100 -or $minimum -gt $maximum) { continue }
    $key = "$($mob.z)/$nameId"
    if ($entries.ContainsKey($key)) {
        $old = $entries[$key]
        if ($old.minimumLevel -gt 0) { $minimum = if ($minimum -eq 0) { $old.minimumLevel } else { [Math]::Min($minimum, $old.minimumLevel) } }
        $maximum = [Math]::Max($maximum, $old.maximumLevel)
    }
    $entries[$key] = [ordered]@{ placeNameId = [uint32]$mob.z; nameId = $nameId; name = [string]$mob.n; minimumLevel = $minimum; maximumLevel = $maximum }
}
if ($entries.Count -lt 200) { throw 'Unexpectedly incomplete mob catalog; preserving the existing file.' }
$result = [ordered]@{
    source = $mobUrl
    locationSource = $locationUrl
    note = 'Known zone species, including special/event spawns; not guaranteed exhaustive or present inside every patrol circle. Runtime sightings extend the list. Level ranges are informational; actual enemy levels control pulls.'
    species = @($entries.Values | Sort-Object { $_.placeNameId }, { $_.nameId })
}
$path = Join-Path $repo 'BestiaryNav/farming-targets.json'
[IO.File]::WriteAllText($path, ($result | ConvertTo-Json -Depth 6) + "`n")
Write-Output "Wrote $($entries.Count) species/zone records."
