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
    [string] $ExpectedCommit,

    # A checkout of the release commit. When given, the frozen LICENSE and PRIVACY.md must be its
    # files byte for byte.
    [string] $SourceRepository
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'ReleaseArtifactSet.ps1')

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

$set = @(Get-TigerQueryReleaseArtifactSet -Version $ExpectedVersion)
$expectedNames = @($set | ForEach-Object name)
$entries = @($manifest.artifacts)
$entryNames = @($entries | ForEach-Object name)
if (($entryNames -join '|') -cne ($expectedNames -join '|')) {
    $missing = @($expectedNames | Where-Object { $_ -cnotin $entryNames })
    $unexpected = @($entryNames | Where-Object { $_ -cnotin $expectedNames })
    throw "Manifest payload mismatch. Missing: $($missing -join ', '); unexpected: $($unexpected -join ', '); the record lists every artifact once, in the release set's order."
}
for ($index = 0; $index -lt $set.Count; $index++) {
    if ([string] $entries[$index].kind -cne $set[$index].kind) {
        throw "Manifest entry '$($entries[$index].name)' has kind '$($entries[$index].kind)'; expected '$($set[$index].kind)'."
    }
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

# The WinGet archive holds exactly the three submission manifests, at its root.
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $ArtifactDirectory "TigerSqlCmd-$ExpectedVersion-WinGet.zip"))
try {
    $archived = @($archive.Entries | ForEach-Object FullName | Sort-Object)
}
finally {
    $archive.Dispose()
}
if (($archived -join ',') -cne (($TigerSqlCmdWinGetManifestNames | Sort-Object) -join ',')) {
    throw "TigerSqlCmd-$ExpectedVersion-WinGet.zip must hold exactly $($TigerSqlCmdWinGetManifestNames -join ', '); it holds $($archived -join ', ')."
}

if (-not [string]::IsNullOrWhiteSpace($SourceRepository)) {
    foreach ($name in 'LICENSE', 'PRIVACY.md') {
        $source = (Get-FileHash -LiteralPath (Join-Path $SourceRepository $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        $frozen = (Get-FileHash -LiteralPath (Join-Path $ArtifactDirectory $name) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($frozen -cne $source) {
            throw "The release's $name ($frozen) is not the release commit's $name ($source)."
        }
    }
}

Write-Host "Verified $($entries.Count) artifacts for TigerQuery $ExpectedVersion at $ExpectedCommit."
