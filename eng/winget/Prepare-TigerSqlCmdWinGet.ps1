#Requires -Version 7.0
<#
    .SYNOPSIS
    Generates the TigerSqlCmd WinGet manifest set from an installer.

    .DESCRIPTION
    The manifests are TigerSetup's: `tiger-setup winget prepare` writes them from
    ItTiger.TigerSqlCmd.Installer\TigerSetup.toml and the installer's bytes, resolving the {version}
    token of the declared licence, privacy and release-notes URLs, and `tiger-setup winget finalize`
    fills in the immutable release URL and the installer's hash. This script pins the inputs - the
    builder tigersetup.json names, the version, the file name, the URL - and refuses the result
    unless every manifest check in TigerSqlCmdWinGet.psm1 passes and the set is exactly the three
    submission manifests.

    The release workflow runs it over the installer it just built; that set is the one recorded in
    the release and submitted. A set generated locally hashes a local build, which is never the
    released installer.

    .PARAMETER InstallerPath
    The installer to describe. Defaults to ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmd-<version>-Setup.exe.

    .PARAMETER OutputRoot
    The root the manifests\i\ItTiger\TigerSqlCmd\<version> directory is created under. Defaults to
    artifacts\winget.

    .PARAMETER ExpectedVersion
    When supplied, the version Version.props must already be at.

    .PARAMETER InstallerUrl
    The immutable release asset URL. Defaults to, and must equal, the v<version> URL.

    .PARAMETER ExpectedInstallerSha256
    When supplied, the digest the installer must hash to.

    .PARAMETER TigerSetupPath
    tiger-setup.exe; defaults to the one on PATH. Must be the pinned version.

    .PARAMETER Validate
    Also runs winget validate over the generated set. The release runner has no WinGet client, so
    the workflow omits it; preparation and the TigerWinLab scenario run it.
#>
[CmdletBinding()]
param(
    [string] $InstallerPath,
    [string] $OutputRoot,
    [string] $ExpectedVersion,
    [string] $InstallerUrl,
    [string] $ExpectedInstallerSha256,
    [string] $TigerSetupPath,
    [switch] $Validate
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $PSScriptRoot 'TigerSqlCmdWinGet.psm1') -Force
. (Join-Path $repositoryRoot 'ItTiger.TigerSqlCmd.Installer' 'TigerSetupBuilder.ps1')

[xml] $versionXml = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Version.props')
$version = [string] $versionXml.Project.PropertyGroup.Version
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $version -cne $ExpectedVersion) {
    throw "Version.props version '$version' does not match expected version '$ExpectedVersion'."
}
$release = Get-TigerSqlCmdWinGetReleaseFact -Version $version

if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repositoryRoot "ItTiger.TigerSqlCmd.Installer\Output\$($release.installerFileName)"
}
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer not found: $InstallerPath"
}
if ([IO.Path]::GetFileName($InstallerPath) -cne $release.installerFileName) {
    throw "Expected installer filename '$($release.installerFileName)'; found '$([IO.Path]::GetFileName($InstallerPath))'."
}
if ([string]::IsNullOrWhiteSpace($InstallerUrl)) { $InstallerUrl = $release.installerUrl }
if ($InstallerUrl -cne $release.installerUrl) {
    throw "Installer URL must be the immutable release URL '$($release.installerUrl)'."
}
$installerSha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
if (-not [string]::IsNullOrWhiteSpace($ExpectedInstallerSha256) -and $installerSha256 -cne $ExpectedInstallerSha256.ToUpperInvariant()) {
    throw "Installer SHA-256 '$installerSha256' does not match '$ExpectedInstallerSha256'."
}

$tigerSetup = Resolve-TigerSqlCmdTigerSetup -RepositoryRoot $repositoryRoot -Path $TigerSetupPath

if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repositoryRoot 'artifacts\winget' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$manifestDirectory = Join-Path $OutputRoot ($release.submissionPath -replace '/', '\')
if (Test-Path -LiteralPath $manifestDirectory) {
    $safeRoot = $OutputRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($manifestDirectory)
    if (-not $resolved.StartsWith($safeRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace manifests outside '$OutputRoot'."
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null

$manifest = Join-Path $repositoryRoot 'ItTiger.TigerSqlCmd.Installer\TigerSetup.toml'
& $tigerSetup winget prepare $manifest --installer $InstallerPath --output $manifestDirectory | Out-Host
if ($LASTEXITCODE -ne 0) { throw "tiger-setup winget prepare failed with exit code $LASTEXITCODE." }
& $tigerSetup winget finalize $manifestDirectory --url $InstallerUrl --installer $InstallerPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "tiger-setup winget finalize failed with exit code $LASTEXITCODE." }

$manifestSet = Read-TigerSqlCmdWinGetManifestSet -ManifestDirectory $manifestDirectory -Version $version
$checks = @(Test-TigerSqlCmdWinGetManifestSet -ManifestSet $manifestSet)
if ($manifestSet.installer.installerSha256 -cne $installerSha256) {
    $checks += New-TigerSqlCmdWinGetCheck -Name 'manifest/installer-sha256' -Status 'FAIL' `
        -Message "InstallerSha256 is '$($manifestSet.installer.installerSha256)'; the installer hashes to '$installerSha256'."
}
foreach ($check in $checks) {
    Write-Host ('{0,-5} {1,-32} {2}' -f $check.status, $check.name, $check.message)
}
$failed = @($checks | Where-Object status -ceq 'FAIL')
if ($failed.Count -gt 0) {
    throw "The generated WinGet manifests fail $($failed.Count) check(s): $(@($failed | ForEach-Object name) -join ', ')."
}

if ($Validate) {
    Write-Host 'Validating manifests with WinGet...'
    & winget validate --manifest $manifestDirectory --disable-interactivity
    if ($LASTEXITCODE -ne 0) {
        throw "winget validate failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Prepared WinGet manifests: $manifestDirectory" -ForegroundColor Green
Write-Host "Installer URL: $InstallerUrl"
Write-Host "Installer SHA-256: $installerSha256"
