Set-StrictMode -Version Latest

function Assert-ExistingAnnotatedReleaseTag {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Tag,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ExpectedCommitSha,

        [Parameter(Mandatory)]
        [ValidatePattern('^[0-9a-fA-F]{40}$')]
        [string] $ActualCommitSha,

        [Parameter(Mandatory)]
        [string] $ObjectType
    )

    if ($ActualCommitSha -cne $ExpectedCommitSha) {
        throw "Tag $Tag points to '$ActualCommitSha', not '$ExpectedCommitSha'. Never move an existing release tag."
    }
    if ($ObjectType -cne 'tag') {
        throw "Tag $Tag is not annotated. Manual intervention is required; it will not be replaced."
    }
}

function Assert-CompatibleGitHubDraftRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Release,

        [Parameter(Mandatory)]
        [string] $Tag,

        [Parameter(Mandatory)]
        [string] $Title
    )

    $releaseId = 0L
    if (-not [long]::TryParse([string] $Release.id, [ref] $releaseId) -or $releaseId -le 0) {
        throw "GitHub Release $Tag does not expose a valid numeric release ID."
    }
    if ($Release.draft -cne $true) {
        throw "GitHub Release $Tag is already published. This automation never edits a published release."
    }
    if ($Release.prerelease -cne $false -or
        [string] $Release.tag_name -cne $Tag -or
        [string] $Release.name -cne $Title) {
        throw "Existing draft release $Tag has conflicting title, tag, or prerelease state."
    }
}

function Resolve-CompatibleGitHubRelease {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Releases,

        [Parameter(Mandatory)]
        [string] $Tag,

        [Parameter(Mandatory)]
        [string] $Title
    )

    $matches = @($Releases | Where-Object { [string] $_.tag_name -ceq $Tag })
    if ($matches.Count -eq 0) {
        return $null
    }
    if ($matches.Count -gt 1) {
        throw "GitHub contains multiple releases for tag $Tag. Manual intervention is required."
    }

    Assert-CompatibleGitHubDraftRelease -Release $matches[0] -Tag $Tag -Title $Title
    return $matches[0]
}

function Get-MissingCompatibleGitHubReleaseAssets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $ExistingAssets,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $ExpectedAssets,

        [Parameter(Mandatory)]
        [string] $Tag
    )

    $expectedNames = @($ExpectedAssets | ForEach-Object { [string] $_.Name })
    $unexpectedAssets = @($ExistingAssets | Where-Object { [string] $_.name -cnotin $expectedNames })
    if ($unexpectedAssets.Count -ne 0) {
        throw "Draft release $Tag contains unexpected assets: $($unexpectedAssets.name -join ', ')."
    }

    $missingAssets = [Collections.Generic.List[object]]::new()
    foreach ($expectedAsset in $ExpectedAssets) {
        $matches = @($ExistingAssets | Where-Object { [string] $_.name -ceq [string] $expectedAsset.Name })
        if ($matches.Count -gt 1) {
            throw "Draft release $Tag contains duplicate asset '$($expectedAsset.Name)'."
        }
        if ($matches.Count -eq 0) {
            $missingAssets.Add($expectedAsset)
            continue
        }

        $remoteAsset = $matches[0]
        if ([string] $remoteAsset.state -cne 'uploaded' -or
            [long] $remoteAsset.size -ne [long] $expectedAsset.Length -or
            [string] $remoteAsset.digest -cne [string] $expectedAsset.Digest) {
            throw "Existing asset '$($expectedAsset.Name)' has different bytes or incomplete metadata. It will not be replaced automatically."
        }
    }

    return $missingAssets.ToArray()
}
