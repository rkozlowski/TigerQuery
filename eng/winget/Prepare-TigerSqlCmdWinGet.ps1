[CmdletBinding()]
param(
    [string]$InstallerPath,
    [string]$OutputRoot,
    [string]$ExpectedVersion,
    [string]$InstallerUrl,
    [string]$ExpectedInstallerSha256,
    [switch]$Validate
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$versionFile = Join-Path $repositoryRoot 'Version.props'
[xml]$versionXml = Get-Content -LiteralPath $versionFile
$version = [string]$versionXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read Version from $versionFile."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $version -cne $ExpectedVersion) {
    throw "Version.props version '$version' does not match expected version '$ExpectedVersion'."
}

$filenameVersion = $version -replace '\.', '_'
if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    $InstallerPath = Join-Path $repositoryRoot (
        "ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmdSetup_${filenameVersion}.exe")
}
$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer not found: $InstallerPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\winget'
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

$packageIdentifier = 'ItTiger.TigerSqlCmd'
$manifestDirectory = Join-Path $OutputRoot (
    "manifests\i\ItTiger\TigerSqlCmd\$version")
if (Test-Path -LiteralPath $manifestDirectory) {
    $resolvedOutputRoot = $OutputRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    ) + [IO.Path]::DirectorySeparatorChar
    $resolvedManifestDirectory = [IO.Path]::GetFullPath($manifestDirectory)
    if (-not $resolvedManifestDirectory.StartsWith($resolvedOutputRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean manifest directory outside $OutputRoot."
    }
    Remove-Item -LiteralPath $resolvedManifestDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $manifestDirectory -Force | Out-Null

$installerFileName = Split-Path -Leaf $InstallerPath
$expectedInstallerFileName = "TigerSqlCmdSetup_${filenameVersion}.exe"
if ($installerFileName -cne $expectedInstallerFileName) {
    throw "Expected installer filename '$expectedInstallerFileName'; found '$installerFileName'."
}

$expectedInstallerUrl = "https://github.com/rkozlowski/TigerQuery/releases/download/v$version/$installerFileName"
if ([string]::IsNullOrWhiteSpace($InstallerUrl)) {
    $InstallerUrl = $expectedInstallerUrl
}
if ($InstallerUrl -cne $expectedInstallerUrl) {
    throw "Installer URL must be the immutable expected release URL '$expectedInstallerUrl'."
}
$installerSha256 = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
if (
    -not [string]::IsNullOrWhiteSpace($ExpectedInstallerSha256) -and
    $installerSha256 -cne $ExpectedInstallerSha256.ToUpperInvariant()
) {
    throw "Installer SHA-256 '$installerSha256' does not match '$ExpectedInstallerSha256'."
}

$installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.12.0.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $version
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
# TigerSqlCmd is framework-dependent: WinGet installs the .NET runtime dependency
# first; the installer itself also verifies it and fails clearly if missing
# (it does not auto-install in silent mode without /INSTALLDOTNET).
Dependencies:
  PackageDependencies:
  - PackageIdentifier: Microsoft.DotNet.Runtime.10
Commands:
- tiger-sqlcmd
AppsAndFeaturesEntries:
- DisplayName: TigerSqlCmd $version
  Publisher: IT Tiger
  DisplayVersion: $version
  ProductCode: ItTiger.TigerSqlCmd_is1
  InstallerType: inno
Installers:
- Architecture: x64
  InstallerUrl: $InstallerUrl
  InstallerSha256: $installerSha256
ManifestType: installer
ManifestVersion: 1.12.0
"@

$localeManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.12.0.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $version
PackageLocale: en-US
Publisher: IT Tiger
PublisherUrl: https://www.ittiger.net/
PublisherSupportUrl: https://github.com/rkozlowski/TigerQuery/issues
PackageName: TigerSqlCmd
PackageUrl: https://www.ittiger.net/projects/tigerquery/
License: MIT
LicenseUrl: https://github.com/rkozlowski/TigerQuery/blob/main/LICENSE
Copyright: Copyright (c) 2024-2026 IT Tiger
ShortDescription: SQL Server script and query runner built on the TigerQuery engine.
Description: |-
  TigerSqlCmd is a SQL Server command-line client built on TigerQuery. It runs inline
  queries and SQL files, supports sqlcmd-compatible scripting and protected SqlCmdEx
  variables, and manages reusable connection profiles for interactive and unattended use.
Moniker: tiger-sqlcmd
Tags:
- cli
- database
- dotnet
- sql
- sqlcmd
- sql-server
ReleaseNotesUrl: https://github.com/rkozlowski/TigerQuery/releases/tag/v$version
ManifestType: defaultLocale
ManifestVersion: 1.12.0
"@

$versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.12.0.schema.json
PackageIdentifier: $packageIdentifier
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.12.0
"@

Set-Content -LiteralPath (Join-Path $manifestDirectory "$packageIdentifier.installer.yaml") `
    -Value $installerManifest -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $manifestDirectory "$packageIdentifier.locale.en-US.yaml") `
    -Value $localeManifest -Encoding utf8NoBOM
Set-Content -LiteralPath (Join-Path $manifestDirectory "$packageIdentifier.yaml") `
    -Value $versionManifest -Encoding utf8NoBOM

Write-Host "Prepared WinGet manifests: $manifestDirectory" -ForegroundColor Green
Write-Host "Installer URL: $InstallerUrl"
Write-Host "Installer SHA-256: $installerSha256"

if ($Validate) {
    Write-Host 'Validating manifests with WinGet...'
    & winget validate --manifest $manifestDirectory
    if ($LASTEXITCODE -ne 0) {
        throw "winget validate failed with exit code $LASTEXITCODE."
    }
}
