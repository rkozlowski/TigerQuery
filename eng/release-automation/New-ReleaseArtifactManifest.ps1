[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $CommitSha,

    [string] $JsonPath,
    [string] $TextPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'."
}

$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Artifact directory not found: $ArtifactDirectory"
}

$installerVersion = $Version -replace '\.', '_'
$expectedNames = @(
    "ItTiger.TigerQuery.$Version.nupkg"
    "ItTiger.TigerQuery.$Version.snupkg"
    "ItTiger.TigerQuery.Core.$Version.nupkg"
    "ItTiger.TigerQuery.Core.$Version.snupkg"
    "ItTiger.TigerQuery.CliCore.$Version.nupkg"
    "ItTiger.TigerQuery.CliCore.$Version.snupkg"
    "ItTiger.TigerSqlCmd.$Version.nupkg"
    "ItTiger.TigerSqlCmd.$Version.snupkg"
    "TigerSqlCmdSetup_$installerVersion.exe"
)

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
    foreach ($name in $expectedNames) {
        $path = Join-Path $ArtifactDirectory $name
        $file = Get-Item -LiteralPath $path
        [ordered]@{
            name = $name
            kind = if ($name.EndsWith('.snupkg', [StringComparison]::Ordinal)) {
                'NuGetSymbols'
            }
            elseif ($name.EndsWith('.nupkg', [StringComparison]::Ordinal)) {
                'NuGetPackage'
            }
            else {
                'WindowsInstaller'
            }
            length = $file.Length
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

