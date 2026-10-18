<#
.SYNOPSIS
    Publishes tiger-sqlcmd and builds its TigerSetup installer.

.DESCRIPTION
    One repeatable command:

        pwsh ItTiger.TigerSqlCmd.Installer\BuildInstaller.ps1

    Produces TigerSqlCmd-<version>-Setup.exe, where <version> comes from Version.props. The CLI is
    published into the ignored WorkingDir\payload\ together with LICENSE (as LICENSE.txt),
    THIRD-PARTY-NOTICES.md, PRIVACY.md and the Start Menu shell script, and tiger-setup builds
    TigerSetup.toml from it; its [metadata] check then refuses a tiger-sqlcmd.exe that disagrees
    with the project. tigersetup.json pins the builder.

.PARAMETER ExpectedVersion
    When supplied, the version Version.props must already be at.

.PARAMETER OutputDirectory
    Where the installer is written. Defaults to ItTiger.TigerSqlCmd.Installer\Output.

.PARAMETER NoBuild
    Publish the already-built Release output without compiling or restoring. Release automation uses
    this after its single solution build, so the exact built binaries are packaged.

.PARAMETER TigerSetupPath
    Full path to tiger-setup.exe. Defaults to tiger-setup.exe on PATH. Its version must be the one
    tigersetup.json pins, so a local build and the release build use the same builder.

.PARAMETER Fast
    tiger-setup build --fast: functionally identical, larger, much quicker. For iteration only.
#>
[CmdletBinding()]
param(
    [string] $ExpectedVersion,
    [string] $OutputDirectory,
    [switch] $NoBuild,
    [string] $TigerSetupPath,
    [switch] $Fast
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'TigerSetupBuilder.ps1')

$projectRoot = Split-Path -Parent $PSScriptRoot
$cliProject = Join-Path $projectRoot 'ItTiger.TigerSqlCmd\ItTiger.TigerSqlCmd.csproj'
$manifest = Join-Path $PSScriptRoot 'TigerSetup.toml'
$workingDir = Join-Path $PSScriptRoot 'WorkingDir'
$payloadDir = Join-Path $workingDir 'payload'

[xml] $versionXml = Get-Content -LiteralPath (Join-Path $projectRoot 'Version.props')
$version = [string] $versionXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'Could not read Version from Version.props.'
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion) -and $version -cne $ExpectedVersion) {
    throw "Version.props version '$version' does not match expected version '$ExpectedVersion'."
}

$tigerSetup = Resolve-TigerSqlCmdTigerSetup -RepositoryRoot $projectRoot -Path $TigerSetupPath
Write-Host "`nBuilding the TigerSqlCmd $version installer with $tigerSetup..." -ForegroundColor Cyan

# A stale file left in the staging directory would be packaged as though it belonged there.
if (Test-Path -LiteralPath $workingDir) {
    Remove-Item -LiteralPath $workingDir -Recurse -Force
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null

Write-Host "Publishing tiger-sqlcmd to $payloadDir..."
$publishArguments = @('publish', $cliProject, '-c', 'Release', '-o', $payloadDir, '-m:1', '/nodeReuse:false')
if ($NoBuild) {
    $publishArguments += @('--no-build', '--no-restore')
}
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The installer's one prerequisite is the base .NET runtime of TigerSetup.toml's
# Microsoft.DotNet.Runtime.<major> dependency; a publish that needs anything else is refused.
$runtimeConfigFile = Join-Path $payloadDir 'tiger-sqlcmd.runtimeconfig.json'
$runtimeOptions = (Get-Content -LiteralPath $runtimeConfigFile -Raw | ConvertFrom-Json).runtimeOptions
$frameworks = @(@($runtimeOptions.PSObject.Properties | Where-Object Name -in 'framework', 'frameworks' | ForEach-Object Value) |
    ForEach-Object { $_ } | Where-Object { $_ })
if ($frameworks.Count -ne 1 -or $frameworks[0].name -cne 'Microsoft.NETCore.App') {
    $found = ($frameworks | ForEach-Object { "$($_.name) $($_.version)" }) -join ', '
    throw "Expected a single Microsoft.NETCore.App framework reference in $runtimeConfigFile; found: $found."
}
$runtimeMajor = ([string] $frameworks[0].version).Split('.')[0]
$declaredDependency = "id = `"Microsoft.DotNet.Runtime.$runtimeMajor`""
if (-not (Get-Content -LiteralPath $manifest -Raw).Contains($declaredDependency)) {
    throw "tiger-sqlcmd needs Microsoft.NETCore.App $($frameworks[0].version), but TigerSetup.toml does not declare $declaredDependency."
}
Write-Host "Required runtime: Microsoft.NETCore.App $($frameworks[0].version) (Microsoft.DotNet.Runtime.$runtimeMajor)"

$cliVersion = (Get-Item -LiteralPath (Join-Path $payloadDir 'tiger-sqlcmd.exe')).VersionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($cliVersion) -or $cliVersion.Split('+')[0] -cne $version) {
    throw "Published tiger-sqlcmd.exe ProductVersion '$cliVersion' does not match '$version'."
}

# Every third-party package the publish ships has its row, at the shipped version, in the
# notices; a dependency change that forgot them fails here rather than in a release.
$notices = Get-Content -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Raw
$deps = Get-Content -LiteralPath (Join-Path $payloadDir 'tiger-sqlcmd.deps.json') -Raw | ConvertFrom-Json
$unlisted = @(foreach ($library in $deps.libraries.PSObject.Properties) {
    $id, $libraryVersion = $library.Name.Split('/')
    if ($library.Value.type -ceq 'package' -and $id -notlike 'ItTiger.*' -and
        -not $notices.Contains("| $id | $libraryVersion |")) { $library.Name }
})
if ($unlisted.Count -gt 0) {
    throw "THIRD-PARTY-NOTICES.md does not list these shipped packages at their shipped versions: $($unlisted -join ', ')."
}

# The terms that ship with the program: the licence, the third-party notices and the privacy
# statement - the same files the release publishes as its License and PrivacyStatement.
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination (Join-Path $payloadDir 'LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $payloadDir
Copy-Item -LiteralPath (Join-Path $projectRoot 'PRIVACY.md') -Destination $payloadDir
New-Item -ItemType Directory -Path (Join-Path $payloadDir 'shell') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'shell\tiger-sqlcmd-shell.cmd') -Destination (Join-Path $payloadDir 'shell')

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'Output'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installerPath = Join-Path $OutputDirectory "TigerSqlCmd-$version-Setup.exe"
# Removed first, so a failed build can never leave an earlier installer looking like this one.
Remove-Item -LiteralPath $installerPath -Force -ErrorAction SilentlyContinue

$buildArguments = @('build', $manifest, '--output', $installerPath)
if ($Fast) { $buildArguments += '--fast' }
& $tigerSetup @buildArguments
if ($LASTEXITCODE -ne 0) { throw "tiger-setup build failed with exit code $LASTEXITCODE." }
if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not produced: $installerPath"
}

& $tigerSetup verify $installerPath
if ($LASTEXITCODE -ne 0) { throw "tiger-setup verify rejected '$installerPath' (exit code $LASTEXITCODE)." }

Write-Host "`nTigerSqlCmd installer built successfully: $installerPath" -ForegroundColor Green
