Set-StrictMode -Version Latest

<#
    Host-side helpers for preparing and validating the TigerSqlCmd WinGet package.

    This module owns TigerSqlCmd package policy only: what the manifests must say,
    which release asset they must point at, and what the submission set is. The
    Windows validation environment is TigerWinLab's job - see
    Invoke-TigerWinLabWinGetScenario.ps1 in the TigerWinLab repository. Nothing
    here creates a validation environment of its own.
#>

$script:PackageIdentifier = 'ItTiger.TigerSqlCmd'
$script:ProductCode = 'ItTiger.TigerSqlCmd_is1'
$script:Command = 'tiger-sqlcmd'
$script:RuntimeDependency = 'Microsoft.DotNet.Runtime.10'
$script:ReleaseRepositoryUrl = 'https://github.com/rkozlowski/TigerQuery'
$script:ResultSchemaVersion = 1

function New-TigerSqlCmdWinGetCheck {
    <#
        .SYNOPSIS
        Builds one PASS/WARN/FAIL check record.

        .DESCRIPTION
        The shape matches TigerWinLab's own check contract so lab checks and
        TigerQuery's manifest and release checks can be reported as one list.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [ValidateSet('PASS', 'WARN', 'FAIL')]
        [string] $Status,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Message
    )

    [pscustomobject][ordered]@{
        name = $Name
        status = $Status
        message = $Message
    }
}

function New-TigerSqlCmdWinGetAssertion {
    <#
        .SYNOPSIS
        Builds a check from a condition and both of its messages.

        .DESCRIPTION
        Both messages are built by the caller before the condition is evaluated,
        so a check reads correctly whichever way it goes, and neither message may
        dereference the thing the check exists to prove is there.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message,

        [Parameter(Mandatory)]
        [string] $FailureMessage,

        [ValidateSet('WARN', 'FAIL')]
        [string] $FailureStatus = 'FAIL'
    )

    if ($Condition) {
        return New-TigerSqlCmdWinGetCheck -Name $Name -Status 'PASS' -Message $Message
    }
    New-TigerSqlCmdWinGetCheck -Name $Name -Status $FailureStatus -Message $FailureMessage
}

function Assert-TigerSqlCmdWinGetVersion {
    <#
        .SYNOPSIS
        Rejects anything that is not a stable three-part release version.

        .DESCRIPTION
        The version becomes a tag, a file name, a URL, and a WinGet package
        version, so it is checked once here rather than in each of them.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version
    )

    if ($Version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw "Version '$Version' must be a stable three-part numeric version (for example, 0.8.8)."
    }
    $Version
}

function Get-TigerSqlCmdWinGetReleaseFact {
    <#
        .SYNOPSIS
        Derives every name and URL a release version implies.

        .DESCRIPTION
        The installer file name, the tag, and the immutable asset URL are all
        functions of the version. Deriving them in one place is what lets the
        manifest checks compare the manifests against something other than
        themselves.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version
    )

    $null = Assert-TigerSqlCmdWinGetVersion -Version $Version
    $installerFileName = 'TigerSqlCmdSetup_{0}.exe' -f ($Version -replace '\.', '_')
    $tag = "v$Version"

    [pscustomobject][ordered]@{
        version = $Version
        tag = $tag
        packageIdentifier = $script:PackageIdentifier
        productCode = $script:ProductCode
        command = $script:Command
        runtimeDependency = $script:RuntimeDependency
        installerFileName = $installerFileName
        installerUrl = '{0}/releases/download/{1}/{2}' -f $script:ReleaseRepositoryUrl, $tag, $installerFileName
        releaseNotesUrl = '{0}/releases/tag/{1}' -f $script:ReleaseRepositoryUrl, $tag
        manifestFileNames = @(
            "$script:PackageIdentifier.installer.yaml"
            "$script:PackageIdentifier.locale.en-US.yaml"
            "$script:PackageIdentifier.yaml"
        )
        submissionPath = 'manifests/i/ItTiger/TigerSqlCmd/{0}' -f $Version
    }
}

function Get-TigerSqlCmdWinGetManifestField {
    <#
        .SYNOPSIS
        Reads one scalar field from manifest lines.

        .DESCRIPTION
        The manifests this repository generates are a fixed, flat shape, so a
        line reader is enough and avoids taking a YAML dependency for three
        files. Indentation and a leading sequence dash are accepted so nested
        installer entries read by the same rule; this is not a YAML parser and
        must not be used as one.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $Lines,

        [Parameter(Mandatory)]
        [string] $Name
    )

    foreach ($line in $Lines) {
        if ($line -cmatch ('^\s*-?\s*{0}:\s*(?<value>.*?)\s*$' -f [regex]::Escape($Name))) {
            return [string] $Matches.value
        }
    }
    return $null
}

function Get-TigerSqlCmdWinGetManifestListItem {
    <#
        .SYNOPSIS
        Reads the block-sequence items that follow a top-level field.

        .DESCRIPTION
        Commands and Tags are written as a bare sequence under their key, so the
        items are the '- value' lines between that key and the next line that is
        not one.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $Lines,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $items = [System.Collections.Generic.List[string]]::new()
    $inside = $false
    foreach ($line in $Lines) {
        if ($line -cmatch ('^{0}:\s*$' -f [regex]::Escape($Name))) {
            $inside = $true
            continue
        }
        if (-not $inside) { continue }
        if ($line -cmatch '^-\s+(?<value>.+?)\s*$') {
            $items.Add([string] $Matches.value)
            continue
        }
        break
    }
    $items.ToArray()
}

function Get-TigerSqlCmdWinGetManifestDependency {
    <#
        .SYNOPSIS
        Reads the package dependencies an installer manifest declares.

        .DESCRIPTION
        A dependency is the only indented 'PackageIdentifier' in the document -
        the package's own identifier is at the left margin - so indentation is
        what distinguishes them.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [AllowEmptyString()]
        [string[]] $Lines
    )

    $dependencies = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $Lines) {
        if ($line -cmatch '^\s+-\s+PackageIdentifier:\s*(?<value>\S+)\s*$') {
            $dependencies.Add([string] $Matches.value)
        }
    }
    $dependencies.ToArray()
}

function Read-TigerSqlCmdWinGetManifestSet {
    <#
        .SYNOPSIS
        Loads the three prepared manifests and the facts they declare.

        .DESCRIPTION
        Throws only when the set cannot be read at all - a missing directory or
        a missing file is an operator error rather than a validation result.
        What the manifests say is returned as facts, so the caller can report
        each disagreement as its own check instead of stopping at the first.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ManifestDirectory,

        [Parameter(Mandatory)]
        [string] $Version
    )

    $release = Get-TigerSqlCmdWinGetReleaseFact -Version $Version
    $ManifestDirectory = [System.IO.Path]::GetFullPath($ManifestDirectory)
    if (-not (Test-Path -LiteralPath $ManifestDirectory -PathType Container)) {
        throw "Prepared WinGet manifest directory not found: $ManifestDirectory. Run eng\winget\Prepare-TigerSqlCmdWinGet.ps1 first."
    }

    $presentNames = @(Get-ChildItem -LiteralPath $ManifestDirectory -File | ForEach-Object Name)
    $missing = @($release.manifestFileNames | Where-Object { $_ -cnotin $presentNames })
    if ($missing.Count -ne 0) {
        throw "Prepared WinGet manifest set is incomplete. Missing: $($missing -join ', ')."
    }

    $documents = [System.Collections.Generic.List[object]]::new()
    $lines = [ordered]@{}
    foreach ($name in $release.manifestFileNames) {
        $path = Join-Path $ManifestDirectory $name
        $bytes = [System.IO.File]::ReadAllBytes($path)
        $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
        $text = [System.Text.UTF8Encoding]::new($false, $true).GetString($bytes)
        $lines[$name] = @($text -split "`r?`n")
        $documents.Add([pscustomobject][ordered]@{
            name = $name
            path = $path
            hasBom = $hasBom
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToUpperInvariant()
            length = $bytes.Length
        })
    }

    $installerLines = $lines[$release.manifestFileNames[0]]
    $localeLines = $lines[$release.manifestFileNames[1]]
    $versionLines = $lines[$release.manifestFileNames[2]]

    [pscustomobject][ordered]@{
        directory = $ManifestDirectory
        release = $release
        documents = $documents.ToArray()
        extraFiles = @($presentNames | Where-Object { $_ -cnotin $release.manifestFileNames })
        installer = [pscustomobject][ordered]@{
            packageIdentifier = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'PackageIdentifier'
            packageVersion = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'PackageVersion'
            manifestType = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'ManifestType'
            manifestVersion = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'ManifestVersion'
            installerType = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'InstallerType'
            scope = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'Scope'
            architecture = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'Architecture'
            installerUrl = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'InstallerUrl'
            installerSha256 = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'InstallerSha256'
            displayVersion = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'DisplayVersion'
            productCode = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'ProductCode'
            upgradeBehavior = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'UpgradeBehavior'
            elevationRequirement = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'ElevationRequirement'
            minimumOSVersion = Get-TigerSqlCmdWinGetManifestField -Lines $installerLines -Name 'MinimumOSVersion'
            commands = @(Get-TigerSqlCmdWinGetManifestListItem -Lines $installerLines -Name 'Commands')
            dependencies = @(Get-TigerSqlCmdWinGetManifestDependency -Lines $installerLines)
        }
        locale = [pscustomobject][ordered]@{
            packageIdentifier = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'PackageIdentifier'
            packageVersion = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'PackageVersion'
            packageLocale = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'PackageLocale'
            manifestType = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'ManifestType'
            manifestVersion = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'ManifestVersion'
            packageName = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'PackageName'
            publisher = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'Publisher'
            license = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'License'
            releaseNotesUrl = Get-TigerSqlCmdWinGetManifestField -Lines $localeLines -Name 'ReleaseNotesUrl'
        }
        version = [pscustomobject][ordered]@{
            packageIdentifier = Get-TigerSqlCmdWinGetManifestField -Lines $versionLines -Name 'PackageIdentifier'
            packageVersion = Get-TigerSqlCmdWinGetManifestField -Lines $versionLines -Name 'PackageVersion'
            defaultLocale = Get-TigerSqlCmdWinGetManifestField -Lines $versionLines -Name 'DefaultLocale'
            manifestType = Get-TigerSqlCmdWinGetManifestField -Lines $versionLines -Name 'ManifestType'
            manifestVersion = Get-TigerSqlCmdWinGetManifestField -Lines $versionLines -Name 'ManifestVersion'
        }
    }
}

function Test-TigerSqlCmdWinGetManifestSet {
    <#
        .SYNOPSIS
        Checks the prepared manifests against everything the release version implies.

        .DESCRIPTION
        This is the part of validation that needs no Windows guest: identity,
        version agreement across the three documents, installer metadata, the
        immutable asset URL, and submission-shaped contents. It reports checks
        rather than throwing, so one run shows every disagreement.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $ManifestSet
    )

    $release = $ManifestSet.release
    $installer = $ManifestSet.installer
    $locale = $ManifestSet.locale
    $version = $ManifestSet.version
    $checks = [System.Collections.Generic.List[object]]::new()

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/set' `
        -Condition ($ManifestSet.extraFiles.Count -eq 0) `
        -Message 'The manifest directory holds exactly the three submission files.' `
        -FailureMessage "The manifest directory holds files a submission does not expect: $($ManifestSet.extraFiles -join ', ')."))

    $identifiers = @($installer.packageIdentifier, $locale.packageIdentifier, $version.packageIdentifier)
    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/identifier' `
        -Condition (@($identifiers | Where-Object { $_ -cne $release.packageIdentifier }).Count -eq 0) `
        -Message "All three manifests declare $($release.packageIdentifier)." `
        -FailureMessage "PackageIdentifier is '$($identifiers -join ', ')'; expected $($release.packageIdentifier) in all three."))

    $versions = @($installer.packageVersion, $locale.packageVersion, $version.packageVersion)
    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/version' `
        -Condition (@($versions | Where-Object { $_ -cne $release.version }).Count -eq 0) `
        -Message "All three manifests declare version $($release.version)." `
        -FailureMessage "PackageVersion is '$($versions -join ', ')'; expected $($release.version) in all three."))

    $manifestTypes = @($installer.manifestType, $locale.manifestType, $version.manifestType)
    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/type' `
        -Condition ($installer.manifestType -ceq 'installer' -and $locale.manifestType -ceq 'defaultLocale' -and $version.manifestType -ceq 'version') `
        -Message 'The set is the installer, defaultLocale, and version manifests.' `
        -FailureMessage "ManifestType values are '$($manifestTypes -join ', ')'; expected installer, defaultLocale, version."))

    $manifestVersions = @($installer.manifestVersion, $locale.manifestVersion, $version.manifestVersion)
    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/schema-version' `
        -Condition ((@($manifestVersions | Select-Object -Unique).Count -eq 1) -and -not [string]::IsNullOrWhiteSpace([string] $installer.manifestVersion)) `
        -Message "All three manifests use ManifestVersion $($installer.manifestVersion)." `
        -FailureMessage "ManifestVersion differs across the set: '$($manifestVersions -join ', ')'."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/default-locale' `
        -Condition ($version.defaultLocale -ceq $locale.packageLocale -and -not [string]::IsNullOrWhiteSpace([string] $locale.packageLocale)) `
        -Message "The version manifest's DefaultLocale matches the locale manifest ($($locale.packageLocale))." `
        -FailureMessage "DefaultLocale '$($version.defaultLocale)' does not match PackageLocale '$($locale.packageLocale)'."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/installer-type' `
        -Condition ($installer.installerType -ceq 'inno') `
        -Message 'The installer is declared inno, matching the Inno Setup installer this repository builds.' `
        -FailureMessage "InstallerType is '$($installer.installerType)'; expected inno."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/scope' `
        -Condition ($installer.scope -ceq 'machine') `
        -Message 'The package is declared machine scope, matching the installer.' `
        -FailureMessage "Scope is '$($installer.scope)'; expected machine."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/architecture' `
        -Condition ($installer.architecture -ceq 'x64') `
        -Message 'The single installer entry is x64.' `
        -FailureMessage "Architecture is '$($installer.architecture)'; expected x64."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/installer-url' `
        -Condition ($installer.installerUrl -ceq $release.installerUrl) `
        -Message "InstallerUrl is the immutable release asset $($release.installerUrl)." `
        -FailureMessage "InstallerUrl is '$($installer.installerUrl)'; expected '$($release.installerUrl)'."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/installer-sha256' `
        -Condition ([string] $installer.installerSha256 -cmatch '^[0-9A-F]{64}$') `
        -Message "InstallerSha256 is a 64-character upper-case digest ($($installer.installerSha256))." `
        -FailureMessage "InstallerSha256 '$($installer.installerSha256)' is not a 64-character upper-case SHA-256 digest."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/apps-and-features' `
        -Condition ($installer.productCode -ceq $release.productCode -and $installer.displayVersion -cne $release.version) `
        -Message "AppsAndFeaturesEntries names product code $($release.productCode) without repeating PackageVersion as DisplayVersion." `
        -FailureMessage "AppsAndFeaturesEntries names product code '$($installer.productCode)' and DisplayVersion '$($installer.displayVersion)'; expected product code '$($release.productCode)' and no DisplayVersion equal to PackageVersion '$($release.version)'."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/command' `
        -Condition ($installer.commands -ccontains $release.command) `
        -Message "The manifest declares the $($release.command) command." `
        -FailureMessage "Commands is '$($installer.commands -join ', ')'; expected it to contain $($release.command)."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/dependency' `
        -Condition ($installer.dependencies -ccontains $release.runtimeDependency) `
        -Message "The framework-dependent package declares its $($release.runtimeDependency) dependency." `
        -FailureMessage "PackageDependencies is '$($installer.dependencies -join ', ')'; expected it to contain $($release.runtimeDependency)."))

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/release-notes-url' `
        -Condition ($locale.releaseNotesUrl -ceq $release.releaseNotesUrl) `
        -Message "ReleaseNotesUrl points at the $($release.tag) release." `
        -FailureMessage "ReleaseNotesUrl is '$($locale.releaseNotesUrl)'; expected '$($release.releaseNotesUrl)'."))

    $bomFiles = @($ManifestSet.documents | Where-Object { $_.hasBom } | ForEach-Object name)
    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'manifest/encoding' `
        -Condition ($bomFiles.Count -eq 0) `
        -Message 'All three manifests are UTF-8 without a byte-order mark, as prepared.' `
        -FailureMessage "These manifests carry a UTF-8 byte-order mark: $($bomFiles -join ', ')."))

    $checks.ToArray()
}

function Get-TigerSqlCmdWinGetRecordedChecksum {
    <#
        .SYNOPSIS
        Reads one file's digest out of a SHA256SUMS.txt, or nothing.

        .DESCRIPTION
        The file is the sha256sum format the release workflow writes: a digest,
        whitespace, then the file name, optionally marked binary with an
        asterisk. The pattern is concatenated rather than built with -f, because
        the quantifier braces in the digest would otherwise be read as format
        placeholders and every line would fail to match.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $ChecksumPath,

        [Parameter(Mandatory)]
        [string] $FileName
    )

    $pattern = '^(?<hash>[0-9a-fA-F]{64})\s+\*?' + [regex]::Escape($FileName) + '\s*$'
    foreach ($line in @(Get-Content -LiteralPath $ChecksumPath)) {
        if ($line -cmatch $pattern) {
            return ([string] $Matches.hash).ToUpperInvariant()
        }
    }
    return $null
}

function Test-TigerSqlCmdWinGetReleaseAsset {
    <#
        .SYNOPSIS
        Proves the published release asset is the one the manifests describe.

        .DESCRIPTION
        The manifests can only promise a URL and a digest. This downloads that
        exact URL as an unauthenticated client would, hashes the bytes, and
        compares them with the manifest, with the release's own checksum files
        when they are on hand, and with the locally retained installer when it
        still exists. The downloaded file is what the lab then installs, so what
        is validated in the guest is the released payload rather than a rebuild.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $ManifestSet,

        [Parameter(Mandatory)]
        [string] $DownloadDirectory,

        [string] $ReleaseInputDirectory,

        [ValidateRange(30, 1800)]
        [int] $TimeoutSeconds = 300
    )

    $release = $ManifestSet.release
    $expectedSha256 = [string] $ManifestSet.installer.installerSha256
    $checks = [System.Collections.Generic.List[object]]::new()
    $null = New-Item -ItemType Directory -Path $DownloadDirectory -Force
    $downloadPath = Join-Path $DownloadDirectory $release.installerFileName

    $downloaded = $null
    $failure = $null
    try {
        $progress = $ProgressPreference
        $ProgressPreference = 'SilentlyContinue'
        try {
            Invoke-WebRequest -Uri $release.installerUrl -OutFile $downloadPath `
                -TimeoutSec $TimeoutSeconds -MaximumRedirection 5 -ErrorAction Stop
        }
        finally {
            $ProgressPreference = $progress
        }
        $downloaded = [pscustomobject][ordered]@{
            path = [System.IO.Path]::GetFullPath($downloadPath)
            length = [long] (Get-Item -LiteralPath $downloadPath).Length
            sha256 = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToUpperInvariant()
        }
    }
    catch {
        $failure = $_.Exception.Message
    }

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'release/asset-download' `
        -Condition ($null -ne $downloaded) `
        -Message "An unauthenticated client can download $($release.installerUrl)." `
        -FailureMessage "The release asset '$($release.installerUrl)' could not be downloaded: $failure"))

    if ($null -eq $downloaded) {
        return [pscustomobject]@{
            checks = $checks.ToArray()
            installerPath = $null
            sha256 = $null
            length = 0L
        }
    }

    $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'release/asset-sha256' `
        -Condition ($downloaded.sha256 -ceq $expectedSha256) `
        -Message "The published asset hashes to the manifest's InstallerSha256 ($expectedSha256)." `
        -FailureMessage "The published asset hashes to '$($downloaded.sha256)'; the manifest declares '$expectedSha256'."))

    if ([string]::IsNullOrWhiteSpace($ReleaseInputDirectory) -or -not (Test-Path -LiteralPath $ReleaseInputDirectory -PathType Container)) {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'release/inputs' -Status 'WARN' `
            -Message 'No release input directory was available, so SHA256SUMS.txt, release-artifacts.json, and the retained installer were not cross-checked.'))
        return [pscustomobject]@{
            checks = $checks.ToArray()
            installerPath = $downloaded.path
            sha256 = $downloaded.sha256
            length = $downloaded.length
        }
    }

    $checksumPath = Join-Path $ReleaseInputDirectory 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $checksumPath -PathType Leaf) {
        $recorded = Get-TigerSqlCmdWinGetRecordedChecksum -ChecksumPath $checksumPath -FileName $release.installerFileName
        $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'release/checksums-file' `
            -Condition ($null -ne $recorded -and $recorded -ceq $downloaded.sha256) `
            -Message "SHA256SUMS.txt records the same digest for $($release.installerFileName)." `
            -FailureMessage "SHA256SUMS.txt records '$recorded' for $($release.installerFileName); the published asset hashes to '$($downloaded.sha256)'."))
    }
    else {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'release/checksums-file' -Status 'WARN' `
            -Message "SHA256SUMS.txt is not present in '$ReleaseInputDirectory', so the release's own checksum file was not cross-checked."))
    }

    $artifactManifestPath = Join-Path $ReleaseInputDirectory 'release-artifacts.json'
    if (Test-Path -LiteralPath $artifactManifestPath -PathType Leaf) {
        $entry = $null
        $recordedVersion = $null
        try {
            $artifactManifest = Get-Content -LiteralPath $artifactManifestPath -Raw | ConvertFrom-Json
            $recordedVersion = [string] $artifactManifest.releaseVersion
            $entry = @($artifactManifest.artifacts | Where-Object { $_.name -ceq $release.installerFileName }) |
                Select-Object -First 1
        }
        catch {
            $entry = $null
        }
        $matched = $null -ne $entry -and
            $recordedVersion -ceq $release.version -and
            ([string] $entry.sha256).ToUpperInvariant() -ceq $downloaded.sha256 -and
            [long] $entry.length -eq $downloaded.length
        $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'release/artifact-manifest' `
            -Condition $matched `
            -Message "release-artifacts.json records $($release.installerFileName) for $($release.version) with the same digest and length." `
            -FailureMessage "release-artifacts.json does not record $($release.installerFileName) for $($release.version) with digest $($downloaded.sha256) and length $($downloaded.length)."))
    }
    else {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'release/artifact-manifest' -Status 'WARN' `
            -Message "release-artifacts.json is not present in '$ReleaseInputDirectory', so the release artifact manifest was not cross-checked."))
    }

    $localInstallerPath = Join-Path $ReleaseInputDirectory $release.installerFileName
    if (Test-Path -LiteralPath $localInstallerPath -PathType Leaf) {
        $localSha256 = (Get-FileHash -LiteralPath $localInstallerPath -Algorithm SHA256).Hash.ToUpperInvariant()
        $checks.Add((New-TigerSqlCmdWinGetAssertion -Name 'release/local-installer' `
            -Condition ($localSha256 -ceq $downloaded.sha256) `
            -Message 'The locally retained installer is byte-identical to the published asset.' `
            -FailureMessage "The locally retained installer hashes to '$localSha256'; the published asset hashes to '$($downloaded.sha256)'."))
    }
    else {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'release/local-installer' -Status 'WARN' `
            -Message "$($release.installerFileName) is not retained in '$ReleaseInputDirectory', so no local copy was compared."))
    }

    [pscustomobject]@{
        checks = $checks.ToArray()
        installerPath = $downloaded.path
        sha256 = $downloaded.sha256
        length = $downloaded.length
    }
}

function Resolve-TigerWinLabRoot {
    <#
        .SYNOPSIS
        Locates the TigerWinLab working copy that provides the guest.

        .DESCRIPTION
        TigerWinLab is a separate repository, so it is found rather than
        vendored: an explicit path first, then TIGERWINLAB_ROOT, then the
        sibling checkout. Only a directory that actually exposes the WinGet
        scenario entry point is accepted, so a wrong path fails here instead of
        halfway through a run.
    #>
    [CmdletBinding()]
    param(
        [string] $Path
    )

    $candidates = [System.Collections.Generic.List[object]]::new()
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $candidates.Add(@{ source = 'the -TigerWinLabRoot argument'; path = $Path })
    }
    if (-not [string]::IsNullOrWhiteSpace($env:TIGERWINLAB_ROOT)) {
        $candidates.Add(@{ source = 'TIGERWINLAB_ROOT'; path = $env:TIGERWINLAB_ROOT })
    }
    $repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $candidates.Add(@{ source = 'the sibling checkout'; path = (Join-Path (Split-Path -Parent $repositoryRoot) 'TigerWinLab') })

    foreach ($candidate in $candidates) {
        $root = [string] $candidate.path
        if ([string]::IsNullOrWhiteSpace($root)) { continue }
        $root = [System.IO.Path]::GetFullPath($root)
        $entryPoint = Join-Path $root 'Invoke-TigerWinLabWinGetScenario.ps1'
        if (Test-Path -LiteralPath $entryPoint -PathType Leaf) {
            return [pscustomobject][ordered]@{
                root = $root
                source = [string] $candidate.source
                winGetScenario = $entryPoint
                labValidation = Join-Path $root 'Test-TigerWinLab.ps1'
            }
        }
    }

    $attempted = ($candidates | ForEach-Object { "$($_.source) ($($_.path))" }) -join '; '
    throw "TigerWinLab was not found. Pass -TigerWinLabRoot, set TIGERWINLAB_ROOT, or check TigerWinLab out beside this repository. Tried $attempted."
}

function Get-TigerSqlCmdWinGetRelativePath {
    <#
        .SYNOPSIS
        Expresses one path relative to a directory, in Windows form.

        .DESCRIPTION
        A TigerWinLab specification resolves relative paths against its own
        location, which keeps a generated specification portable. A relative
        path cannot leave the drive, so an absolute path is kept when the two do
        not share a root.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $From,

        [Parameter(Mandatory)]
        [string] $To
    )

    $From = [System.IO.Path]::GetFullPath($From)
    $To = [System.IO.Path]::GetFullPath($To)
    if (-not [string]::Equals(
            [System.IO.Path]::GetPathRoot($From),
            [System.IO.Path]::GetPathRoot($To),
            [StringComparison]::OrdinalIgnoreCase)) {
        return $To
    }
    [System.IO.Path]::GetRelativePath($From, $To)
}

function New-TigerSqlCmdWinGetLabSpec {
    <#
        .SYNOPSIS
        Renders the TigerWinLab WinGet scenario specification for one release.

        .DESCRIPTION
        TigerQuery owns what TigerSqlCmd must look like once installed, so those
        expectations live here in a template and only the version and the two
        artifact paths are filled in per release. The paths are written relative
        to the specification file, so nothing in the lab resolves a path this
        repository chose for it.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version,

        [Parameter(Mandatory)]
        [string] $ManifestDirectory,

        [Parameter(Mandatory)]
        [string] $InstallerPath,

        [Parameter(Mandatory)]
        [string] $SpecPath,

        [string] $TemplatePath = (Join-Path $PSScriptRoot 'tiger-sqlcmd.labspec.template.json')
    )

    $release = Get-TigerSqlCmdWinGetReleaseFact -Version $Version
    if (-not (Test-Path -LiteralPath $TemplatePath -PathType Leaf)) {
        throw "The TigerWinLab specification template was not found: $TemplatePath"
    }

    $SpecPath = [System.IO.Path]::GetFullPath($SpecPath)
    $specDirectory = Split-Path -Parent $SpecPath
    $null = New-Item -ItemType Directory -Path $specDirectory -Force

    try {
        $spec = Get-Content -LiteralPath $TemplatePath -Raw | ConvertFrom-Json
    }
    catch {
        throw "The TigerWinLab specification template '$TemplatePath' is not valid JSON: $($_.Exception.Message)"
    }

    $spec.package.version = $release.version
    $spec.manifestDirectory = Get-TigerSqlCmdWinGetRelativePath -From $specDirectory -To $ManifestDirectory
    $spec.installer.path = Get-TigerSqlCmdWinGetRelativePath -From $specDirectory -To $InstallerPath
    $spec.installer.expectedUrl = $release.installerUrl

    [System.IO.File]::WriteAllText(
        $SpecPath,
        ($spec | ConvertTo-Json -Depth 12),
        [System.Text.UTF8Encoding]::new($false))
    $SpecPath
}

function Get-TigerSqlCmdWinGetScenarioCheck {
    <#
        .SYNOPSIS
        Flattens a TigerWinLab job result into this report's check list.

        .DESCRIPTION
        TigerWinLab returns guest health checks plus ordered scenario phases,
        each with its own checks. They are prefixed rather than rewritten, so a
        failing check here names the same phase and check TigerWinLab's own
        summary does.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowNull()]
        [object] $JobResult
    )

    $checks = [System.Collections.Generic.List[object]]::new()
    if ($null -eq $JobResult) {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
            -Message 'The TigerWinLab WinGet scenario produced no result.'))
        return $checks.ToArray()
    }

    $status = [string] $JobResult.status
    if ($status -ceq 'BUSY') {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
            -Message "TigerWinLab is in use by another process: $($JobResult.message)"))
        return $checks.ToArray()
    }

    if ($null -ne $JobResult.PSObject.Properties['health']) {
        foreach ($check in @($JobResult.health)) {
            $checks.Add((New-TigerSqlCmdWinGetCheck -Name "lab/health/$($check.name)" `
                -Status ([string] $check.status) -Message ([string] $check.message)))
        }
    }

    $scenario = $null
    if ($null -ne $JobResult.PSObject.Properties['result']) { $scenario = $JobResult.result }
    if ($null -eq $scenario -or $null -eq $scenario.PSObject.Properties['phases']) {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
            -Message "The TigerWinLab WinGet scenario produced no phases: $($JobResult.message)"))
        return $checks.ToArray()
    }

    foreach ($phase in @($scenario.phases)) {
        foreach ($check in @($phase.checks)) {
            $checks.Add((New-TigerSqlCmdWinGetCheck -Name ('lab/{0}/{1}' -f $phase.name, $check.name) `
                -Status ([string] $check.status) -Message ([string] $check.message)))
        }
    }

    if ($status -cnotin @('OK', 'FAILED')) {
        $checks.Add((New-TigerSqlCmdWinGetCheck -Name 'lab/scenario' -Status 'FAIL' `
            -Message "The TigerWinLab WinGet scenario ended as ${status}: $($JobResult.message)"))
    }
    $checks.ToArray()
}

function Get-TigerSqlCmdWinGetSubmissionFile {
    <#
        .SYNOPSIS
        Names the exact files a winget-pkgs pull request must carry.

        .DESCRIPTION
        The submission is the three prepared manifests copied to
        manifests/i/ItTiger/TigerSqlCmd/<version>/ in a winget-pkgs fork. Their
        digests are recorded, so the files that were validated can be shown to
        be the files that were submitted.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $ManifestSet
    )

    foreach ($document in $ManifestSet.documents) {
        [pscustomobject][ordered]@{
            name = $document.name
            sourcePath = $document.path
            submissionPath = '{0}/{1}' -f $ManifestSet.release.submissionPath, $document.name
            length = $document.length
            sha256 = $document.sha256
        }
    }
}

function Get-TigerSqlCmdWinGetVerdict {
    <#
        .SYNOPSIS
        Reduces a check list to PASS or FAIL.

        .DESCRIPTION
        A warning is something a maintainer must read, not something that blocks
        a submission; only a FAIL does that. An empty check list is a FAIL,
        because nothing was proven.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Checks
    )

    $failed = @($Checks | Where-Object { $_.status -ceq 'FAIL' })
    $warned = @($Checks | Where-Object { $_.status -ceq 'WARN' })
    $passed = @($Checks | Where-Object { $_.status -ceq 'PASS' })
    $status = if ($Checks.Count -eq 0 -or $failed.Count -gt 0) { 'FAIL' } else { 'PASS' }

    [pscustomobject][ordered]@{
        status = $status
        passed = $passed.Count
        warned = $warned.Count
        failed = $failed.Count
        total = @($Checks).Count
    }
}

function New-TigerSqlCmdWinGetResult {
    <#
        .SYNOPSIS
        Assembles the machine-readable readiness record.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $ManifestSet,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [object[]] $Checks,

        [object] $Lab,

        [object] $Asset,

        [Parameter(Mandatory)]
        [string] $ResultPath
    )

    $verdict = Get-TigerSqlCmdWinGetVerdict -Checks $Checks
    $release = $ManifestSet.release
    $publishedSha256 = $null
    $publishedLength = 0L
    if ($null -ne $Asset) {
        $publishedSha256 = [string] $Asset.sha256
        $publishedLength = [long] $Asset.length
    }

    [pscustomobject][ordered]@{
        schemaVersion = $script:ResultSchemaVersion
        status = $verdict.status
        package = $release.packageIdentifier
        version = $release.version
        completedUtc = [DateTime]::UtcNow.ToString('o')
        summary = $verdict
        installer = [pscustomobject][ordered]@{
            url = $release.installerUrl
            fileName = $release.installerFileName
            declaredSha256 = [string] $ManifestSet.installer.installerSha256
            publishedSha256 = $publishedSha256
            publishedLength = $publishedLength
        }
        submission = [pscustomobject][ordered]@{
            repository = 'microsoft/winget-pkgs'
            path = $release.submissionPath
            files = @(Get-TigerSqlCmdWinGetSubmissionFile -ManifestSet $ManifestSet)
        }
        lab = $Lab
        checks = @($Checks)
        resultPath = [System.IO.Path]::GetFullPath($ResultPath)
    }
}

function Format-TigerSqlCmdWinGetSummary {
    <#
        .SYNOPSIS
        Renders a readiness result as coloured lines.

        .DESCRIPTION
        One layout serves both the terminal and the summary file a run leaves
        behind, so the record a maintainer keeps is the report they read. Each
        line carries its colour rather than being written directly, because the
        file wants the text and the terminal wants both.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Result
    )

    function New-Line {
        param(
            [AllowEmptyString()]
            [string] $Text = '',

            [string] $Colour = 'DarkGray'
        )

        [pscustomobject]@{ text = $Text; colour = $Colour }
    }

    New-Line
    New-Line -Text "TigerSqlCmd $($Result.version) WinGet readiness" -Colour 'Cyan'
    foreach ($check in @($Result.checks)) {
        $colour = switch ([string] $check.status) {
            'PASS' { 'DarkGray' }
            'WARN' { 'Yellow' }
            default { 'Red' }
        }
        New-Line -Colour $colour -Text (
            '  {0,-4} {1,-46} {2}' -f $check.status, $check.name, $check.message)
    }

    New-Line
    New-Line -Text "  $($Result.summary.passed) passed, $($Result.summary.warned) warned, $($Result.summary.failed) failed"
    New-Line -Text "  Installer: $($Result.installer.url)"
    New-Line -Text "  Published SHA-256: $($Result.installer.publishedSha256)"
    New-Line
    New-Line -Text "  Files ready for microsoft/winget-pkgs at $($Result.submission.path):"
    foreach ($file in @($Result.submission.files)) {
        New-Line -Text "    $($file.name)"
        New-Line -Text "      $($file.sourcePath)"
    }
    New-Line
    New-Line -Text "  Machine-readable result: $($Result.resultPath)"
    if ($null -ne $Result.lab -and
        $null -ne $Result.lab.PSObject.Properties['jobOutputPath'] -and
        -not [string]::IsNullOrWhiteSpace([string] $Result.lab.jobOutputPath)) {
        New-Line -Text "  TigerWinLab job artifacts: $($Result.lab.jobOutputPath)"
    }

    New-Line
    if ($Result.status -ceq 'PASS') {
        New-Line -Colour 'Green' -Text "PASS: TigerSqlCmd $($Result.version) is ready for a winget-pkgs pull request."
    }
    else {
        New-Line -Colour 'Red' -Text "FAIL: TigerSqlCmd $($Result.version) is not ready for a winget-pkgs pull request."
    }
}

function Write-TigerSqlCmdWinGetSummary {
    <#
        .SYNOPSIS
        Renders a readiness result for a person reading a terminal.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Result
    )

    foreach ($line in @(Format-TigerSqlCmdWinGetSummary -Result $Result)) {
        Write-Host $line.text -ForegroundColor $line.colour
    }
}

function Save-TigerSqlCmdWinGetSummary {
    <#
        .SYNOPSIS
        Writes the human-readable report beside the machine-readable one.

        .DESCRIPTION
        A release record that only exists in terminal scrollback is not a record.
        This is the same report Write-TigerSqlCmdWinGetSummary prints, so the two
        can never disagree about what a run found.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Result,

        [Parameter(Mandatory)]
        [string] $Path
    )

    $Path = [System.IO.Path]::GetFullPath($Path)
    $lines = @(Format-TigerSqlCmdWinGetSummary -Result $Result | ForEach-Object { [string] $_.text })
    [System.IO.File]::WriteAllLines($Path, $lines, [System.Text.UTF8Encoding]::new($false))
    $Path
}

Export-ModuleMember -Function @(
    'New-TigerSqlCmdWinGetCheck'
    'New-TigerSqlCmdWinGetAssertion'
    'Assert-TigerSqlCmdWinGetVersion'
    'Get-TigerSqlCmdWinGetReleaseFact'
    'Get-TigerSqlCmdWinGetManifestField'
    'Get-TigerSqlCmdWinGetManifestListItem'
    'Get-TigerSqlCmdWinGetManifestDependency'
    'Read-TigerSqlCmdWinGetManifestSet'
    'Test-TigerSqlCmdWinGetManifestSet'
    'Get-TigerSqlCmdWinGetRecordedChecksum'
    'Test-TigerSqlCmdWinGetReleaseAsset'
    'Resolve-TigerWinLabRoot'
    'Get-TigerSqlCmdWinGetRelativePath'
    'New-TigerSqlCmdWinGetLabSpec'
    'Get-TigerSqlCmdWinGetScenarioCheck'
    'Get-TigerSqlCmdWinGetSubmissionFile'
    'Get-TigerSqlCmdWinGetVerdict'
    'New-TigerSqlCmdWinGetResult'
    'Format-TigerSqlCmdWinGetSummary'
    'Write-TigerSqlCmdWinGetSummary'
    'Save-TigerSqlCmdWinGetSummary'
)
