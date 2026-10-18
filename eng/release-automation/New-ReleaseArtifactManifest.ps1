[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $CommitSha,

    # The generated WinGet submission manifests; they are recorded as one archive.
    [Parameter(Mandatory)]
    [string] $WinGetManifestDirectory,

    [string] $JsonPath,
    [string] $TextPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'ReleaseArtifactSet.ps1')

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'."
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory not found: $ArtifactDirectory"
}

# The release-bound terms are the committed files themselves, frozen with the release commit.
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination (Join-Path $ArtifactDirectory 'LICENSE')
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'PRIVACY.md') -Destination (Join-Path $ArtifactDirectory 'PRIVACY.md')

# The WinGet manifest set travels with the release, because the workflow artifact expires.
$WinGetManifestDirectory = [IO.Path]::GetFullPath($WinGetManifestDirectory)
$presentManifests = @(Get-ChildItem -LiteralPath $WinGetManifestDirectory -File | ForEach-Object Name | Sort-Object)
if (($presentManifests -join ',') -cne (($TigerSqlCmdWinGetManifestNames | Sort-Object) -join ',')) {
    throw "The WinGet manifest directory must hold exactly $($TigerSqlCmdWinGetManifestNames -join ', '); it holds $($presentManifests -join ', ')."
}
$archivePath = Join-Path $ArtifactDirectory "TigerSqlCmd-$Version-WinGet.zip"
Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::Open($archivePath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $TigerSqlCmdWinGetManifestNames) {
        $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $WinGetManifestDirectory $name), $name, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}

$set = @(Get-TigerQueryReleaseArtifactSet -Version $Version)
$expectedNames = @($set | ForEach-Object name)
$actualNames = @(
    Get-ChildItem -LiteralPath $ArtifactDirectory -File |
        ForEach-Object Name
)
$unexpected = @($actualNames | Where-Object { $_ -cnotin $expectedNames })
$missing = @($expectedNames | Where-Object { $_ -cnotin $actualNames })
if ($unexpected.Count -ne 0 -or $missing.Count -ne 0) {
    throw "Release payload mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
}

$artifacts = @(
    foreach ($artifact in $set) {
        $path = Join-Path $ArtifactDirectory $artifact.name
        [ordered]@{
            name = $artifact.name
            kind = $artifact.kind
            length = (Get-Item -LiteralPath $path).Length
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)

if ([string]::IsNullOrWhiteSpace($JsonPath)) {
    $JsonPath = Join-Path $ArtifactDirectory 'release-artifacts.json'
}
if ([string]::IsNullOrWhiteSpace($TextPath)) {
    $TextPath = Join-Path $ArtifactDirectory 'SHA256SUMS.txt'
}

$manifest = [ordered]@{
    schemaVersion = 1
    releaseVersion = $Version
    sourceCommit = $CommitSha.ToLowerInvariant()
    generatedAtUtc = [DateTime]::UtcNow.ToString('o')
    artifacts = $artifacts
}

$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $JsonPath -Encoding utf8NoBOM
$checksumLines = @($artifacts | ForEach-Object { "$($_.sha256)  $($_.name)" })
Set-Content -LiteralPath $TextPath -Value $checksumLines -Encoding utf8NoBOM

Write-Host "Recorded $($artifacts.Count) release payloads in $JsonPath and $TextPath."
