[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [uri] $InstallerUri,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{64}$')]
    [string] $InstallerSha256
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'Inno Setup can only be provisioned on Windows.'
}

$innoKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 7_is1'

function Resolve-InnoSetupCompiler {
    param(
        [Parameter(Mandatory)]
        [string] $ExpectedVersion
    )

    $installation = Get-ItemProperty -LiteralPath $innoKey -ErrorAction SilentlyContinue
    if ($null -eq $installation -or
        [string] $installation.DisplayVersion -cne $ExpectedVersion -or
        [string]::IsNullOrWhiteSpace([string] $installation.InstallLocation)) {
        return $null
    }

    $compilerPath = Join-Path ([string] $installation.InstallLocation) 'ISCC.exe'
    if (-not (Test-Path -LiteralPath $compilerPath -PathType Leaf)) {
        return $null
    }

    return [IO.Path]::GetFullPath($compilerPath)
}

$compilerPath = Resolve-InnoSetupCompiler -ExpectedVersion $Version
if ($null -ne $compilerPath) {
    Write-Host "Using installed Inno Setup $Version compiler: $compilerPath"
    Write-Output $compilerPath
    return
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('TigerQuery-InnoSetup-' + [Guid]::NewGuid().ToString('N'))
$installerPath = Join-Path $temporaryRoot ([IO.Path]::GetFileName($InstallerUri.LocalPath))

try {
    New-Item -ItemType Directory -Path $temporaryRoot -Force | Out-Null

    Write-Host "Downloading Inno Setup $Version from $InstallerUri"
    try {
        Invoke-WebRequest -Uri $InstallerUri -OutFile $installerPath
    }
    catch {
        throw "Could not download the Inno Setup $Version installer from '$InstallerUri': $($_.Exception.Message)"
    }

    $actualSha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    if ($actualSha256 -cne $InstallerSha256.ToUpperInvariant()) {
        throw "Inno Setup $Version installer SHA-256 '$actualSha256' does not match expected SHA-256 '$($InstallerSha256.ToUpperInvariant())'."
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $installerPath
    $signerSubject = if ($null -eq $signature.SignerCertificate) {
        '<none>'
    }
    else {
        $signature.SignerCertificate.Subject
    }
    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
        $null -eq $signature.SignerCertificate -or
        $signerSubject -notlike 'CN=Pyrsys B.V.,*') {
        throw "Inno Setup $Version installer Authenticode validation failed: status '$($signature.Status)', signer '$signerSubject'."
    }

    Write-Host "Installing Inno Setup $Version silently."
    $process = Start-Process -FilePath $installerPath `
        -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/ALLUSERS') `
        -Wait -PassThru -WindowStyle Hidden
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup $Version installer failed with exit code $($process.ExitCode)."
    }

    $compilerPath = Resolve-InnoSetupCompiler -ExpectedVersion $Version
    if ($null -eq $compilerPath) {
        $installation = Get-ItemProperty -LiteralPath $innoKey -ErrorAction SilentlyContinue
        $foundVersion = if ($null -eq $installation) { '<not registered>' } else { [string] $installation.DisplayVersion }
        $foundLocation = if ($null -eq $installation) { '<not registered>' } else { [string] $installation.InstallLocation }
        throw "Inno Setup $Version installation completed, but ISCC.exe was not found for the exact registered version. Registered version: '$foundVersion'; install location: '$foundLocation'."
    }

    Write-Host "Provisioned Inno Setup $Version compiler: $compilerPath"
    Write-Output $compilerPath
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
