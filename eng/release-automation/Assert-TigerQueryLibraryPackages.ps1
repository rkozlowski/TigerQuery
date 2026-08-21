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

function Get-PackageReferences {
    param([Parameter(Mandatory)][string] $ProjectPath)

    [xml] $project = Get-Content -Raw -LiteralPath $ProjectPath
    $references = @{}
    foreach ($reference in @($project.SelectNodes('/Project/ItemGroup/PackageReference'))) {
        $id = $reference.GetAttribute('Include')
        $version = $reference.GetAttribute('Version')
        if ([string]::IsNullOrWhiteSpace($version)) {
            $versionNode = $reference.SelectSingleNode('Version')
            if ($null -ne $versionNode) { $version = $versionNode.InnerText }
        }
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($version)) {
            throw "Could not read a package reference from $ProjectPath."
        }
        $references[$id] = $version
    }
    return $references
}

$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$packages = @(
    @{ Id = 'ItTiger.TigerQuery.Core'; Project = 'ItTiger.TigerQuery.Core\ItTiger.TigerQuery.Core.csproj' }
    @{ Id = 'ItTiger.TigerQuery'; Project = 'ItTiger.TigerQuery\ItTiger.TigerQuery.csproj' }
    @{ Id = 'ItTiger.TigerQuery.CliCore'; Project = 'ItTiger.TigerQuery.CliCore\ItTiger.TigerQuery.CliCore.csproj' }
)

foreach ($package in $packages) {
    $id = $package.Id
    $packagePath = Join-Path $PackageDirectory "$id.$ExpectedVersion.nupkg"
    $symbolsPath = Join-Path $PackageDirectory "$id.$ExpectedVersion.snupkg"
    foreach ($path in @($packagePath, $symbolsPath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package: $path" }
    }

    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $packagePath))
    try {
        $nuspecEntry = $archive.GetEntry("$id.nuspec")
        if ($null -eq $nuspecEntry) { throw "$packagePath does not contain $id.nuspec." }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }

        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ([string] $metadata.id -cne $id -or [string] $metadata.version -cne $ExpectedVersion) {
            throw "$packagePath has unexpected package identity."
        }
        if ([string] $metadata.readme -cne 'README.md' -or $null -eq $archive.GetEntry('README.md')) {
            throw "$packagePath does not contain the configured root README.md."
        }
        if ([string] $metadata.icon -cne 'TigerQuery256.png' -or $null -eq $archive.GetEntry('TigerQuery256.png')) {
            throw "$packagePath does not contain the configured root icon."
        }
        foreach ($requiredPath in @("lib/net10.0/$id.dll", "lib/net10.0/$id.xml")) {
            if ($null -eq $archive.GetEntry($requiredPath)) { throw "$packagePath does not contain $requiredPath." }
        }

        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if (
            $null -eq $repository -or
            $repository.GetAttribute('url') -cne 'https://github.com/rkozlowski/TigerQuery' -or
            $repository.GetAttribute('commit') -cne $ExpectedRepositoryCommit
        ) {
            throw "$packagePath has unexpected repository URL or commit metadata."
        }

        $forbiddenAssemblies = @($archive.Entries | Where-Object {
            $_.FullName -match '(?i)(TigerSqlCmd|TigerQuery\.Tests).*\.dll$'
        })
        if ($forbiddenAssemblies.Count -ne 0) {
            throw "$packagePath contains forbidden assemblies: $($forbiddenAssemblies.FullName -join ', ')."
        }

        $dependencies = @($nuspec.SelectNodes("//*[local-name()='dependency']"))
        $forbiddenDependencies = @($dependencies | Where-Object {
            $_.GetAttribute('id') -match '(?i)NLog|Spectre|TigerSqlCmd|TigerQuery\.Tests'
        })
        if ($id -eq 'ItTiger.TigerQuery') {
            $forbiddenDependencies += @($dependencies | Where-Object {
                $_.GetAttribute('id') -match '(?i)TigerCli|TigerQuery\.CliCore'
            })
        }
        if ($forbiddenDependencies.Count -ne 0) {
            throw "$packagePath contains forbidden dependencies: $($forbiddenDependencies.id -join ', ')."
        }

        $expectedDependencies = Get-PackageReferences -ProjectPath (Join-Path $repositoryRoot $package.Project)
        if ($id -in @('ItTiger.TigerQuery', 'ItTiger.TigerQuery.CliCore')) {
            $expectedDependencies['ItTiger.TigerQuery.Core'] = $ExpectedVersion
        }
        if ($dependencies.Count -ne $expectedDependencies.Count) {
            throw "$packagePath has $($dependencies.Count) dependencies; expected $($expectedDependencies.Count)."
        }
        foreach ($expected in $expectedDependencies.GetEnumerator()) {
            $matches = @($dependencies | Where-Object { $_.GetAttribute('id') -ceq $expected.Key })
            if ($matches.Count -ne 1 -or $matches[0].GetAttribute('version') -cne $expected.Value) {
                throw "$packagePath does not contain exact dependency '$($expected.Key)' version '$($expected.Value)'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
    Write-Host "Validated $([IO.Path]::GetFileName($packagePath)) and $([IO.Path]::GetFileName($symbolsPath))."
}

