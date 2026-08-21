[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ExpectedRepositoryCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = @(
    @{ Id = 'ItTiger.TigerQuery.Core'; RequiredPdb = 'lib/net10.0/ItTiger.TigerQuery.Core.pdb' }
    @{ Id = 'ItTiger.TigerQuery'; RequiredPdb = 'lib/net10.0/ItTiger.TigerQuery.pdb' }
    @{ Id = 'ItTiger.TigerQuery.CliCore'; RequiredPdb = 'lib/net10.0/ItTiger.TigerQuery.CliCore.pdb' }
    @{ Id = 'ItTiger.TigerSqlCmd'; RequiredPdb = 'tools/net10.0/any/tiger-sqlcmd.pdb' }
)

foreach ($package in $packages) {
    $id = $package.Id
    $path = Join-Path $PackageDirectory "$id.$ExpectedVersion.snupkg"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing symbol package: $path" }
    if ([IO.Path]::GetFileName($path) -cne "$id.$ExpectedVersion.snupkg") {
        throw "Unexpected symbol package filename: $path"
    }

    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path))
    try {
        $nuspecEntry = $archive.GetEntry("$id.nuspec")
        if ($null -eq $nuspecEntry) { throw "$path does not contain $id.nuspec." }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ([string] $metadata.id -cne $id -or [string] $metadata.version -cne $ExpectedVersion) {
            throw "$path has unexpected symbol package identity."
        }
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if (
            $null -eq $repository -or
            $repository.GetAttribute('url') -cne 'https://github.com/rkozlowski/TigerQuery' -or
            $repository.GetAttribute('commit') -cne $ExpectedRepositoryCommit
        ) {
            throw "$path has unexpected repository URL or commit metadata."
        }
        if ($null -eq $archive.GetEntry($package.RequiredPdb)) {
            throw "$path does not contain required symbols at '$($package.RequiredPdb)'."
        }
        $forbidden = @($archive.Entries | Where-Object {
            $_.FullName -match '(?i)(TigerQuery\.Tests|testhost|\.dll$|\.exe$|\.deps\.json$|\.runtimeconfig\.json$|connections\.json$)'
        })
        if ($forbidden.Count -ne 0) {
            throw "$path contains binary, test, or connection-store payloads: $($forbidden.FullName -join ', ')."
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "Validated symbol package $([IO.Path]::GetFileName($path))."
}

