#Requires -Version 7.0
<#
    .SYNOPSIS
    Validates a published TigerSqlCmd release as a WinGet package and reports whether
    its manifests are ready to submit to microsoft/winget-pkgs.

    .DESCRIPTION
    Three things have to be true before a winget-pkgs pull request is honest, and this
    checks all three:

      1. the prepared manifests say what this release implies - identity, version,
         installer metadata, and the immutable asset URL;
      2. the asset actually published at that URL is the one the manifests hash; and
      3. WinGet can install that exact payload on a clean Windows machine, run the
         command it registers, and remove it again.

    The third is TigerWinLab's job. This script does not build a validation environment
    of its own: it generates a TigerWinLab WinGet scenario specification for the release
    and runs TigerWinLab's own entry point against it, bounded by a timeout, then folds
    the lab's checks into one PASS/FAIL result.

    The run is read-only with respect to the host: nothing is installed, and WinGet's
    host settings are never touched. The installation happens in the lab guest.

    .PARAMETER Version
    The published release version to validate, for example 0.8.8.

    .PARAMETER TigerWinLabRoot
    The TigerWinLab working copy to use. Defaults to TIGERWINLAB_ROOT, then to a
    TigerWinLab checkout beside this repository.

    .PARAMETER SkipLab
    Runs the manifest and published-asset checks only. Useful for a quick re-check of a
    manifest edit; it cannot produce a submission-ready PASS on its own.

    .EXAMPLE
    .\eng\winget\Test-TigerSqlCmdWinGet.ps1 -Version 0.8.8

    .EXAMPLE
    .\eng\winget\Test-TigerSqlCmdWinGet.ps1 -Version 0.8.8 -Json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $TigerWinLabRoot,

    [string] $ManifestDirectory,

    [string] $OutputRoot,

    [ValidateRange(5, 240)]
    [int] $TimeoutMinutes = 45,

    [switch] $SkipLab,

    [switch] $Json
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'TigerSqlCmdWinGet.psm1') -Force

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$null = Assert-TigerSqlCmdWinGetVersion -Version $Version

if ([string]::IsNullOrWhiteSpace($ManifestDirectory)) {
    $ManifestDirectory = Join-Path $repositoryRoot "artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\$Version"
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot "artifacts\winget\validation\$Version"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$releaseInputDirectory = Join-Path $repositoryRoot "artifacts\winget-input\v$Version"
$resultPath = Join-Path $OutputRoot 'result.json'
$null = New-Item -ItemType Directory -Path $OutputRoot -Force

$manifestSet = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory $ManifestDirectory -Version $Version
$checks = [System.Collections.Generic.List[object]]::new()
foreach ($check in @(Test-TigerSqlCmdWinGetManifestSet -ManifestSet $manifestSet)) {
    $checks.Add($check)
}

Write-Host "Verifying the published release asset for TigerSqlCmd $Version..."
$asset = Test-TigerSqlCmdWinGetReleaseAsset `
    -ManifestSet $manifestSet `
    -DownloadDirectory (Join-Path $OutputRoot 'published') `
    -ReleaseInputDirectory $releaseInputDirectory
foreach ($check in @($asset.checks)) {
    $checks.Add($check)
}

$lab = $null
if ($SkipLab) {
    $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
        -Message '-SkipLab was requested, so the WinGet install and uninstall lifecycle was not validated in TigerWinLab.'))
}
elseif ($null -eq $asset.installerPath) {
    $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
        -Message 'The published installer could not be downloaded, so there was nothing to validate in TigerWinLab.'))
}
else {
    $labRoot = Resolve-TigerWinLabRoot -Path $TigerWinLabRoot
    $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/location' -Status 'PASS' `
        -Message "TigerWinLab resolved from $($labRoot.source): $($labRoot.root)."))

    # The lab installs what the release published, not what this repository built, so
    # the specification points at the file just downloaded from the immutable URL.
    $specPath = New-TigerSqlCmdWinGetLabSpec `
        -Version $Version `
        -ManifestDirectory $manifestSet.directory `
        -InstallerPath $asset.installerPath `
        -SpecPath (Join-Path $OutputRoot 'tigerwinlab-spec.json')
    $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/specification' -Status 'PASS' `
        -Message "Generated the TigerWinLab WinGet specification at $specPath."))

    $labResultPath = Join-Path $OutputRoot 'tigerwinlab-result.json'
    if (Test-Path -LiteralPath $labResultPath -PathType Leaf) {
        Remove-Item -LiteralPath $labResultPath -Force
    }
    $labOutputRoot = Join-Path $OutputRoot 'tigerwinlab-artifacts'

    $arguments = @(
        '-NoLogo', '-NoProfile', '-NonInteractive'
        '-ExecutionPolicy', 'Bypass'
        '-File', $labRoot.winGetScenario
        '-SpecPath', $specPath
        '-OutputRoot', $labOutputRoot
        '-ResultPath', $labResultPath
        '-TimeoutMinutes', $TimeoutMinutes
    )

    Write-Host "Running the TigerWinLab WinGet scenario (timeout: $TimeoutMinutes minutes)..."
    # TigerWinLab bounds the guest job itself; this bounds TigerWinLab. The margin is
    # what a baseline restore plus the lab's own teardown needs after the job's timeout
    # has already fired, so the watchdog only fires when the lab itself is stuck.
    $watchdogSeconds = ($TimeoutMinutes * 60) + 600
    $labExitCode = $null
    $labProcess = Start-Process -FilePath (Get-Process -Id $PID).Path `
        -ArgumentList $arguments -NoNewWindow -PassThru
    try {
        if ($labProcess.WaitForExit($watchdogSeconds * 1000)) {
            $labExitCode = $labProcess.ExitCode
        }
        else {
            Stop-Process -Id $labProcess.Id -Force -ErrorAction SilentlyContinue
            $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/watchdog' -Status 'FAIL' `
                -Message "The TigerWinLab WinGet scenario did not finish within $watchdogSeconds seconds and was terminated."))
        }
    }
    finally {
        $labProcess.Dispose()
    }

    $labJobResult = $null
    if (Test-Path -LiteralPath $labResultPath -PathType Leaf) {
        try {
            $labJobResult = Get-Content -LiteralPath $labResultPath -Raw | ConvertFrom-Json
        }
        catch {
            $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/result' -Status 'FAIL' `
                -Message "TigerWinLab wrote an unreadable result at '$labResultPath': $($_.Exception.Message)"))
        }
    }
    else {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/result' -Status 'FAIL' `
            -Message "TigerWinLab wrote no result at '$labResultPath' (exit code $labExitCode). Check that the lab is provisioned with New-TigerWinLab.ps1 and that this session is elevated."))
    }

    foreach ($check in @(Get-TigerSqlCmdWinGetScenarioCheck -JobResult $labJobResult)) {
        $checks.Add($check)
    }

    # Read every member through PSObject: a BUSY lease result is a different, much
    # smaller shape than a job result, and it carries no job id or output path.
    $jobOutputPath = $null
    $jobId = $null
    $jobStatus = $null
    if ($null -ne $labJobResult) {
        if ($null -ne $labJobResult.PSObject.Properties['jobId']) {
            $jobId = [string] $labJobResult.jobId
        }
        if ($null -ne $labJobResult.PSObject.Properties['status']) {
            $jobStatus = [string] $labJobResult.status
        }
        if ($null -ne $labJobResult.PSObject.Properties['outputPath']) {
            $jobOutputPath = [string] $labJobResult.outputPath
        }
    }

    $lab = [pscustomobject][ordered]@{
        root = $labRoot.root
        source = $labRoot.source
        scenario = $labRoot.winGetScenario
        specPath = $specPath
        resultPath = $labResultPath
        exitCode = $labExitCode
        jobId = $jobId
        jobStatus = $jobStatus
        jobOutputPath = $jobOutputPath
    }
}

$result = New-TigerSqlCmdWinGetResult `
    -ManifestSet $manifestSet `
    -Checks $checks.ToArray() `
    -Lab $lab `
    -Asset $asset `
    -ResultPath $resultPath

[System.IO.File]::WriteAllText(
    $resultPath,
    ($result | ConvertTo-Json -Depth 12),
    [System.Text.UTF8Encoding]::new($false))
$null = Save-TigerSqlCmdWinGetSummary -Result $result -Path (Join-Path $OutputRoot 'summary.txt')

if ($Json) {
    $result | ConvertTo-Json -Depth 12
}
else {
    Write-TigerSqlCmdWinGetSummary -Result $result
}

if ($result.status -ceq 'PASS') { exit 0 }
exit 1
