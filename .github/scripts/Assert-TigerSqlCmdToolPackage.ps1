[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackagePath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion,

    [string] $ExpectedRepositoryCommit
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageId = 'ItTiger.TigerSqlCmd'
$toolFramework = 'net10.0'
$toolDirectory = "tools/$toolFramework/any"
$expectedFileName = "$packageId.$ExpectedVersion.nupkg"

if (-not (Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw "Missing tool package: $PackagePath"
}

$resolvedPackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
if ([IO.Path]::GetFileName($resolvedPackagePath) -cne $expectedFileName) {
    throw "Tool package file name must be '$expectedFileName', not '$([IO.Path]::GetFileName($resolvedPackagePath))'."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)
try {
    function Read-ArchiveEntry {
        param(
            [Parameter(Mandatory)]
            [IO.Compression.ZipArchiveEntry] $Entry
        )

        $reader = [IO.StreamReader]::new($Entry.Open())
        try {
            return $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }

    $nuspecEntry = $archive.GetEntry("$packageId.nuspec")
    if ($null -eq $nuspecEntry) {
        throw "$PackagePath does not contain $packageId.nuspec."
    }

    [xml] $nuspec = Read-ArchiveEntry -Entry $nuspecEntry
    $metadata = $nuspec.SelectSingleNode(
        "/*[local-name()='package']/*[local-name()='metadata']"
    )
    if ($null -eq $metadata) {
        throw "$PackagePath does not contain NuGet package metadata."
    }

    $id = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
    $version = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
    if ($id -cne $packageId -or $version -cne $ExpectedVersion) {
        throw "$PackagePath has package identity '$id/$version'; expected '$packageId/$ExpectedVersion'."
    }

    $packageTypes = @($metadata.SelectNodes("*[local-name()='packageTypes']/*[local-name()='packageType']"))
    if ($packageTypes.Count -ne 1 -or $packageTypes[0].GetAttribute('name') -cne 'DotnetTool') {
        throw "$PackagePath must contain exactly one package type named 'DotnetTool'."
    }

    if (@($nuspec.SelectNodes("//*[local-name()='dependency']")).Count -ne 0) {
        throw "$PackagePath must contain its tool payload instead of package dependencies."
    }

    $readme = $metadata.SelectSingleNode("*[local-name()='readme']")
    if ($null -eq $readme -or $readme.InnerText -cne 'README.md' -or $null -eq $archive.GetEntry('README.md')) {
        throw "$PackagePath does not contain the configured root README.md."
    }

    $icon = $metadata.SelectSingleNode("*[local-name()='icon']")
    if ($null -eq $icon -or $icon.InnerText -cne 'TigerQuery256.png' -or $null -eq $archive.GetEntry('TigerQuery256.png')) {
        throw "$PackagePath does not contain the configured root TigerQuery256.png icon."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRepositoryCommit)) {
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        if ($null -eq $repository -or $repository.GetAttribute('commit') -cne $ExpectedRepositoryCommit) {
            $actualRepositoryCommit = if ($null -eq $repository) { '<missing>' } else { $repository.GetAttribute('commit') }
            throw "$PackagePath repository commit '$actualRepositoryCommit' does not match '$ExpectedRepositoryCommit'."
        }
    }

    $settingsEntries = @(
        $archive.Entries |
            Where-Object { [IO.Path]::GetFileName($_.FullName) -ceq 'DotnetToolSettings.xml' }
    )
    $expectedSettingsPath = "$toolDirectory/DotnetToolSettings.xml"
    if ($settingsEntries.Count -ne 1 -or $settingsEntries[0].FullName -cne $expectedSettingsPath) {
        throw "$PackagePath must contain exactly one DotnetToolSettings.xml at '$expectedSettingsPath'."
    }

    [xml] $toolSettings = Read-ArchiveEntry -Entry $settingsEntries[0]
    $toolRoot = $toolSettings.SelectSingleNode("/*[local-name()='DotNetCliTool']")
    if ($null -eq $toolRoot -or $toolRoot.GetAttribute('Version') -cne '1') {
        throw "$expectedSettingsPath must have a DotNetCliTool root with Version='1'."
    }

    $commands = @($toolRoot.SelectNodes("*[local-name()='Commands']/*[local-name()='Command']"))
    if ($commands.Count -ne 1) {
        throw "$expectedSettingsPath must contain exactly one command."
    }

    $command = $commands[0]
    if (
        $command.GetAttribute('Name') -cne 'tiger-sqlcmd' -or
        $command.GetAttribute('EntryPoint') -cne 'tiger-sqlcmd.dll' -or
        $command.GetAttribute('Runner') -cne 'dotnet'
    ) {
        throw "$expectedSettingsPath must define tiger-sqlcmd -> dotnet tiger-sqlcmd.dll."
    }

    foreach ($requiredToolFile in @(
        'tiger-sqlcmd.dll',
        'tiger-sqlcmd.deps.json',
        'tiger-sqlcmd.runtimeconfig.json'
    )) {
        if ($null -eq $archive.GetEntry("$toolDirectory/$requiredToolFile")) {
            throw "$PackagePath does not contain $toolDirectory/$requiredToolFile."
        }
    }

    $libEntries = @($archive.Entries | Where-Object { $_.FullName.StartsWith('lib/', [StringComparison]::Ordinal) })
    if ($libEntries.Count -ne 0) {
        throw "$PackagePath contains library-package content under lib/ instead of only a tool payload."
    }
}
finally {
    $archive.Dispose()
}

$sha256 = (Get-FileHash -LiteralPath $resolvedPackagePath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Validated $expectedFileName as command 'tiger-sqlcmd' for $toolFramework (SHA-256 $sha256)."
