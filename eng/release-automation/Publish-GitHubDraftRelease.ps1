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
    [switch] $PlanOnly,
    [switch] $AllowDifferentHeadForRecovery
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'GitHubReleaseState.ps1')

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

function Get-GitHubReleases {
    $releasePagesJson = & gh api --paginate --slurp "repos/$Repository/releases?per_page=100"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list GitHub Releases for $Repository, including drafts."
    }

    $releasePages = $releasePagesJson | ConvertFrom-Json
    $releases = [Collections.Generic.List[object]]::new()
    foreach ($page in @($releasePages)) {
        foreach ($release in @($page)) {
            $releases.Add($release)
        }
    }
    return $releases.ToArray()
}

function Get-GitHubReleaseById {
    param(
        [Parameter(Mandatory)]
        [long] $ReleaseId
    )

    $releaseJson = & gh api "repos/$Repository/releases/$ReleaseId"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect GitHub Release ID $ReleaseId for $Repository."
    }
    $release = $releaseJson | ConvertFrom-Json
    if ([long] $release.id -ne $ReleaseId) {
        throw "GitHub returned release ID '$($release.id)' while inspecting release ID $ReleaseId."
    }
    Assert-CompatibleGitHubDraftRelease -Release $release -Tag $tag -Title $title
    return $release
}

$head = (& git rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw 'Could not resolve the checked-out commit.'
}
if ($head -cne $CommitSha) {
    if (-not $AllowDifferentHeadForRecovery) {
        throw "Checked-out commit '$head' does not match release commit '$CommitSha'."
    }
    Write-Warning "Recovery helper commit '$head' differs from release commit '$CommitSha'; the retained artifact manifest and existing tag must still match the release commit exactly."
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
    Assert-ExistingAnnotatedReleaseTag `
        -Tag $tag `
        -ExpectedCommitSha $CommitSha `
        -ActualCommitSha $tagCommit `
        -ObjectType $tagType
    Write-Host "Existing annotated tag $tag already points to $CommitSha."
}

$release = Resolve-CompatibleGitHubRelease -Releases @(Get-GitHubReleases) -Tag $tag -Title $title
if ($null -eq $release) {
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
    $release = Resolve-CompatibleGitHubRelease -Releases @(Get-GitHubReleases) -Tag $tag -Title $title
    if ($null -eq $release) {
        throw "GitHub reported successful creation of draft release $tag, but it was not returned by the draft-capable releases API."
    }
}
else {
    Write-Host "Existing draft GitHub Release $tag is compatible with this run."
}

$releaseId = [long] $release.id
Write-Host "Using draft GitHub Release ID $releaseId for $tag."
$releaseApi = Get-GitHubReleaseById -ReleaseId $releaseId

$existingAssets = @($releaseApi.assets)
$missingAssets = @(Get-MissingCompatibleGitHubReleaseAssets `
    -ExistingAssets $existingAssets `
    -ExpectedAssets $assets `
    -Tag $tag)
$missingAssetNames = @($missingAssets | ForEach-Object { [string] $_.Name })
foreach ($asset in $assets | Where-Object { [string] $_.Name -cnotin $missingAssetNames }) {
    Write-Host "Verified existing release asset $($asset.Name) ($($asset.Digest))."
}

foreach ($asset in $missingAssets) {
    & gh release upload $tag $asset.Path --repo $Repository
    if ($LASTEXITCODE -ne 0) { throw "Could not upload release asset '$($asset.Name)'." }
    Write-Host "Uploaded release asset $($asset.Name)."
}

$verifiedRelease = Get-GitHubReleaseById -ReleaseId $releaseId
$verifiedAssets = @($verifiedRelease.assets)
$stillMissingAssets = @(Get-MissingCompatibleGitHubReleaseAssets `
    -ExistingAssets $verifiedAssets `
    -ExpectedAssets $assets `
    -Tag $tag)
if ($stillMissingAssets.Count -ne 0) {
    throw "Draft release $tag does not contain the exact expected asset-name set."
}

Write-Host "Draft GitHub Release $tag (ID $releaseId) contains the exact validated asset set and remains unpublished."

