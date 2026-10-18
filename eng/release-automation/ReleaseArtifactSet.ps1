<#
    .SYNOPSIS
    The one definition of a TigerQuery release set. Dot-source this file.

    .DESCRIPTION
    Every file the release action records, in record order, with the kind the release record
    gives it: the four NuGet packages and their symbols, the TigerSqlCmd installer, its WinGet
    manifest set, and the terms that apply to exactly this release - LICENSE (License) and the
    TigerSqlCmd privacy statement PRIVACY.md (PrivacyStatement). New-ReleaseArtifactManifest.ps1
    writes the record from it and Assert-ReleaseArtifactManifest.ps1 proves a set against it.
#>

Set-StrictMode -Version Latest

function Get-TigerQueryReleaseArtifactSet {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string] $Version)

    foreach ($package in 'ItTiger.TigerQuery', 'ItTiger.TigerQuery.Core', 'ItTiger.TigerQuery.CliCore', 'ItTiger.TigerSqlCmd') {
        [pscustomobject]@{ name = "$package.$Version.nupkg"; kind = 'NuGetPackage' }
        [pscustomobject]@{ name = "$package.$Version.snupkg"; kind = 'NuGetSymbols' }
    }
    [pscustomobject]@{ name = "TigerSqlCmd-$Version-Setup.exe"; kind = 'WindowsInstaller' }
    [pscustomobject]@{ name = "TigerSqlCmd-$Version-WinGet.zip"; kind = 'WinGetManifests' }
    [pscustomobject]@{ name = 'LICENSE'; kind = 'License' }
    [pscustomobject]@{ name = 'PRIVACY.md'; kind = 'PrivacyStatement' }
}

# The three WinGet submission manifests the WinGetManifests archive holds, at its root.
$script:TigerSqlCmdWinGetManifestNames = @(
    'ItTiger.TigerSqlCmd.installer.yaml'
    'ItTiger.TigerSqlCmd.locale.en-US.yaml'
    'ItTiger.TigerSqlCmd.yaml'
)
