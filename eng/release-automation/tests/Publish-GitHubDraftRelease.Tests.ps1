[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path (Split-Path -Parent $PSScriptRoot) 'GitHubReleaseState.ps1')

$passed = 0

function Invoke-Test {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Body
    )

    & $Body
    $script:passed++
    Write-Host "PASS: $Name"
}

function Assert-True {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Body,

        [Parameter(Mandatory)]
        [string] $MessagePattern
    )

    try {
        & $Body
    }
    catch {
        if ($_.Exception.Message -notlike $MessagePattern) {
            throw "Expected exception matching '$MessagePattern'; found '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected exception matching '$MessagePattern', but no exception was thrown."
}

$tag = 'v0.8.8'
$title = 'TigerQuery 0.8.8'
$commit = '1111111111111111111111111111111111111111'
$otherCommit = '2222222222222222222222222222222222222222'
$expectedAsset = [pscustomobject]@{
    Name = 'TigerSqlCmdSetup_0_8_8.exe'
    Length = 12345L
    Digest = 'sha256:' + ('a' * 64)
}

Invoke-Test 'no release exists' {
    $release = Resolve-CompatibleGitHubRelease -Releases @() -Tag $tag -Title $title
    Assert-True -Condition ($null -eq $release) -Message 'Expected no matching release.'
}

Invoke-Test 'matching draft exists and retains numeric ID' {
    $release = Resolve-CompatibleGitHubRelease -Releases @([pscustomobject]@{
        id = 123456L
        tag_name = $tag
        name = $title
        draft = $true
        prerelease = $false
    }) -Tag $tag -Title $title
    Assert-True -Condition ([long] $release.id -eq 123456L) -Message 'Expected the matching draft release ID.'
}

Invoke-Test 'draft asset inspection identifies a missing asset' {
    $missing = @(Get-MissingCompatibleGitHubReleaseAssets `
        -ExistingAssets @() -ExpectedAssets @($expectedAsset) -Tag $tag)
    Assert-True -Condition ($missing.Count -eq 1 -and $missing[0].Name -ceq $expectedAsset.Name) `
        -Message 'Expected the absent draft asset to be returned for upload.'
}

Invoke-Test 'matching existing asset is accepted' {
    $missing = @(Get-MissingCompatibleGitHubReleaseAssets -ExistingAssets @([pscustomobject]@{
        name = $expectedAsset.Name
        state = 'uploaded'
        size = $expectedAsset.Length
        digest = $expectedAsset.Digest
    }) -ExpectedAssets @($expectedAsset) -Tag $tag)
    Assert-True -Condition ($missing.Count -eq 0) -Message 'Expected matching existing asset to be retained.'
}

Invoke-Test 'conflicting existing asset is rejected' {
    Assert-Throws -MessagePattern '*different bytes or incomplete metadata*' -Body {
        Get-MissingCompatibleGitHubReleaseAssets -ExistingAssets @([pscustomobject]@{
            name = $expectedAsset.Name
            state = 'uploaded'
            size = $expectedAsset.Length
            digest = 'sha256:' + ('b' * 64)
        }) -ExpectedAssets @($expectedAsset) -Tag $tag
    }
}

Invoke-Test 'unexpected draft asset is rejected' {
    Assert-Throws -MessagePattern '*contains unexpected assets*' -Body {
        Get-MissingCompatibleGitHubReleaseAssets -ExistingAssets @([pscustomobject]@{
            name = 'unexpected.zip'
            state = 'uploaded'
            size = 10L
            digest = 'sha256:' + ('c' * 64)
        }) -ExpectedAssets @($expectedAsset) -Tag $tag
    }
}

Invoke-Test 'published release is rejected' {
    Assert-Throws -MessagePattern '*already published*' -Body {
        Resolve-CompatibleGitHubRelease -Releases @([pscustomobject]@{
            id = 123456L
            tag_name = $tag
            name = $title
            draft = $false
            prerelease = $false
        }) -Tag $tag -Title $title
    }
}

Invoke-Test 'tag at the correct commit is accepted' {
    Assert-ExistingAnnotatedReleaseTag `
        -Tag $tag -ExpectedCommitSha $commit -ActualCommitSha $commit -ObjectType 'tag'
}

Invoke-Test 'tag at the wrong commit is rejected' {
    Assert-Throws -MessagePattern '*Never move an existing release tag*' -Body {
        Assert-ExistingAnnotatedReleaseTag `
            -Tag $tag -ExpectedCommitSha $commit -ActualCommitSha $otherCommit -ObjectType 'tag'
    }
}

Write-Host "All $passed release-state tests passed."
