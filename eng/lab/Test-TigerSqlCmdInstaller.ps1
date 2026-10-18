#Requires -Version 7.0
<#
    .SYNOPSIS
    Proves a TigerSqlCmd installer in TigerWinLab: a fresh install, the migration from the
    published Inno Setup release, and uninstall.

    .DESCRIPTION
    One TigerWinLab job on a clean Windows 11 guest, never on this desktop, running
    eng\lab\installer\accept.ps1: a fresh machine-wide install that acquires the .NET 10 runtime
    itself, and its removal; then the published Inno Setup release installed over a seeded
    connection store with a DPAPI-protected password, the candidate installed over it, and the
    migrated installation removed. Every assertion is this repository's; TigerWinLab supplies the
    guest and the evidence.

    The installer is the exact file named by -InstallerPath (a local candidate or a retrieved
    release asset); its declared contract is checked first with Assert-TigerSqlCmdInstaller.ps1.
    The Inno Setup installer it migrates from is the published release asset, downloaded once into
    artifacts\lab\retained\ and refused unless it hashes to the SHA-256 that release recorded.

    .PARAMETER InstallerPath
    Defaults to ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmd-<version>-Setup.exe.

    .PARAMETER TigerWinLabRoot
    An explicit TigerWinLab working copy. Omit it and the lab is resolved from its TigerAiCore
    registration.

    .PARAMETER Baseline
    The TigerWinLab baseline. Defaults to TigerWinLab-Win11; neither its profile nor Windows
    provides the .NET runtime, so the fresh install has to acquire it.
#>
[CmdletBinding()]
param(
    [string] $InstallerPath,
    [ValidateSet('0.8.8')]
    [string] $LegacyVersion = '0.8.8',
    [string] $LegacyInstallerPath,
    [string] $TigerWinLabRoot,
    [string] $Baseline = 'TigerWinLab-Win11',
    [ValidateRange(10, 240)]
    [int] $TimeoutMinutes = 60
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $repoRoot 'eng\winget\TigerSqlCmdWinGet.psm1') -Force
. (Join-Path $repoRoot 'ItTiger.TigerSqlCmd.Installer\TigerSetupBuilder.ps1')
[xml] $versionProps = Get-Content -LiteralPath (Join-Path $repoRoot 'Version.props') -Raw
$version = [string] $versionProps.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repoRoot "ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmd-$version-Setup.exe"
}
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
& (Join-Path $repoRoot 'eng\release-automation\Assert-TigerSqlCmdInstaller.ps1') -InstallerPath $InstallerPath -ExpectedVersion $version

# The candidate's own tiger-sqlcmd.dll, which the guest must find installed after the upgrade.
$payloadZip = Join-Path ([IO.Path]::GetTempPath()) ('TigerSqlCmd-lab-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $tigerSetup = Resolve-TigerSqlCmdTigerSetup -RepositoryRoot $repoRoot
    & $tigerSetup inspect $InstallerPath --output-zip $payloadZip | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "tiger-setup inspect --output-zip failed for $InstallerPath." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($payloadZip)
    try {
        $stream = ($zip.Entries | Where-Object FullName -ceq 'tiger-sqlcmd.dll').Open()
        try { $candidateDllSha256 = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash } finally { $stream.Dispose() }
    }
    finally { $zip.Dispose() }
}
finally { Remove-Item -LiteralPath $payloadZip -Force -ErrorAction SilentlyContinue }

# --- the published release it migrates from -----------------------------------------------------
# The digest the v0.8.8 release recorded in its release-artifacts.json and GitHub's asset digest.
$publishedInnoInstallers = @{ '0.8.8' = '5169879DE65B6ECA1FE50BE06A8E7309BFCEA69C876910C3D00DD1CCC05FE955' }
$legacyName = "TigerSqlCmdSetup_$($LegacyVersion -replace '\.', '_').exe"
if ([string]::IsNullOrWhiteSpace($LegacyInstallerPath)) {
    $LegacyInstallerPath = Join-Path $repoRoot "artifacts\lab\retained\$legacyName"
    if (-not (Test-Path -LiteralPath $LegacyInstallerPath)) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $LegacyInstallerPath) -Force | Out-Null
        Invoke-WebRequest -Uri "https://github.com/rkozlowski/TigerQuery/releases/download/v$LegacyVersion/$legacyName" -OutFile $LegacyInstallerPath
    }
}
$legacyHash = (Get-FileHash -LiteralPath $LegacyInstallerPath -Algorithm SHA256).Hash
if ($legacyHash -cne $publishedInnoInstallers[$LegacyVersion]) {
    throw "$LegacyInstallerPath hashes to $legacyHash, not the published TigerSqlCmd $LegacyVersion installer $($publishedInnoInstallers[$LegacyVersion])."
}

$lab = Resolve-TigerWinLabRoot -Path $TigerWinLabRoot
Write-Host "TigerWinLab resolved from $($lab.source): $($lab.root)"

$outputRoot = Join-Path $repoRoot "artifacts\lab\$version"
$payloadRoot = Join-Path $outputRoot 'payload'
if (Test-Path -LiteralPath $payloadRoot) { Remove-Item -LiteralPath $payloadRoot -Recurse -Force }
New-Item -ItemType Directory -Path $payloadRoot -Force | Out-Null
Copy-Item -LiteralPath $InstallerPath, $LegacyInstallerPath -Destination $payloadRoot
[ordered]@{
    version = $version
    candidate = [IO.Path]::GetFileName($InstallerPath)
    candidateDllSha256 = $candidateDllSha256
    legacy = $legacyName
    legacyVersion = $LegacyVersion
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $payloadRoot 'run.json') -Encoding utf8NoBOM
# The guest runs Windows PowerShell 5.1, which reads BOM-less UTF-8 as ANSI.
$guestScript = Join-Path $payloadRoot 'accept.ps1'
Get-Content -LiteralPath (Join-Path $PSScriptRoot 'installer\accept.ps1') -Raw -Encoding utf8 |
    Set-Content -LiteralPath $guestScript -Encoding utf8BOM -NoNewline
$env:TIGERQUERY_GUEST_SCRIPT = $guestScript
try {
    powershell.exe -NoProfile -NonInteractive -Command '$t = $null; $e = $null; [System.Management.Automation.Language.Parser]::ParseFile($env:TIGERQUERY_GUEST_SCRIPT, [ref]$t, [ref]$e) | Out-Null; if ($e.Count) { $e | Out-String | Write-Error; exit 1 }'
    if ($LASTEXITCODE -ne 0) { throw 'The guest script does not parse in Windows PowerShell 5.1.' }
}
finally { Remove-Item Env:TIGERQUERY_GUEST_SCRIPT }

# The Lab is a child process with its own result file; exit codes 0 OK, 1 failed, 2 BUSY, 3 TIMEOUT.
$resultPath = Join-Path $outputRoot 'lab-result.json'
Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
$arguments = @('-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $lab.root 'Invoke-TigerWinLabJob.ps1'),
    '-Name', 'tigersqlcmd-installer', '-PayloadPath', $payloadRoot, '-EntryScript', 'accept.ps1',
    '-Baseline', $Baseline, '-NetworkState', 'online', '-TimeoutMinutes', $TimeoutMinutes,
    '-OutputRoot', (Join-Path $outputRoot 'lab'), '-ResultPath', $resultPath)
Write-Host "Running the installer acceptance in TigerWinLab ($Baseline, timeout $TimeoutMinutes minutes)..."
$process = Start-Process -FilePath (Get-Process -Id $PID).Path -ArgumentList $arguments -PassThru -NoNewWindow `
    -RedirectStandardOutput (Join-Path $outputRoot 'lab.stdout.log') -RedirectStandardError (Join-Path $outputRoot 'lab.stderr.log')
# Headroom beyond the lab's own timeout: its teardown and baseline restore continue after it fires.
$outerTimeout = [TimeSpan]::FromMinutes($TimeoutMinutes + 30)
if (-not $process.WaitForExit([int] $outerTimeout.TotalMilliseconds)) {
    $process.Kill($true)
    throw "TigerWinLab did not finish within $($outerTimeout.TotalMinutes) minutes; its job was stopped."
}
if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
    Get-Content -LiteralPath (Join-Path $outputRoot 'lab.stderr.log') -ErrorAction SilentlyContinue | Write-Host
    throw "TigerWinLab exited with $($process.ExitCode) and wrote no result to $resultPath."
}
$result = Get-Content -LiteralPath $resultPath -Raw -Encoding utf8 | ConvertFrom-Json

$phases = @()
if ($null -ne $result.PSObject.Properties['result'] -and $null -ne $result.result -and $null -ne $result.result.PSObject.Properties['phases']) {
    $phases = @($result.result.phases)
}
foreach ($phase in $phases) {
    Write-Host ''
    Write-Host "[$($phase.status)] $($phase.name)"
    foreach ($check in @($phase.checks)) { Write-Host ("  {0,-4} {1,-34} {2}" -f $check.status, $check.code, $check.message) }
}
$failed = @($phases | ForEach-Object { @($_.checks) } | Where-Object { $_.status -ne 'PASS' })
$total = @($phases | ForEach-Object { @($_.checks) }).Count
Write-Host ''
Write-Host "Evidence: $(Join-Path $outputRoot 'lab')"
if ($process.ExitCode -ne 0 -or $result.status -ne 'OK' -or $phases.Count -eq 0 -or $failed.Count -gt 0) {
    Write-Host "FAIL: TigerWinLab status $($result.status), exit $($process.ExitCode), $($failed.Count) of $total check(s) failed: $($result.message)" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: TigerSqlCmd $version installer in TigerWinLab: $total checks across $($phases.Count) phases." -ForegroundColor Green
exit 0
