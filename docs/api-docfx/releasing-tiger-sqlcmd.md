# Releasing `tiger-sqlcmd`

`Version.props` is the single source of truth for the .NET tool package and the
Windows installer. Do not edit a version in the installer sources.

## Build the .NET tool package

From the repository root:

```powershell
dotnet pack ItTiger.TigerSqlCmd\ItTiger.TigerSqlCmd.csproj -c Release -o artifacts\packages
```

The expected package is
`artifacts\packages\ItTiger.TigerSqlCmd.<version>.nupkg`. Validate it without
touching the global tool store:

```powershell
$toolPath = Join-Path $env:TEMP ('tiger-sqlcmd-tool-' + [Guid]::NewGuid().ToString('N'))
dotnet tool install ItTiger.TigerSqlCmd --tool-path $toolPath `
  --add-source artifacts\packages --version <version>
& (Join-Path $toolPath 'tiger-sqlcmd.exe') --version
& (Join-Path $toolPath 'tiger-sqlcmd.exe') --help
dotnet tool uninstall ItTiger.TigerSqlCmd --tool-path $toolPath
```

For a local manifest installation, use `dotnet new tool-manifest` followed by
`dotnet tool install --local ItTiger.TigerSqlCmd`. Use `dotnet tool update`
and `dotnet tool uninstall` with `--global`, `--local`, or `--tool-path` to
address the corresponding installation.

## Build the Windows installer

Install Inno Setup 7, then run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File ItTiger.TigerSqlCmd.Installer\BuildInstaller.ps1
```

A Release build of `ItTiger.TigerSqlCmd.Installer` invokes the same script. It
publishes the complete framework-dependent CLI payload under the ignored
`WorkingDir\cli` directory and produces:

```text
ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmdSetup_<version_with_underscores>_x64.exe
```

The per-user installer writes to
`%LOCALAPPDATA%\Programs\IT Tiger\TigerSqlCmd`, adds that directory to the user
PATH, and removes it from PATH during uninstall. It derives the required x64
`Microsoft.NETCore.App` runtime from `tiger-sqlcmd.runtimeconfig.json`. An
interactive install offers to download the runtime from Microsoft. Silent setup
fails when it is absent unless `/INSTALLDOTNET` is supplied.

Validate clean install, reinstall/upgrade, commands, and uninstall with a
throwaway destination:

```powershell
$installDir = Join-Path $env:TEMP ('TigerSqlCmd-' + [Guid]::NewGuid().ToString('N'))
& .\ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmdSetup_<version>_x64.exe `
  /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /DIR="$installDir"
& (Join-Path $installDir 'tiger-sqlcmd.exe') --version
& (Join-Path $installDir 'tiger-sqlcmd.exe') --help
& (Join-Path $installDir 'unins000.exe') /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
Test-Path -LiteralPath $installDir
```

## GitHub Release and WinGet inputs

Attach both the `.nupkg` and the versioned x64 installer `.exe` to the matching
GitHub Release; do not rename the installer after hashing it. For a future
WinGet manifest, record:

| Field | Value |
| --- | --- |
| Package version | Exact `Version.props` value |
| Publisher | `IT Tiger` |
| Installer URL | Immutable GitHub Release asset URL |
| Installer SHA-256 | `Get-FileHash <installer> -Algorithm SHA256` |
| Architecture | `x64` |
| Scope | `user` |
| Installer type | `inno` |
| Silent behavior | Inno `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-`; add `/INSTALLDOTNET` only when setup should install a missing runtime |

Before publishing, run the tests, a Release build, package and installer
validation, DocFX, and `git diff --check`. NuGet publication and the first
WinGet manifest submission remain separate, intentional release actions.
