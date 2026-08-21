[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'."
}

foreach ($id in @(
    'ItTiger.TigerQuery.Core'
    'ItTiger.TigerQuery'
    'ItTiger.TigerQuery.CliCore'
    'ItTiger.TigerSqlCmd'
)) {
    $lowerId = $id.ToLowerInvariant()
    $index = Invoke-RestMethod "https://api.nuget.org/v3-flatcontainer/$lowerId/index.json"
    if (@($index.versions) -contains $Version.ToLowerInvariant()) {
        throw "$id $Version already exists on NuGet.org. Published packages are immutable. If this follows a partial release run, stop and follow the documented recovery procedure; this workflow never skips or overwrites an existing version."
    }
}

Write-Host "All four NuGet package identities are available at version $Version."

