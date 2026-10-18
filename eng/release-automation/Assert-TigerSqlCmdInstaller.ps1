#Requires -Version 7.0
<#
    .SYNOPSIS
    Checks a built TigerSqlCmd installer without running it.

    .DESCRIPTION
    The installer is a TigerSetup Setup.exe, so what it will do is declared in its embedded
    metadata. `tiger-setup verify` proves the bytes are intact, and `tiger-setup inspect --json` is
    read for the product's contract:

      - identity: ItTiger.TigerSqlCmd, TigerSqlCmd, IT Tiger, the expected version, x64,
        machine-wide only, and the Add/Remove Programs key;
      - content: tiger-sqlcmd, its runtime files, LICENSE.txt, THIRD-PARTY-NOTICES.md and
        PRIVACY.md byte for byte as this repository holds them, and no .pdb or .xml file;
      - integration: the PATH option on by default, the Start Menu shell, the Inno Setup
        installation it replaces, the .NET runtime prerequisite, and no custom action.

    Installation behaviour - the migration from 0.8.8, install, upgrade, uninstall, PATH, the
    connection store and its protected passwords - is proven on the exact bytes in TigerWinLab
    (eng\lab\Test-TigerSqlCmdInstaller.ps1), never on the machine running this check.

    .PARAMETER TigerSetupPath
    tiger-setup.exe; defaults to the one on PATH. Must be the version tigersetup.json pins.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [string] $TigerSetupPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $repoRoot 'ItTiger.TigerSqlCmd.Installer' 'TigerSetupBuilder.ps1')

$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw "Installer not found: $InstallerPath" }
$expectedName = "TigerSqlCmd-$ExpectedVersion-Setup.exe"
if ([IO.Path]::GetFileName($InstallerPath) -cne $expectedName) { throw "Installer file name must be '$expectedName'." }

$tigerSetup = Resolve-TigerSqlCmdTigerSetup -RepositoryRoot $repoRoot -Path $TigerSetupPath

& $tigerSetup verify $InstallerPath | Out-Host
if ($LASTEXITCODE -ne 0) { throw "tiger-setup verify rejected '$InstallerPath' (exit code $LASTEXITCODE)." }

$json = & $tigerSetup inspect $InstallerPath --json
if ($LASTEXITCODE -ne 0) { throw "tiger-setup inspect failed for '$InstallerPath' (exit code $LASTEXITCODE)." }
$inspection = ($json -join "`n") | ConvertFrom-Json

$failures = [Collections.Generic.List[string]]::new()
function Assert-That([bool] $Condition, [string] $Message) {
    if (-not $Condition) { $failures.Add($Message) }
}

$package = $inspection.package
Assert-That ($package.id -ceq 'ItTiger.TigerSqlCmd') "package id is '$($package.id)'."
Assert-That ($package.name -ceq 'TigerSqlCmd') "package name is '$($package.name)'."
Assert-That ($package.publisher -ceq 'IT Tiger') "package publisher is '$($package.publisher)'."
Assert-That ($package.version -ceq $ExpectedVersion) "package version is '$($package.version)', not '$ExpectedVersion'."
Assert-That ($package.architecture -ceq 'x64') "package architecture is '$($package.architecture)'."
Assert-That ((@($package.scopes) -join ',') -ceq 'machine') "scopes are '$(@($package.scopes) -join ',')', not machine-wide only."
Assert-That ($package.install_roots.machine -ceq '%PROGRAMFILES%\TigerSqlCmd') "the machine install root is '$($package.install_roots.machine)'."
Assert-That ($inspection.registration.key_name -ceq 'ItTiger.TigerSqlCmd') "registration key is '$($inspection.registration.key_name)'."

# inspect reports install-relative paths with forward slashes.
$paths = @($inspection.files | ForEach-Object { ([string] $_.path).Replace('/', '\') })
foreach ($required in 'tiger-sqlcmd.exe', 'tiger-sqlcmd.dll', 'tiger-sqlcmd.runtimeconfig.json', 'tiger-sqlcmd.deps.json',
    'ItTiger.TigerQuery.dll', 'ItTiger.TigerQuery.Core.dll', 'ItTiger.TigerQuery.CliCore.dll', 'Microsoft.Data.SqlClient.dll',
    'LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md', 'shell\tiger-sqlcmd-shell.cmd') {
    Assert-That ($paths -ccontains $required) "the payload has no '$required'."
}
$excluded = @($paths | Where-Object { $_ -match '\.(pdb|xml)$' })
Assert-That ($excluded.Count -eq 0) "the payload carries debug symbols or XML documentation: $($excluded -join ', ')."

# The terms installed with the program are the ones the release publishes: each payload file must
# be this repository's file, byte for byte. inspect lists no file hashes, so the payload is
# exported as a ZIP and the entries hashed.
$terms = [ordered]@{
    'LICENSE.txt' = 'LICENSE'
    'THIRD-PARTY-NOTICES.md' = 'THIRD-PARTY-NOTICES.md'
    'PRIVACY.md' = 'PRIVACY.md'
}
$payloadZip = Join-Path ([IO.Path]::GetTempPath()) ('TigerSqlCmd-payload-' + [Guid]::NewGuid().ToString('N') + '.zip')
$installedHashes = @{}
try {
    & $tigerSetup inspect $InstallerPath --output-zip $payloadZip | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "tiger-setup inspect --output-zip failed for '$InstallerPath' (exit code $LASTEXITCODE)." }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payload = [IO.Compression.ZipFile]::OpenRead($payloadZip)
    try {
        foreach ($name in $terms.Keys) {
            $entry = @($payload.Entries | Where-Object { $_.FullName.Replace('\', '/') -ceq $name })
            if ($entry.Count -eq 1) {
                $stream = $entry[0].Open()
                try { $installedHashes[$name] = (Get-FileHash -InputStream $stream -Algorithm SHA256).Hash.ToLowerInvariant() }
                finally { $stream.Dispose() }
            }
        }
    }
    finally {
        $payload.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
}
foreach ($name in $terms.Keys) {
    $sourceSha256 = (Get-FileHash -LiteralPath (Join-Path $repoRoot $terms[$name]) -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-That ($installedHashes[$name] -ceq $sourceSha256) `
        "the payload's $name ($($installedHashes[$name])) is not this repository's $($terms[$name]) ($sourceSha256)."
}

$pathOption = @($inspection.options | Where-Object { $_.name -ceq 'path' })
Assert-That ($pathOption.Count -eq 1 -and [string] $pathOption[0].default -eq 'True') 'the PATH option is not declared on by default.'
$pathEntries = @($inspection.path_entries)
Assert-That ($pathEntries.Count -eq 1 -and [string]::IsNullOrEmpty([string] $pathEntries[0].path) -and $pathEntries[0].option -ceq 'path') `
    'the install root is not the one PATH entry, gated by the PATH option.'
$startMenu = @($inspection.shortcuts | Where-Object { $_.location -ceq 'start-menu' })
Assert-That ($startMenu.Count -eq 1 -and $startMenu[0].name -ceq 'TigerSqlCmd' -and $startMenu[0].target -ceq 'shell/tiger-sqlcmd-shell.cmd') `
    'there is not exactly one Start Menu shortcut, TigerSqlCmd, to the shell script.'
Assert-That (@($inspection.actions).Count -eq 0) 'the installer declares a custom action.'

Assert-That ($inspection.legacy.installer_type -ceq 'inno' -and $inspection.legacy.registration_key -ceq 'ItTiger.TigerSqlCmd_is1') `
    'the Inno Setup installation it replaces is not declared.'
$dependencies = @($inspection.dependencies)
Assert-That ($dependencies.Count -eq 1 -and $dependencies[0].id -ceq 'Microsoft.DotNet.Runtime.10' -and
    $dependencies[0].detect.path -ceq '%PROGRAMFILES%\dotnet\shared\Microsoft.NETCore.App' -and $dependencies[0].minimum_version -ceq '10.0') `
    "dependencies are '$(@($dependencies | ForEach-Object id) -join ',')', not the .NET 10 runtime alone."

if ($failures.Count -gt 0) {
    throw "Installer '$InstallerPath' does not meet the TigerSqlCmd contract:`n  - $($failures -join "`n  - ")"
}
Write-Host "PASS: $expectedName verifies and declares the TigerSqlCmd $ExpectedVersion installation contract." -ForegroundColor Green
