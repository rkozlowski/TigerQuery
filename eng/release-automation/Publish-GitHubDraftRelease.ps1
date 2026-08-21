[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $CommitSha,

    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [Parameter(Mandatory)]
    [string] $ManifestPath,

    [Parameter(Mandatory)]
    [string] $ReleaseNotesHeaderPath,

    [string] $Repository = 'rkozlowski/TigerQuery',
    [switch] $PlanOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid release version '$Version'."
}
$CommitSha = $CommitSha.ToLowerInvariant()
$ArtifactDirectory = [IO.Path]::GetFullPath($ArtifactDirectory)
$ManifestPath = [IO.Path]::GetFullPath($ManifestPath)
$ReleaseNotesHeaderPath = [IO.Path]::GetFullPath($ReleaseNotesHeaderPath)
if (-not (Test-Path -LiteralPath $ReleaseNotesHeaderPath -PathType Leaf)) {
    throw "Release notes header not found: $ReleaseNotesHeaderPath"
}

& (Join-Path $PSScriptRoot 'Assert-ReleaseArtifactManifest.ps1') `
    -ArtifactDirectory $ArtifactDirectory `
    -ManifestPath $ManifestPath `
    -ExpectedVersion $Version `
    -ExpectedCommit $CommitSha

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$assetNames = @($manifest.artifacts | ForEach-Object name) + @(
    'release-artifacts.json'
    'SHA256SUMS.txt'
)
$assets = @(
    foreach ($name in $assetNames) {
        $path = Join-Path $ArtifactDirectory $name
        [pscustomobject]@{
            Name = $name
            Path = $path
            Length = (Get-Item -LiteralPath $path).Length
            Digest = 'sha256:' + (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
)

$tag = "v$Version"
$title = "TigerQuery $Version"
if ($PlanOnly) {
    Write-Host "PLAN: create or verify annotated tag $tag at $CommitSha."
    Write-Host "PLAN: create or verify draft GitHub Release '$title'."
    foreach ($asset in $assets) {
        Write-Host "PLAN: upload or verify $($asset.Name) ($($asset.Digest))."
    }
    return
}

foreach ($command in @('git', 'gh')) {
    if ($null -eq (Get-Command $command -ErrorAction SilentlyContinue)) {
        throw "Required command '$command' is not available."
    }
}

$head = (& git rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $head -cne $CommitSha) {
    throw "Checked-out commit '$head' does not match release commit '$CommitSha'."
}

$remoteTag = (& git ls-remote --tags origin "refs/tags/$tag" | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "Could not inspect remote tag $tag." }
if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    git config user.name 'github-actions[bot]'
    git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    & git tag -a $tag $CommitSha -m $title
    if ($LASTEXITCODE -ne 0) { throw "Could not create annotated tag $tag." }
    & git push origin "refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Could not push annotated tag $tag." }
    Write-Host "Created annotated tag $tag at $CommitSha."
}
else {
    & git fetch --force origin "refs/tags/$tag:refs/tags/$tag"
    if ($LASTEXITCODE -ne 0) { throw "Could not fetch existing tag $tag." }
    $tagCommit = (& git rev-list -n 1 $tag | Out-String).Trim()
    $tagType = (& git cat-file -t $tag | Out-String).Trim()
    if ($tagCommit -cne $CommitSha) {
        throw "Tag $tag points to '$tagCommit', not '$CommitSha'. Never move an existing release tag."
    }
    if ($tagType -cne 'tag') {
        throw "Tag $tag is not annotated. Manual intervention is required; it will not be replaced."
    }
    Write-Host "Existing annotated tag $tag already points to $CommitSha."
}

$releaseJson = & gh release view $tag --repo $Repository --json name,isDraft,isPrerelease,tagName 2>$null
$releaseFound = $LASTEXITCODE -eq 0
if (-not $releaseFound) {
    $header = (Get-Content -LiteralPath $ReleaseNotesHeaderPath -Raw).Trim()
    & gh release create $tag `
        --repo $Repository `
        --verify-tag `
        --draft `
        --title $title `
        --generate-notes `
        --notes $header
    if ($LASTEXITCODE -ne 0) { throw "Could not create draft GitHub Release for $tag." }
    Write-Host "Created draft GitHub Release $tag."
}
else {
    $release = $releaseJson | ConvertFrom-Json
    if (-not $release.isDraft) {
        throw "GitHub Release $tag is already published. This workflow never edits a published release."
    }
    if ($release.isPrerelease -or $release.tagName -cne $tag -or $release.name -cne $title) {
        throw "Existing draft release $tag has conflicting title, tag, or prerelease state."
    }
    Write-Host "Existing draft GitHub Release $tag is compatible with this run."
}

$releaseApiJson = & gh api "repos/$Repository/releases/tags/$tag"
if ($LASTEXITCODE -ne 0) { throw "Could not inspect assets for release $tag." }
$releaseApi = $releaseApiJson | ConvertFrom-Json
if (-not $releaseApi.draft) { throw "Release $tag is not a draft." }

$existingAssets = @($releaseApi.assets)
$unexpectedAssets = @($existingAssets | Where-Object { $_.name -cnotin $assetNames })
if ($unexpectedAssets.Count -ne 0) {
    throw "Draft release $tag contains unexpected assets: $($unexpectedAssets.name -join ', ')."
}

foreach ($asset in $assets) {
    $matches = @($existingAssets | Where-Object { $_.name -ceq $asset.Name })
    if ($matches.Count -gt 1) { throw "Draft release $tag contains duplicate asset '$($asset.Name)'." }
    if ($matches.Count -eq 1) {
        $remoteAsset = $matches[0]
        if ($remoteAsset.state -cne 'uploaded' -or $remoteAsset.size -ne $asset.Length -or $remoteAsset.digest -cne $asset.Digest) {
            throw "Existing asset '$($asset.Name)' has different bytes or incomplete metadata. It will not be replaced automatically."
        }
        Write-Host "Verified existing release asset $($asset.Name) ($($asset.Digest))."
        continue
    }

    & gh release upload $tag $asset.Path --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Could not upload release asset '$($asset.Name)'." }
    Write-Host "Uploaded release asset $($asset.Name)."
}

$verifiedReleaseJson = & gh api "repos/$Repository/releases/tags/$tag"
if ($LASTEXITCODE -ne 0) { throw "Could not verify uploaded assets for release $tag." }
$verifiedAssets = @(($verifiedReleaseJson | ConvertFrom-Json).assets)
$verifiedUnexpected = @($verifiedAssets | Where-Object { $_.name -cnotin $assetNames })
if ($verifiedAssets.Count -ne $assets.Count -or $verifiedUnexpected.Count -ne 0) {
    throw "Draft release $tag does not contain the exact expected asset-name set."
}
foreach ($asset in $assets) {
    $match = @($verifiedAssets | Where-Object { $_.name -ceq $asset.Name })
    if ($match.Count -ne 1 -or $match[0].state -cne 'uploaded' -or $match[0].digest -cne $asset.Digest) {
        throw "Release asset '$($asset.Name)' failed post-upload digest verification."
    }
}

Write-Host "Draft GitHub Release $tag contains the exact validated asset set and remains unpublished."

