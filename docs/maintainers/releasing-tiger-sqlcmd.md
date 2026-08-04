# TigerSqlCmd release checklist

This maintainer-only checklist prepares a `tiger-sqlcmd` release. It is deliberately
excluded from the public DocFX content and navigation. `Version.props` is the single
version source for the assemblies, .NET tool package, installer build, and generated
WinGet manifests.

Do not publish packages, create a GitHub Release, submit WinGet manifests, commit, or push
until the release owner explicitly starts that step.

## 1. Validate the source tree

From the repository root, restore once and run the release gates:

```powershell
dotnet restore TigerQuery.sln
dotnet build TigerQuery.sln -c Release --no-restore -m:1 /nodeReuse:false

$unconfiguredStore = Join-Path $env:TEMP (Join-Path ('TigerQuery-unconfigured-' + [Guid]::NewGuid().ToString('N')) 'connections.json')
$env:TIGERQUERY_CONNECTION_STORE_FILE = $unconfiguredStore
dotnet test TigerQuery.sln --no-restore --no-build `
  --logger 'console;verbosity=normal' -m:1 /nodeReuse:false
$testExit = $LASTEXITCODE
$storeCreated = Test-Path -LiteralPath $unconfiguredStore
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
"UnconfiguredStoreCreated=$storeCreated"
if ($testExit -ne 0 -or $storeCreated) { throw 'Unconfigured test gate failed.' }

dotnet docfx docs/api-docfx/docfx.json
git diff --check
```

Run the configured live SQL tests under normal host access using the exact bootstrap and
commands in `AGENTS.md`. Missing configuration must skip before SQL activity. Invalid,
malformed, ambiguous, or unauthorized configuration must fail. Never delete reported
orphan databases automatically.

## 2. Pack and smoke-test the .NET tool

The package targets `net10.0`; installation requires a .NET 10 SDK. An older SDK can
misleadingly report that `DotnetToolSettings.xml` is missing even though the package is
correct.

```powershell
$version = ([xml](Get-Content -LiteralPath Version.props)).Project.PropertyGroup.Version
dotnet pack ItTiger.TigerSqlCmd\ItTiger.TigerSqlCmd.csproj -c Release --no-build `
  -o artifacts\packages
$packagePath = "artifacts\packages\ItTiger.TigerSqlCmd.$version.nupkg"
& .\.github\scripts\Assert-TigerSqlCmdToolPackage.ps1 `
  -PackagePath $packagePath -ExpectedVersion $version

$toolPath = Join-Path $env:TEMP ('tiger-sqlcmd-tool-' + [Guid]::NewGuid().ToString('N'))
dotnet tool install ItTiger.TigerSqlCmd --tool-path $toolPath `
  --source (Resolve-Path artifacts\packages) --version $version
& (Join-Path $toolPath 'tiger-sqlcmd.exe') --version
& (Join-Path $toolPath 'tiger-sqlcmd.exe') --help
dotnet tool uninstall ItTiger.TigerSqlCmd --tool-path $toolPath
```

## 3. Build and validate the Windows installer

The installer follows TigerWrap's deployment contract:

- stable AppId and upgrade identity `ItTiger.TigerSqlCmd`;
- administrator elevation and machine-wide uninstall registration;
- `{autopf}\ItTiger\TigerSqlCmd` (normally `C:\Program Files\ItTiger\TigerSqlCmd`);
- the CLI payload under `{app}\cli` and that directory exactly once on the system PATH;
- runtime detection derived from the published `tiger-sqlcmd.runtimeconfig.json`;
- interactive runtime download from Microsoft when approved;
- silent failure when the runtime is missing unless `/INSTALLDOTNET` is supplied;
- artifact name `TigerSqlCmdSetup_<version_with_underscores>.exe` (no `_x64`).

Build with Inno Setup 7 installed:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass `
  -File ItTiger.TigerSqlCmd.Installer\BuildInstaller.ps1
```

Run the following from an elevated PowerShell. Do not use `/DIR` for the release gate: the
default Program Files destination is part of the contract.

```powershell
$version = ([xml](Get-Content -LiteralPath Version.props)).Project.PropertyGroup.Version
$installer = Resolve-Path ("ItTiger.TigerSqlCmd.Installer\Output\TigerSqlCmdSetup_{0}.exe" -f ($version -replace '\.', '_'))
$installDir = 'C:\Program Files\ItTiger\TigerSqlCmd'
$cliDir = Join-Path $installDir 'cli'

$install = Start-Process -FilePath $installer -Wait -PassThru -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-'
)
if ($install.ExitCode -ne 0) { throw "Clean install failed: $($install.ExitCode)" }

# Reinstall exercises the stable Inno upgrade identity and cleanup behavior.
$reinstall = Start-Process -FilePath $installer -Wait -PassThru -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-'
)
if ($reinstall.ExitCode -ne 0) { throw "Reinstall failed: $($reinstall.ExitCode)" }

$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
$pathMatches = @($machinePath -split ';' | Where-Object {
  $_.TrimEnd('\') -ieq $cliDir.TrimEnd('\')
})
if ($pathMatches.Count -ne 1) { throw "Expected one system PATH entry; found $($pathMatches.Count)." }

& (Join-Path $cliDir 'tiger-sqlcmd.exe') --version
& (Join-Path $cliDir 'tiger-sqlcmd.exe') --help

$uninstallKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\ItTiger.TigerSqlCmd_is1'
if (-not (Test-Path -LiteralPath $uninstallKey)) { throw 'Machine uninstall key is missing.' }
$uninstaller = Join-Path $installDir 'unins000.exe'
$uninstall = Start-Process -FilePath $uninstaller -Wait -PassThru -ArgumentList @(
  '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART'
)
if ($uninstall.ExitCode -ne 0) { throw "Silent uninstall failed: $($uninstall.ExitCode)" }

if (Test-Path -LiteralPath $installDir) { throw "Installation directory remains: $installDir" }
if (Test-Path -LiteralPath $uninstallKey) { throw 'Machine uninstall key remains.' }
$machinePath = [Environment]::GetEnvironmentVariable('Path', 'Machine')
if ($machinePath -split ';' | Where-Object { $_.TrimEnd('\') -ieq $cliDir.TrimEnd('\') }) {
  throw 'System PATH entry remains after uninstall.'
}
```

WinGet supplies `Microsoft.DotNet.Runtime.10` before setup. Direct silent setup uses
Inno's `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-` contract; add
`/INSTALLDOTNET` only when setup itself should download a missing runtime.

## 4. Prepare the WinGet submission

Generate TigerWrap-structured three-file manifests from the final installer. The script
reads the version, enforces the filename, calculates SHA-256, and writes an ignored
winget-pkgs-shaped staging tree:

```powershell
pwsh -NoProfile -File eng\winget\Prepare-TigerSqlCmdWinGet.ps1 -Validate
```

The generated installer manifest describes an x64 Inno installer with machine scope,
self-elevation, standard Inno silent modes, install-style upgrades, the stable
`ItTiger.TigerSqlCmd_is1` Apps & Features product code, and a dependency on
`Microsoft.DotNet.Runtime.10`.

Before submission, upload the exact installer to the matching GitHub Release, download it
from the manifest URL, and verify that its SHA-256 still matches both the local artifact
and `InstallerSha256`. Then rerun `winget validate --manifest <generated-version-folder>`
and perform local clean-install, upgrade, command, and uninstall checks through WinGet.
Do not submit the manifests from this repository task.

## 5. Manual publication order

After every gate is green and the release owner approves publication:

1. publish `ItTiger.TigerQuery`, `ItTiger.TigerQuery.Core`,
   `ItTiger.TigerQuery.CliCore`, and `ItTiger.TigerSqlCmd` as applicable;
2. create the matching `v<version>` GitHub Release;
3. upload the exact `.nupkg`, `.snupkg` where applicable, and
   `TigerSqlCmdSetup_<version_with_underscores>.exe` assets;
4. download each asset and verify SHA-256 against the locally approved artifacts;
5. regenerate and validate the WinGet staging manifests against the immutable release URL;
6. submit the WinGet manifest change separately and monitor its validation/review.

Generated packages, installer `WorkingDir`/`Output`, DocFX `api`/`_site`, and
`artifacts/winget` staging remain ignored and must not be committed.
