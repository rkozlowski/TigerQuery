<#
    .SYNOPSIS
    Provisions the pinned TigerSetup builder on a machine that does not have it - the release
    workflow's runner.

    .DESCRIPTION
    A developer machine has TigerSetup installed and on PATH. A GitHub-hosted runner does not, so
    the release build installs the one TigerSetup release ItTiger.TigerSqlCmd.Installer\tigersetup.json
    pins: the published installer is downloaded straight to a file, refused unless its SHA-256 is the
    pinned one, installed quietly per user into -Directory without the PATH option, and the installed
    tiger-setup.exe must report the pinned version. Its path goes to GITHUB_OUTPUT as `path`, which
    the workflow passes to BuildInstaller.ps1 and the scripts that inspect the installer.

    -VerifyOnly downloads and checks the installer and installs nothing: the local proof of the pin.

    The same mechanics provision TigerMarkView's release runner.

    .EXAMPLE
    pwsh -File eng\release-automation\Install-TigerSetup.ps1 -Directory $env:RUNNER_TEMP\tigersetup
#>
[CmdletBinding()]
param(
    [string] $Directory = (Join-Path ([IO.Path]::GetTempPath()) 'tigersetup'),
    [switch] $VerifyOnly,
    [string] $GitHubOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path (Join-Path $repoRoot 'ItTiger.TigerSqlCmd.Installer') 'TigerSetupBuilder.ps1')
$pin = Get-TigerSqlCmdTigerSetupPin -RepositoryRoot $repoRoot

$name = "TigerSetup-$($pin.version)-Setup.exe"
$download = Join-Path ([IO.Path]::GetTempPath()) "tigerquery-$([guid]::NewGuid().ToString('N'))-$name"
try {
    Write-Host "Downloading $($pin.installerUrl)"
    Invoke-WebRequest -Uri $pin.installerUrl -OutFile $download -UseBasicParsing
    $actual = (Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash
    if ($actual -ne [string] $pin.installerSha256) {
        throw "$name hashes to $actual, not the pinned $($pin.installerSha256); it is not installed."
    }
    Write-Host "$name matches the pinned SHA-256."
    if ($VerifyOnly) { exit 0 }

    $log = Join-Path ([IO.Path]::GetTempPath()) "tigersetup-$($pin.version)-install.log"
    $arguments = @('install', '--quiet', '--scope', 'user', '--install-root', $Directory,
        '--option', 'path', 'off', '--log', $log)
    # Start-Process joins its arguments with spaces; the two paths are quoted for that.
    $quoted = $arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $process = Start-Process -FilePath $download -ArgumentList $quoted -Wait -PassThru -NoNewWindow
    if ($process.ExitCode -ne 0) {
        $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 40) -join "`n" } else { '(no log)' }
        throw "The TigerSetup installer exited $($process.ExitCode).`n$tail"
    }
}
finally {
    Remove-Item -LiteralPath $download -Force -ErrorAction SilentlyContinue
}

$tigerSetup = Join-Path $Directory 'tiger-setup.exe'
if (-not (Test-Path -LiteralPath $tigerSetup -PathType Leaf)) { throw "The installation holds no $tigerSetup." }
$resolved = Resolve-TigerSqlCmdTigerSetup -RepositoryRoot $repoRoot -Path $tigerSetup
Write-Host "tiger-setup: $resolved ($($pin.version))"
if ($GitHubOutput) { "path=$resolved" | Out-File -LiteralPath $GitHubOutput -Encoding utf8 -Append }
exit 0
