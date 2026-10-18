[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Version,

    # Defaults to .github/release-notes/<version>.md.
    [string] $NotesFile
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# A release's notes are a checked-in file per version whose first heading names the product and
# the version, with real user-facing content; a changelog link is not release notes.
if ([string]::IsNullOrWhiteSpace($NotesFile)) {
    $NotesFile = Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) ".github\release-notes\$Version.md"
}
$NotesFile = [IO.Path]::GetFullPath($NotesFile)
if (-not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) {
    throw "Release notes not found: $NotesFile"
}

$lines = @(Get-Content -LiteralPath $NotesFile -Encoding utf8)
$heading = @($lines | Where-Object { $_ -match '^#\s' }) | Select-Object -First 1
if ($heading -cne "# TigerQuery $Version") {
    throw "The first heading of $NotesFile must be '# TigerQuery $Version'; it is '$heading'."
}
$content = @($lines | Where-Object { $_ -match '\S' -and $_ -notmatch '^#' })
if ($content.Count -lt 3) {
    throw "$NotesFile has no real release notes under its heading."
}
$placeholder = @($lines | Where-Object { $_ -match '(?i)\b(TODO|TBD|FIXME)\b|<version>' })
if ($placeholder.Count -gt 0) {
    throw "$NotesFile still contains placeholder text: $($placeholder -join ' | ')"
}
Write-Host "Release notes for TigerQuery $Version are present: $NotesFile"
