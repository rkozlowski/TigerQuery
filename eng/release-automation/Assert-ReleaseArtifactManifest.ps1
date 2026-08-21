[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Release artifact manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported release artifact manifest schema '$($manifest.schemaVersion)'."
}
if ($manifest.releaseVersion -cne $ExpectedVersion) {
    throw "Manifest version '$($manifest.releaseVersion)' does not match '$ExpectedVersion'."
}
if ($manifest.sourceCommit -cne $ExpectedCommit.ToLowerInvariant()) {
    throw "Manifest commit '$($manifest.sourceCommit)' does not match '$ExpectedCommit'."
}

$installerVersion = $ExpectedVersion -replace '\.', '_'
$expectedNames = @(
    "ItTiger.TigerQuery.$ExpectedVersion.nupkg"
    "ItTiger.TigerQuery.$ExpectedVersion.snupkg"
    "ItTiger.TigerQuery.Core.$ExpectedVersion.nupkg"
    "ItTiger.TigerQuery.Core.$ExpectedVersion.snupkg"
    "ItTiger.TigerQuery.CliCore.$ExpectedVersion.nupkg"
    "ItTiger.TigerQuery.CliCore.$ExpectedVersion.snupkg"
    "ItTiger.TigerSqlCmd.$ExpectedVersion.nupkg"
    "ItTiger.TigerSqlCmd.$ExpectedVersion.snupkg"
    "TigerSqlCmdSetup_$installerVersion.exe"
)

$entries = @($manifest.artifacts)
$entryNames = @($entries | ForEach-Object name)
$missing = @($expectedNames | Where-Object { $_ -cnotin $entryNames })
$unexpected = @($entryNames | Where-Object { $_ -cnotin $expectedNames })
if ($entries.Count -ne $expectedNames.Count -or $missing.Count -ne 0 -or $unexpected.Count -ne 0) {
    throw "Manifest payload mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', ')."
}

$allowedDirectoryNames = @($expectedNames + @('release-artifacts.json', 'SHA256SUMS.txt'))
$directoryNames = @(
    Get-ChildItem -LiteralPath $ArtifactDirectory -File |
        ForEach-Object Name
)
$extraFiles = @($directoryNames | Where-Object { $_ -cnotin $allowedDirectoryNames })
$missingFiles = @($allowedDirectoryNames | Where-Object { $_ -cnotin $directoryNames })
if ($extraFiles.Count -ne 0 -or $missingFiles.Count -ne 0) {
    throw "Release directory mismatch. Missing: $($missingFiles -join ', '); unexpected: $($extraFiles -join ', ')."
}

foreach ($entry in $entries) {
    if ($entry.sha256 -notmatch '^[0-9a-f]{64}$' -or $entry.length -lt 1) {
        throw "Manifest entry '$($entry.name)' has invalid length or SHA-256 metadata."
    }

    $path = Join-Path $ArtifactDirectory $entry.name
    $file = Get-Item -LiteralPath $path
    $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($file.Length -ne $entry.length -or $actualHash -cne $entry.sha256) {
        throw "Artifact '$($entry.name)' does not match the recorded length and SHA-256."
    }
}

$expectedText = @($entries | ForEach-Object { "$($_.sha256)  $($_.name)" }) -join [Environment]::NewLine
$actualText = (Get-Content -LiteralPath (Join-Path $ArtifactDirectory 'SHA256SUMS.txt') -Raw).TrimEnd("`r", "`n")
if ($actualText -cne $expectedText) {
    throw 'SHA256SUMS.txt does not exactly match release-artifacts.json.'
}

Write-Host "Verified $($entries.Count) artifacts for TigerQuery $ExpectedVersion at $ExpectedCommit."

