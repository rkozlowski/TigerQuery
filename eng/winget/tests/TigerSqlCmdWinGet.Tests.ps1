#Requires -Version 7.0
<#
    Host-only tests for the TigerSqlCmd WinGet preparation helpers.

    Nothing here starts a VM, touches WinGet, or reaches the network: these cover the
    manifest reasoning, the TigerWinLab specification this repository generates, and the
    way a lab result is folded into one verdict. The lifecycle itself is validated by
    Test-TigerSqlCmdWinGet.ps1 through TigerWinLab.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$wingetDirectory = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $wingetDirectory 'TigerSqlCmdWinGet.psm1') -Force

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

function Get-Check {
    param(
        [Parameter(Mandatory)]
        [object[]] $Checks,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $match = @($Checks | Where-Object { $_.name -ceq $Name })
    if ($match.Count -ne 1) {
        throw "Expected exactly one '$Name' check; found $($match.Count) in: $(($Checks | ForEach-Object name) -join ', ')."
    }
    $match[0]
}

function New-FixtureManifestSet {
    <#
        Writes a complete, correct 1.2.3 manifest set so each test can mutate exactly
        one thing and show that exactly one check notices.
    #>
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    $null = New-Item -ItemType Directory -Path $Directory -Force
    $utf8 = [System.Text.UTF8Encoding]::new($false)

    $installer = @'
# yaml-language-server: $schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
PackageIdentifier: ItTiger.TigerSqlCmd
PackageVersion: 1.2.3
InstallerLocale: en-US
Platform:
- Windows.Desktop
MinimumOSVersion: 10.0.17763.0
InstallerType: inno
Scope: machine
InstallModes:
- interactive
- silent
- silentWithProgress
UpgradeBehavior: install
ElevationRequirement: elevatesSelf
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.DotNet.Runtime.10
Commands:
- tiger-sqlcmd
AppsAndFeaturesEntries:
- DisplayName: TigerSqlCmd 1.2.3
  Publisher: IT Tiger
  DisplayVersion: 1.2.3
  ProductCode: ItTiger.TigerSqlCmd_is1
  InstallerType: inno
Installers:
- Architecture: x64
  InstallerUrl: https://github.com/rkozlowski/TigerQuery/releases/download/v1.2.3/TigerSqlCmdSetup_1_2_3.exe
  InstallerSha256: 0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF
ManifestType: installer
ManifestVersion: 1.12.0
'@

    $locale = @'
# yaml-language-server: $schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
PackageIdentifier: ItTiger.TigerSqlCmd
PackageVersion: 1.2.3
PackageLocale: en-US
Publisher: IT Tiger
PackageName: TigerSqlCmd
License: MIT
ShortDescription: SQL Server script and query runner built on the TigerQuery engine.
Tags:
- cli
- sql
ReleaseNotesUrl: https://github.com/rkozlowski/TigerQuery/releases/tag/v1.2.3
ManifestType: defaultLocale
ManifestVersion: 1.12.0
'@

    $version = @'
# yaml-language-server: $schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
PackageIdentifier: ItTiger.TigerSqlCmd
PackageVersion: 1.2.3
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
'@

    [System.IO.File]::WriteAllText((Join-Path $Directory 'ItTiger.TigerSqlCmd.installer.yaml'), $installer, $utf8)
    [System.IO.File]::WriteAllText((Join-Path $Directory 'ItTiger.TigerSqlCmd.locale.en-US.yaml'), $locale, $utf8)
    [System.IO.File]::WriteAllText((Join-Path $Directory 'ItTiger.TigerSqlCmd.yaml'), $version, $utf8)
    $Directory
}

function New-FixtureChecks {
    param(
        [Parameter(Mandatory)]
        [string] $Directory
    )

    $set = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory $Directory -Version '1.2.3'
    @(Test-TigerSqlCmdWinGetManifestSet -ManifestSet $set)
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("TigerSqlCmdWinGet-tests-" + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot -Force

try {
    Invoke-Test 'a stable three-part version is accepted and anything else is rejected' {
        Assert-True ((Assert-TigerSqlCmdWinGetVersion -Version '0.8.8') -ceq '0.8.8') 'The version should be echoed back.'
        foreach ($bad in @('0.8', '0.8.8.1', '0.8.8-rc.1', 'v0.8.8', '0.8.8 ')) {
            Assert-Throws -MessagePattern '*must be a stable three-part numeric version*' -Body {
                $null = Assert-TigerSqlCmdWinGetVersion -Version $bad
            }
        }
    }

    Invoke-Test 'release facts derive the tag, file name, asset URL, and submission path' {
        $facts = Get-TigerSqlCmdWinGetReleaseFact -Version '0.8.8'
        Assert-True ($facts.tag -ceq 'v0.8.8') "Tag was '$($facts.tag)'."
        Assert-True ($facts.installerFileName -ceq 'TigerSqlCmdSetup_0_8_8.exe') "File name was '$($facts.installerFileName)'."
        Assert-True ($facts.installerUrl -ceq 'https://github.com/rkozlowski/TigerQuery/releases/download/v0.8.8/TigerSqlCmdSetup_0_8_8.exe') "URL was '$($facts.installerUrl)'."
        Assert-True ($facts.releaseNotesUrl -ceq 'https://github.com/rkozlowski/TigerQuery/releases/tag/v0.8.8') "Notes URL was '$($facts.releaseNotesUrl)'."
        Assert-True ($facts.submissionPath -ceq 'manifests/i/ItTiger/TigerSqlCmd/0.8.8') "Submission path was '$($facts.submissionPath)'."
        Assert-True ($facts.manifestFileNames.Count -eq 3) 'A submission is exactly three manifests.'
    }

    Invoke-Test 'the manifest readers read scalars, sequences, and only indented dependencies' {
        $lines = @(
            'PackageIdentifier: ItTiger.TigerSqlCmd'
            'Dependencies:'
            '  PackageDependencies:'
            '  - PackageIdentifier: Microsoft.DotNet.Runtime.10'
            'Commands:'
            '- tiger-sqlcmd'
            '- tiger-sqlcmd-alias'
            'ManifestType: installer'
            'Installers:'
            '- Architecture: x64'
            '  InstallerSha256: ABC'
        )
        Assert-True ((Get-TigerSqlCmdWinGetManifestField -Lines $lines -Name 'PackageIdentifier') -ceq 'ItTiger.TigerSqlCmd') 'The top-level scalar should be read.'
        Assert-True ((Get-TigerSqlCmdWinGetManifestField -Lines $lines -Name 'Architecture') -ceq 'x64') 'A sequence entry key should be read.'
        Assert-True ((Get-TigerSqlCmdWinGetManifestField -Lines $lines -Name 'InstallerSha256') -ceq 'ABC') 'An indented key should be read.'
        Assert-True ($null -eq (Get-TigerSqlCmdWinGetManifestField -Lines $lines -Name 'Absent')) 'An absent key should read as null.'

        $commands = @(Get-TigerSqlCmdWinGetManifestListItem -Lines $lines -Name 'Commands')
        Assert-True ($commands.Count -eq 2 -and $commands[0] -ceq 'tiger-sqlcmd') "Commands were '$($commands -join ', ')'."

        $dependencies = @(Get-TigerSqlCmdWinGetManifestDependency -Lines $lines)
        Assert-True ($dependencies.Count -eq 1 -and $dependencies[0] -ceq 'Microsoft.DotNet.Runtime.10') "Dependencies were '$($dependencies -join ', ')'; the package's own identifier must not be one."
    }

    Invoke-Test 'a missing directory and an incomplete set are operator errors, not check results' {
        Assert-Throws -MessagePattern '*Prepared WinGet manifest directory not found*' -Body {
            $null = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory (Join-Path $testRoot 'absent') -Version '1.2.3'
        }

        $partial = New-FixtureManifestSet -Directory (Join-Path $testRoot 'partial')
        Remove-Item -LiteralPath (Join-Path $partial 'ItTiger.TigerSqlCmd.yaml') -Force
        Assert-Throws -MessagePattern '*manifest set is incomplete*' -Body {
            $null = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory $partial -Version '1.2.3'
        }
    }

    Invoke-Test 'a correct manifest set passes every manifest check' {
        $checks = New-FixtureChecks -Directory (New-FixtureManifestSet -Directory (Join-Path $testRoot 'good'))
        $notPassed = @($checks | Where-Object { $_.status -cne 'PASS' })
        Assert-True ($notPassed.Count -eq 0) "These checks did not pass: $(($notPassed | ForEach-Object { "$($_.name): $($_.message)" }) -join '; ')"
        Assert-True ($checks.Count -ge 15) "Only $($checks.Count) manifest checks ran."
    }

    Invoke-Test 'a manifest that names a different release URL fails exactly that check' {
        $directory = New-FixtureManifestSet -Directory (Join-Path $testRoot 'wrong-url')
        $path = Join-Path $directory 'ItTiger.TigerSqlCmd.installer.yaml'
        (Get-Content -LiteralPath $path -Raw).Replace(
            'https://github.com/rkozlowski/TigerQuery/releases/download/v1.2.3/TigerSqlCmdSetup_1_2_3.exe',
            'https://example.invalid/TigerSqlCmdSetup_1_2_3.exe') |
            Set-Content -LiteralPath $path -Encoding utf8NoBOM -NoNewline

        $checks = New-FixtureChecks -Directory $directory
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/installer-url').status -ceq 'FAIL') 'A foreign installer URL must fail.'
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/version').status -ceq 'PASS') 'Only the URL check should have noticed.'
    }

    Invoke-Test 'a version that disagrees across the set fails the version check' {
        $directory = New-FixtureManifestSet -Directory (Join-Path $testRoot 'wrong-version')
        $path = Join-Path $directory 'ItTiger.TigerSqlCmd.locale.en-US.yaml'
        (Get-Content -LiteralPath $path -Raw).Replace('PackageVersion: 1.2.3', 'PackageVersion: 1.2.4') |
            Set-Content -LiteralPath $path -Encoding utf8NoBOM -NoNewline

        $checks = New-FixtureChecks -Directory $directory
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/version').status -ceq 'FAIL') 'A disagreeing PackageVersion must fail.'
    }

    Invoke-Test 'a dropped runtime dependency fails the dependency check' {
        $directory = New-FixtureManifestSet -Directory (Join-Path $testRoot 'no-dependency')
        $path = Join-Path $directory 'ItTiger.TigerSqlCmd.installer.yaml'
        $kept = @(Get-Content -LiteralPath $path | Where-Object { $_ -cnotmatch 'Microsoft\.DotNet\.Runtime\.10' })
        Set-Content -LiteralPath $path -Value $kept -Encoding utf8NoBOM

        $checks = New-FixtureChecks -Directory $directory
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/dependency').status -ceq 'FAIL') 'A framework-dependent package must declare its runtime.'
    }

    Invoke-Test 'a byte-order mark and an unexpected extra file are both reported' {
        $directory = New-FixtureManifestSet -Directory (Join-Path $testRoot 'bom-and-extra')
        $path = Join-Path $directory 'ItTiger.TigerSqlCmd.yaml'
        $text = [System.IO.File]::ReadAllText($path)
        [System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($true))
        Set-Content -LiteralPath (Join-Path $directory 'notes.txt') -Value 'stray' -Encoding utf8NoBOM

        $checks = New-FixtureChecks -Directory $directory
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/encoding').status -ceq 'FAIL') 'A byte-order mark must be reported.'
        Assert-True ((Get-Check -Checks $checks -Name 'manifest/set').status -ceq 'FAIL') 'An extra file must be reported.'
    }

    Invoke-Test 'the release checksum file is read for the installer and nothing else' {
        $checksumPath = Join-Path $testRoot 'SHA256SUMS.txt'
        $digest = 'a' * 64
        Set-Content -LiteralPath $checksumPath -Encoding utf8NoBOM -Value @(
            ('{0}  ItTiger.TigerSqlCmd.1.2.3.nupkg' -f ('b' * 64))
            "$digest  TigerSqlCmdSetup_1_2_3.exe"
            ('{0} *TigerSqlCmdSetup_9_9_9.exe' -f ('c' * 64))
        )

        $found = Get-TigerSqlCmdWinGetRecordedChecksum -ChecksumPath $checksumPath -FileName 'TigerSqlCmdSetup_1_2_3.exe'
        Assert-True ($found -ceq $digest.ToUpperInvariant()) "The recorded digest read as '$found'."

        $binaryMarked = Get-TigerSqlCmdWinGetRecordedChecksum -ChecksumPath $checksumPath -FileName 'TigerSqlCmdSetup_9_9_9.exe'
        Assert-True ($binaryMarked -ceq ('c' * 64).ToUpperInvariant()) "A binary-marked entry read as '$binaryMarked'."

        $absent = Get-TigerSqlCmdWinGetRecordedChecksum -ChecksumPath $checksumPath -FileName 'TigerSqlCmdSetup_0_0_0.exe'
        Assert-True ($null -eq $absent) "An unrecorded file read as '$absent' instead of nothing."
    }

    Invoke-Test 'a warning does not block a submission but a failure and an empty run do' {
        $warnOnly = @(
            New-TigerSqlCmdWinGetCheck -Name 'a' -Status 'PASS' -Message 'ok'
            New-TigerSqlCmdWinGetCheck -Name 'b' -Status 'WARN' -Message 'read this'
        )
        $verdict = Get-TigerSqlCmdWinGetVerdict -Checks $warnOnly
        Assert-True ($verdict.status -ceq 'PASS') 'A warning must not block a submission.'
        Assert-True ($verdict.warned -eq 1 -and $verdict.passed -eq 1 -and $verdict.total -eq 2) 'The counts must describe the run.'

        $withFailure = @($warnOnly + (New-TigerSqlCmdWinGetCheck -Name 'c' -Status 'FAIL' -Message 'no'))
        Assert-True ((Get-TigerSqlCmdWinGetVerdict -Checks $withFailure).status -ceq 'FAIL') 'A failure must block a submission.'
        Assert-True ((Get-TigerSqlCmdWinGetVerdict -Checks @()).status -ceq 'FAIL') 'Proving nothing is not a pass.'
    }

    Invoke-Test 'a generated specification carries the version, the expected URL, and portable paths' {
        $directory = New-FixtureManifestSet -Directory (Join-Path $testRoot 'spec\manifests')
        $installerPath = Join-Path $testRoot 'spec\published\TigerSqlCmdSetup_1_2_3.exe'
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $installerPath) -Force
        Set-Content -LiteralPath $installerPath -Value 'not a real installer' -Encoding utf8NoBOM

        $specPath = New-TigerSqlCmdWinGetLabSpec `
            -Version '1.2.3' `
            -ManifestDirectory $directory `
            -InstallerPath $installerPath `
            -SpecPath (Join-Path $testRoot 'spec\tigerwinlab-spec.json')

        $spec = Get-Content -LiteralPath $specPath -Raw | ConvertFrom-Json
        Assert-True ($spec.package.version -ceq '1.2.3') "The specification version was '$($spec.package.version)'."
        Assert-True ($spec.package.identifier -ceq 'ItTiger.TigerSqlCmd') 'The specification must name the package.'
        Assert-True ($spec.installer.expectedUrl -ceq 'https://github.com/rkozlowski/TigerQuery/releases/download/v1.2.3/TigerSqlCmdSetup_1_2_3.exe') "The expected URL was '$($spec.installer.expectedUrl)'."
        Assert-True ($spec.manifestDirectory -ceq 'manifests') "The manifest directory was '$($spec.manifestDirectory)'; it must be relative to the specification."
        Assert-True ($spec.installer.path -ceq 'published\TigerSqlCmdSetup_1_2_3.exe') "The installer path was '$($spec.installer.path)'; it must be relative to the specification."
        Assert-True ($spec.hashMismatchProbe.enabled) 'The refusal probe must stay enabled so the lab result is not green by construction.'

        # A specification that is not on the specification file's drive has to stay absolute.
        $foreign = Get-TigerSqlCmdWinGetRelativePath -From 'C:\one' -To 'Z:\two\three.exe'
        Assert-True ($foreign -ceq 'Z:\two\three.exe') "A cross-drive path was rewritten to '$foreign'."
    }

    Invoke-Test 'a lab result is flattened, and an absent, busy, or timed-out lab fails' {
        Assert-True ((Get-Check -Checks (Get-TigerSqlCmdWinGetScenarioCheck -JobResult $null) -Name 'lab/scenario').status -ceq 'FAIL') 'No result at all is a failure.'

        $busy = [pscustomobject]@{ status = 'BUSY'; message = 'another process owns the lab' }
        Assert-True ((Get-Check -Checks (Get-TigerSqlCmdWinGetScenarioCheck -JobResult $busy) -Name 'lab/scenario').status -ceq 'FAIL') 'A busy lab is a failure, not a pass.'

        $completed = [pscustomobject]@{
            status = 'OK'
            message = 'completed'
            jobId = 'winget-1'
            health = @([pscustomobject]@{ name = 'disk'; status = 'PASS'; message = 'enough space' })
            result = [pscustomobject]@{
                phases = @(
                    [pscustomobject]@{ name = 'validate'; checks = @([pscustomobject]@{ name = 'manifest'; status = 'PASS'; message = 'accepted' }) }
                    [pscustomobject]@{ name = 'cleanup'; checks = @([pscustomobject]@{ name = 'path'; status = 'WARN'; message = 'left behind' }) }
                )
            }
        }
        $checks = @(Get-TigerSqlCmdWinGetScenarioCheck -JobResult $completed)
        Assert-True ((Get-Check -Checks $checks -Name 'lab/health/disk').status -ceq 'PASS') 'Guest health checks travel with the result.'
        Assert-True ((Get-Check -Checks $checks -Name 'lab/validate/manifest').status -ceq 'PASS') 'A phase check keeps its phase and check name.'
        Assert-True ((Get-Check -Checks $checks -Name 'lab/cleanup/path').status -ceq 'WARN') 'A lab warning stays a warning.'

        $timedOut = [pscustomobject]@{
            status = 'TIMEOUT'
            message = 'the job did not finish'
            jobId = 'winget-2'
            health = @()
            result = [pscustomobject]@{ phases = @() }
        }
        Assert-True ((Get-Check -Checks (Get-TigerSqlCmdWinGetScenarioCheck -JobResult $timedOut) -Name 'lab/scenario').status -ceq 'FAIL') 'A timed-out scenario must fail even when it left no failing phase.'
    }

    Invoke-Test 'the submission set is the three manifests at their winget-pkgs path' {
        $set = Read-TigerSqlCmdWinGetManifestSet `
            -ManifestDirectory (New-FixtureManifestSet -Directory (Join-Path $testRoot 'submission')) `
            -Version '1.2.3'
        $files = @(Get-TigerSqlCmdWinGetSubmissionFile -ManifestSet $set)
        Assert-True ($files.Count -eq 3) "The submission had $($files.Count) files."
        foreach ($file in $files) {
            Assert-True ($file.submissionPath -ceq "manifests/i/ItTiger/TigerSqlCmd/1.2.3/$($file.name)") "Submission path was '$($file.submissionPath)'."
            Assert-True ($file.sha256 -cmatch '^[0-9A-F]{64}$') 'Every submission file records its digest.'
            Assert-True ($file.length -gt 0) 'Every submission file records its length.'
        }
    }

    Invoke-Test 'TigerWinLab is resolved from an explicit path and a wrong one is refused' {
        $labRoot = Join-Path $testRoot 'fake-lab'
        $null = New-Item -ItemType Directory -Path $labRoot -Force
        Set-Content -LiteralPath (Join-Path $labRoot 'Invoke-TigerWinLabWinGetScenario.ps1') -Value '# stub' -Encoding utf8NoBOM

        $resolved = Resolve-TigerWinLabRoot -Path $labRoot
        Assert-True ($resolved.root -ceq ([System.IO.Path]::GetFullPath($labRoot))) "Resolved '$($resolved.root)'."
        Assert-True (Test-Path -LiteralPath $resolved.winGetScenario -PathType Leaf) 'The resolved root must expose the WinGet scenario entry point.'

        $previous = $env:TIGERWINLAB_ROOT
        $env:TIGERWINLAB_ROOT = Join-Path $testRoot 'no-lab-here'
        try {
            # The sibling checkout is still a candidate, so this only proves a refusal
            # where TigerWinLab is genuinely absent from every candidate.
            $sibling = Join-Path (Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $wingetDirectory))) 'TigerWinLab'
            if (-not (Test-Path -LiteralPath (Join-Path $sibling 'Invoke-TigerWinLabWinGetScenario.ps1') -PathType Leaf)) {
                Assert-Throws -MessagePattern '*TigerWinLab was not found*' -Body {
                    $null = Resolve-TigerWinLabRoot -Path (Join-Path $testRoot 'also-absent')
                }
            }
        }
        finally {
            $env:TIGERWINLAB_ROOT = $previous
        }
    }

    Invoke-Test 'a readiness result records the verdict, the installer, and the submission set' {
        $set = Read-TigerSqlCmdWinGetManifestSet `
            -ManifestDirectory (New-FixtureManifestSet -Directory (Join-Path $testRoot 'result')) `
            -Version '1.2.3'
        $checks = @(New-TigerSqlCmdWinGetCheck -Name 'manifest/identifier' -Status 'PASS' -Message 'ok')
        $asset = [pscustomobject]@{ sha256 = '0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF'; length = 42L }
        $result = New-TigerSqlCmdWinGetResult `
            -ManifestSet $set -Checks $checks -Lab $null -Asset $asset `
            -ResultPath (Join-Path $testRoot 'result\result.json')

        Assert-True ($result.status -ceq 'PASS') "The verdict was '$($result.status)'."
        Assert-True ($result.schemaVersion -eq 1) 'The result carries its schema version.'
        Assert-True ($result.version -ceq '1.2.3') 'The result names the version validated.'
        Assert-True ($result.submission.repository -ceq 'microsoft/winget-pkgs') 'The result names where the submission goes.'
        Assert-True ($result.installer.declaredSha256 -ceq $result.installer.publishedSha256) 'A passing result shows the declared and published digests agreeing.'
        Assert-True (@($result.submission.files).Count -eq 3) 'The result carries the submission set.'

        # The record has to survive the round trip a consumer actually makes.
        $roundTripped = $result | ConvertTo-Json -Depth 12 | ConvertFrom-Json
        Assert-True ($roundTripped.status -ceq 'PASS') 'The result must round-trip through JSON.'
        Assert-True (@($roundTripped.checks).Count -eq 1) 'The checks must round-trip through JSON.'

        # The human-readable report is the same report, so the two cannot disagree.
        $summaryPath = Save-TigerSqlCmdWinGetSummary -Result $result -Path (Join-Path $testRoot 'result\summary.txt')
        $summary = Get-Content -LiteralPath $summaryPath -Raw
        Assert-True ($summary -clike '*PASS: TigerSqlCmd 1.2.3 is ready*') 'The summary file must state the verdict.'
        Assert-True ($summary -clike '*manifest/identifier*') 'The summary file must list the checks.'
        Assert-True ($summary -clike '*manifests/i/ItTiger/TigerSqlCmd/1.2.3*') 'The summary file must name the submission path.'
    }

    Invoke-Test 'the prepared manifests in this working tree pass, when they are present' {
        $repositoryRoot = Split-Path -Parent (Split-Path -Parent $wingetDirectory)
        $versionFile = Join-Path $repositoryRoot 'Version.props'
        [xml] $versionXml = Get-Content -LiteralPath $versionFile
        $version = [string] $versionXml.Project.PropertyGroup.Version
        $manifests = Join-Path $repositoryRoot "artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\$version"

        if (-not (Test-Path -LiteralPath $manifests -PathType Container)) {
            Write-Host "  (skipped: no prepared manifests for $version; run eng\winget\Prepare-TigerSqlCmdWinGet.ps1 first)"
            return
        }

        $set = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory $manifests -Version $version
        $checks = @(Test-TigerSqlCmdWinGetManifestSet -ManifestSet $set)
        $notPassed = @($checks | Where-Object { $_.status -cne 'PASS' })
        Assert-True ($notPassed.Count -eq 0) "These checks did not pass for $version`: $(($notPassed | ForEach-Object { "$($_.name): $($_.message)" }) -join '; ')"
    }
}
finally {
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "All $passed TigerSqlCmd WinGet preparation tests passed."
