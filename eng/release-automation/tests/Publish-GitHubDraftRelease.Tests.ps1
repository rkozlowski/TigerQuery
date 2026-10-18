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
    Name = 'TigerSqlCmd-0.8.8-Setup.exe'
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

Invoke-Test 'a just-created draft missing from the list is awaited' {
    $script:listings = 0
    $sleeps = [Collections.Generic.List[int]]::new()
    $draft = [pscustomobject]@{ id = 654321L; tag_name = $tag; name = $title; draft = $true; prerelease = $false }
    $release = Wait-CompatibleGitHubRelease -Tag $tag -Title $title -Sleep { param($s) $sleeps.Add($s) } -GetReleases {
        $script:listings++
        if ($script:listings -ge 3) { $draft }
    }
    Assert-True ([long] $release.id -eq 654321L) 'Expected the draft once the list returned it.'
    Assert-True (($sleeps -join ',') -ceq '1,2') "Expected waits 1,2; found '$($sleeps -join ',')'."
}

Invoke-Test 'a draft that never appears is reported as absent after bounded retries' {
    $sleeps = [Collections.Generic.List[int]]::new()
    $release = Wait-CompatibleGitHubRelease -Tag $tag -Title $title -GetReleases { @() } `
        -RetryDelaySeconds @(1, 2) -Sleep { param($s) $sleeps.Add($s) }
    Assert-True ($null -eq $release) 'Expected no release.'
    Assert-True ($sleeps.Count -eq 2) "Expected two waits; found $($sleeps.Count)."
}

Invoke-Test 'release scripts do not read a drive-qualified variable by accident' {
    # "refs/tags/$tag:refs/tags/$tag" reads the variable 'refs' on drive 'tag', which is empty.
    $automation = Split-Path -Parent $PSScriptRoot
    foreach ($script in Get-ChildItem -LiteralPath $automation -Filter '*.ps1' -File) {
        $ast = [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref] $null, [ref] $null)
        $qualified = @($ast.FindAll({
            param($node)
            $node -is [Management.Automation.Language.VariableExpressionAst] -and
                $node.VariablePath.IsDriveQualified -and
                $node.VariablePath.DriveName -notin 'env', 'variable', 'function', 'alias'
        }, $true))
        if ($qualified.Count -ne 0) {
            throw "$($script.Name) reads drive-qualified variables: $(($qualified | ForEach-Object { $_.Extent.Text }) -join ', ')."
        }
    }
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

Invoke-Test 'tag message names the release record and the building run' {
    $record = 'b' * 64
    $message = New-ReleaseTagMessage -Title $title -RecordSha256 $record -RunUrl 'https://github.com/o/r/actions/runs/42'
    Assert-True ($message.StartsWith("$title`n`n")) "Message was '$message'."
    Assert-True ($message.Contains('Run: https://github.com/o/r/actions/runs/42')) "Message was '$message'."
    Assert-ReleaseTagNamesRecord -Tag $tag -Message $message -RecordSha256 $record

    $recovery = New-ReleaseTagMessage -Title $title -RecordSha256 $record
    Assert-True ($recovery.Contains('Run: none')) "Recovery message was '$recovery'."
}

Invoke-Test 'tag naming another release record is rejected' {
    $message = New-ReleaseTagMessage -Title $title -RecordSha256 ('b' * 64)
    Assert-Throws -MessagePattern '*does not name release record*' -Body {
        Assert-ReleaseTagNamesRecord -Tag $tag -Message $message -RecordSha256 ('c' * 64)
    }
    Assert-Throws -MessagePattern '*does not name release record*' -Body {
        Assert-ReleaseTagNamesRecord -Tag $tag -Message $title -RecordSha256 ('c' * 64)
    }
}

$automation = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $automation)
. (Join-Path $automation 'ReleaseArtifactSet.ps1')
$sandbox = Join-Path ([IO.Path]::GetTempPath()) ('TigerQuery-release-tests-' + [Guid]::NewGuid().ToString('N'))

function New-FixtureReleaseSet {
    <# A closed 1.2.3 release set, recorded by New-ReleaseArtifactManifest.ps1 over stand-in payloads. #>
    param([Parameter(Mandatory)] [string] $Name)

    $directory = Join-Path $sandbox "$Name\release"
    $manifests = Join-Path $sandbox "$Name\winget"
    $null = New-Item -ItemType Directory -Path $directory, $manifests -Force
    foreach ($artifact in Get-TigerQueryReleaseArtifactSet -Version '1.2.3') {
        if ($artifact.kind -in 'NuGetPackage', 'NuGetSymbols', 'WindowsInstaller') {
            Set-Content -LiteralPath (Join-Path $directory $artifact.name) -Value "stand-in $($artifact.name)" -Encoding utf8NoBOM
        }
    }
    foreach ($manifest in $TigerSqlCmdWinGetManifestNames) {
        Set-Content -LiteralPath (Join-Path $manifests $manifest) -Value "ManifestType: $manifest" -Encoding utf8NoBOM
    }
    & (Join-Path $automation 'New-ReleaseArtifactManifest.ps1') -ArtifactDirectory $directory -Version '1.2.3' `
        -CommitSha $commit -WinGetManifestDirectory $manifests | Out-Null
    $directory
}

function Assert-FixtureReleaseSet {
    param([Parameter(Mandatory)] [string] $Directory, [string] $SourceRepository)

    & (Join-Path $automation 'Assert-ReleaseArtifactManifest.ps1') -ArtifactDirectory $Directory `
        -ManifestPath (Join-Path $Directory 'release-artifacts.json') -ExpectedVersion '1.2.3' -ExpectedCommit $commit `
        -SourceRepository $SourceRepository | Out-Null
}

try {
    Invoke-Test 'the release record lists every artifact of the set, with its kind, terms included' {
        $directory = New-FixtureReleaseSet -Name 'record'
        $record = Get-Content -LiteralPath (Join-Path $directory 'release-artifacts.json') -Raw | ConvertFrom-Json
        $kinds = @($record.artifacts | ForEach-Object { "$($_.name)=$($_.kind)" })
        Assert-True ($kinds.Count -eq 12) "The record lists $($kinds.Count) artifacts."
        foreach ($expected in 'TigerSqlCmd-1.2.3-Setup.exe=WindowsInstaller', 'TigerSqlCmd-1.2.3-WinGet.zip=WinGetManifests',
            'LICENSE=License', 'PRIVACY.md=PrivacyStatement') {
            Assert-True ($kinds -ccontains $expected) "The record has no $expected."
        }
        Assert-FixtureReleaseSet -Directory $directory -SourceRepository $repositoryRoot
    }

    Invoke-Test 'frozen terms that are not the release commit''s files are refused' {
        $directory = New-FixtureReleaseSet -Name 'terms'
        $source = Join-Path $sandbox 'terms\source'
        $null = New-Item -ItemType Directory -Path $source -Force
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $source
        Set-Content -LiteralPath (Join-Path $source 'PRIVACY.md') -Value 'a different statement' -Encoding utf8NoBOM
        Assert-Throws -MessagePattern "*release's PRIVACY.md*is not the release commit's PRIVACY.md*" -Body {
            Assert-FixtureReleaseSet -Directory $directory -SourceRepository $source
        }
    }

    Invoke-Test 'a changed artifact and an extra file are refused' {
        $directory = New-FixtureReleaseSet -Name 'tampered'
        Add-Content -LiteralPath (Join-Path $directory 'PRIVACY.md') -Value 'appended later'
        Assert-Throws -MessagePattern "*Artifact 'PRIVACY.md' does not match*" -Body { Assert-FixtureReleaseSet -Directory $directory }

        $directory = New-FixtureReleaseSet -Name 'extra'
        Set-Content -LiteralPath (Join-Path $directory 'notes.txt') -Value 'stray' -Encoding utf8NoBOM
        Assert-Throws -MessagePattern '*Release directory mismatch*unexpected: notes.txt*' -Body { Assert-FixtureReleaseSet -Directory $directory }
    }

    Invoke-Test 'release notes need the product heading and real content' {
        & (Join-Path $automation 'Assert-ReleaseNotes.ps1') -Version ([xml] (Get-Content -LiteralPath (Join-Path $repositoryRoot 'Version.props') -Raw)).Project.PropertyGroup.Version | Out-Null
        $notes = Join-Path $sandbox 'notes.md'
        Set-Content -LiteralPath $notes -Value @('# TigerQuery 1.2.4', '', 'One line.', 'Two.', 'Three.') -Encoding utf8NoBOM
        Assert-Throws -MessagePattern "*must be '# TigerQuery 1.2.3'*" -Body {
            & (Join-Path $automation 'Assert-ReleaseNotes.ps1') -Version '1.2.3' -NotesFile $notes
        }
        Set-Content -LiteralPath $notes -Value @('# TigerQuery 1.2.3', '', 'TODO') -Encoding utf8NoBOM
        Assert-Throws -MessagePattern '*no real release notes*' -Body {
            & (Join-Path $automation 'Assert-ReleaseNotes.ps1') -Version '1.2.3' -NotesFile $notes
        }
    }
}
finally {
    Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "All $passed release-state tests passed."
