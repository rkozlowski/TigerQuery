[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [switch] $ValidateBehavior
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$installerSourcePath = Join-Path $repositoryRoot 'ItTiger.TigerSqlCmd.Installer\Installer.iss'
$InstallerPath = [IO.Path]::GetFullPath($InstallerPath)
$expectedName = "TigerSqlCmdSetup_$($ExpectedVersion -replace '\.', '_').exe"
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "Installer not found: $InstallerPath"
}
if ([IO.Path]::GetFileName($InstallerPath) -cne $expectedName) {
    throw "Expected installer filename '$expectedName'; found '$([IO.Path]::GetFileName($InstallerPath))'."
}

$source = Get-Content -LiteralPath $installerSourcePath -Raw
$requiredSourceContracts = @(
    '(?m)^AppId=ItTiger\.TigerSqlCmd\r?$'
    '(?m)^AppVersion=\{#TigerSqlCmdVersion\}\r?$'
    '(?m)^DefaultDirName=\{autopf\}\\ItTiger\\TigerSqlCmd\r?$'
    '(?m)^PrivilegesRequired=admin\r?$'
    '(?m)^ArchitecturesInstallIn64BitMode=x64compatible\r?$'
    '(?m)^OutputBaseFilename=\{#TigerSqlCmdOutputBaseFilename\}\r?$'
)
foreach ($contract in $requiredSourceContracts) {
    if ($source -notmatch $contract) {
        throw "Installer.iss does not satisfy release contract '$contract'."
    }
}

$versionParts = @($ExpectedVersion.Split('-')[0].Split('.'))
$expectedFileVersion = (@($versionParts) + @('0', '0', '0', '0'))[0..3] -join '.'
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($InstallerPath)
if ($versionInfo.FileVersion.Trim() -cne $expectedFileVersion) {
    throw "Installer file version '$($versionInfo.FileVersion)' does not match '$expectedFileVersion'."
}
if ($versionInfo.FileDescription.Trim() -cne 'TigerSqlCmd installer') {
    throw "Installer description '$($versionInfo.FileDescription)' is unexpected."
}

Write-Host "Validated installer identity, machine-wide model, and file metadata for $expectedName."
if (-not $ValidateBehavior) { return }

if ($ExpectedVersion.Contains('-')) {
    throw 'Behavior validation currently supports stable numeric installer versions only.'
}
if (-not $IsWindows) {
    throw 'Installer behavior validation requires Windows.'
}
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Installer behavior validation requires an elevated Windows runner.'
}

$installDirectory = Join-Path $env:ProgramFiles 'ItTiger\TigerSqlCmd'
$cliDirectory = Join-Path $installDirectory 'cli'
$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ItTiger.TigerSqlCmd_is1'
if (Test-Path -LiteralPath $uninstallKey) {
    throw "Refusing to disturb a pre-existing TigerSqlCmd installation at $uninstallKey."
}

$installedByThisRun = $false
try {
    foreach ($operation in @('clean install', 'reinstall')) {
        $process = Start-Process -FilePath $InstallerPath -Wait -PassThru -ArgumentList @(
            '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-'
        )
        if ($process.ExitCode -ne 0) {
            throw "$operation failed with exit code $($process.ExitCode)."
        }
        $installedByThisRun = $true
    }

    if (-not (Test-Path -LiteralPath $uninstallKey)) {
        throw "Expected machine-wide uninstall product code was not registered: $uninstallKey"
    }
    $uninstallMetadata = Get-ItemProperty -LiteralPath $uninstallKey
    if ($uninstallMetadata.DisplayVersion -cne $ExpectedVersion -or $uninstallMetadata.Publisher -cne 'IT Tiger') {
        throw 'Installed version or publisher metadata is unexpected.'
    }
    $versionFile = Join-Path $installDirectory 'VERSION.txt'
    if ((Get-Content -LiteralPath $versionFile -Raw).Trim() -cne $ExpectedVersion) {
        throw 'Installed VERSION.txt does not match the release version.'
    }

    $machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
    $pathMatches = @($machinePath -split ';' | Where-Object {
        $_.TrimEnd('\') -ieq $cliDirectory.TrimEnd('\')
    })
    if ($pathMatches.Count -ne 1) {
        throw "Expected one machine PATH entry for '$cliDirectory'; found $($pathMatches.Count)."
    }

    $command = Join-Path $cliDirectory 'tiger-sqlcmd.exe'
    $versionOutput = & $command --version | Out-String
    if ($LASTEXITCODE -ne 0 -or $versionOutput -notmatch [regex]::Escape($ExpectedVersion)) {
        throw 'Installed tiger-sqlcmd did not report the expected version.'
    }
    $helpOutput = & $command --help | Out-String
    if ($LASTEXITCODE -ne 0 -or -not $helpOutput.Contains('Usage:')) {
        throw 'Installed tiger-sqlcmd help validation failed.'
    }
}
finally {
    if ($installedByThisRun -and (Test-Path -LiteralPath $uninstallKey)) {
        $uninstaller = Join-Path $installDirectory 'unins000.exe'
        if (Test-Path -LiteralPath $uninstaller) {
            $uninstall = Start-Process -FilePath $uninstaller -Wait -PassThru -ArgumentList @(
                '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
            )
            if ($uninstall.ExitCode -ne 0) {
                throw "Silent uninstall failed with exit code $($uninstall.ExitCode)."
            }
        }
    }
}

if (Test-Path -LiteralPath $installDirectory) { throw "Installation directory remains: $installDirectory" }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Machine uninstall registration remains after uninstall.' }
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
if ($machinePath -split ';' | Where-Object { $_.TrimEnd('\') -ieq $cliDirectory.TrimEnd('\') }) {
    throw 'Machine PATH entry remains after uninstall.'
}
Write-Host 'Validated clean install, reinstall, command execution, PATH identity, and uninstall.'

