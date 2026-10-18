<#
    .SYNOPSIS
    TigerWinLab guest payload: proves the TigerSqlCmd installer on a clean Windows guest.

    .DESCRIPTION
    Runs inside a TigerWinLab job (Windows PowerShell 5.1, as the guest's administrator) from a
    clean baseline with no .NET runtime. run.json names the candidate installer, the published
    Inno Setup installer it migrates from, and the facts the host knows about the candidate. In
    order:

      fresh-install   a machine-wide quiet install that acquires the .NET 10 runtime itself;
                      registration, files, PATH, the Start Menu shell, the command from a new
                      session, verify; then the registered quiet uninstall removes everything
                      the installation added and keeps the runtime
      legacy          the published 0.8.8 Inno Setup installation over a seeded connection store
                      holding a DPAPI-protected password, another tool's file and user files
      migration       the candidate over it: the Inno Setup installation removed by its own
                      uninstaller, one registration, one PATH entry, the candidate's binaries,
                      the saved password still decrypting, the user data byte-identical
      uninstall       the registered quiet uninstall of the migrated installation: program,
                      registration, PATH, shortcut and installer state gone; the connection
                      store, the user's files and the .NET runtime kept

    Writes the phase/check result to TIGERWINLAB_JOB_RESULT and exits 0 only when every check
    passes. Every assertion is TigerQuery's; TigerWinLab supplies the guest and the evidence.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Artifacts = $env:TIGERWINLAB_JOB_ARTIFACTS
$Config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'run.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$Candidate = Join-Path $PSScriptRoot ([string] $Config.candidate)
$Legacy = Join-Path $PSScriptRoot ([string] $Config.legacy)
$Version = [string] $Config.version
$LegacyVersion = [string] $Config.legacyVersion

$Uninstall = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall'
$Key = "$Uninstall\ItTiger.TigerSqlCmd"
$LegacyKey = "$Uninstall\ItTiger.TigerSqlCmd_is1"
$Root = Join-Path $env:ProgramFiles 'TigerSqlCmd'
$LegacyRoot = Join-Path $env:ProgramFiles 'ItTiger\TigerSqlCmd'
$LegacyCli = Join-Path $LegacyRoot 'cli'
$State = Join-Path $env:ProgramData 'TigerSetup\ItTiger.TigerSqlCmd'
$Shortcut = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\TigerSqlCmd.lnk'
$Runtime = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.NETCore.App'
$VendorDir = Join-Path $env:APPDATA 'ItTiger.net'
$Store = Join-Path $VendorDir 'sqlserver-connections.json'
$OtherTool = Join-Path $VendorDir 'other-tiger-tool-settings.json'
$UserDir = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'TigerSqlCmdAcceptance'
$Utf8 = New-Object System.Text.UTF8Encoding($false)

$phases = New-Object System.Collections.Generic.List[object]

function New-Check {
    param([string] $Name, [string] $Code, [bool] $Passed, [string] $Message)
    $status = 'FAIL'
    if ($Passed) { $status = 'PASS' }
    Write-Host "$status $Code - $Message"
    [pscustomobject][ordered]@{ name = $Name; code = $Code; status = $status; message = $Message }
}

function Add-Phase {
    param([string] $Name, [object[]] $Checks)
    $status = 'PASS'
    if (@($Checks | Where-Object { $_.status -ne 'PASS' }).Count -gt 0) { $status = 'FAIL' }
    $phases.Add([pscustomobject][ordered]@{ name = $Name; status = $status; checks = @($Checks) })
}

function Get-MachinePath { [Environment]::GetEnvironmentVariable('Path', 'Machine') }

function Get-PathCount([string] $Entry) {
    @((Get-MachinePath) -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ieq $Entry.TrimEnd('\') }).Count
}

function Get-Registrations {
    <# Every Add/Remove Programs entry, in any view or hive, that names TigerSqlCmd. #>
    $roots = @($Uninstall, 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall')
    @(foreach ($root in $roots) {
        foreach ($item in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
            $values = Get-ItemProperty -LiteralPath $item.PSPath -ErrorAction SilentlyContinue
            if ($null -ne $values -and $null -ne $values.PSObject.Properties['DisplayName'] -and [string] $values.DisplayName -like 'TigerSqlCmd*') {
                $item.PSChildName
            }
        }
    })
}

function Get-RuntimeVersions { @(Get-ChildItem -LiteralPath $Runtime -Directory -Filter '10.*' -ErrorAction SilentlyContinue | ForEach-Object Name) }

function Invoke-Program {
    <# A program with its output kept as evidence: exit code, stdout, stderr. #>
    param([string] $Label, [string] $FilePath, [string[]] $Arguments)
    $out = Join-Path $Artifacts "$Label.stdout.txt"
    $err = Join-Path $Artifacts "$Label.stderr.txt"
    $quoted = @($Arguments | ForEach-Object { if ($_ -match '\s' -and $_ -notmatch '^".*"$') { '"' + $_ + '"' } else { $_ } })
    $process = Start-Process -FilePath $FilePath -ArgumentList $quoted -Wait -PassThru -NoNewWindow `
        -RedirectStandardOutput $out -RedirectStandardError $err
    [pscustomobject]@{
        exitCode = $process.ExitCode
        stdout = [IO.File]::ReadAllText($out)
        stderr = [IO.File]::ReadAllText($err)
    }
}

function Invoke-Setup {
    <# A TigerSetup run, quiet, with --json and its log kept as evidence. #>
    param([string] $Label, [string] $FilePath, [string[]] $Arguments)
    $all = @($Arguments | Where-Object { $_ -ne '--quiet' }) + @('--quiet', '--json', '--log', (Join-Path $Artifacts "$Label.log"))
    Invoke-Program -Label $Label -FilePath $FilePath -Arguments $all
}

function Split-Command([string] $Text) {
    <# A registered uninstall command: its quoted or unquoted program, and its arguments. #>
    $Text = $Text.Trim()
    if ($Text.StartsWith('"')) {
        $end = $Text.IndexOf('"', 1)
        return [pscustomobject]@{ program = $Text.Substring(1, $end - 1); arguments = @($Text.Substring($end + 1).Trim() -split '\s+' | Where-Object { $_ }) }
    }
    $parts = @($Text -split '\s+')
    [pscustomobject]@{ program = $parts[0]; arguments = @($parts | Select-Object -Skip 1) }
}

function Get-Hashes([string[]] $Paths) {
    $map = @{}
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) { $map[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        else { $map[$path] = $null }
    }
    $map
}

function Test-UserData([string] $Prefix, [hashtable] $Expected) {
    $changed = @(foreach ($path in $Expected.Keys) {
        $now = $null
        if (Test-Path -LiteralPath $path -PathType Leaf) { $now = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        if ($null -eq $now -or $now -ne $Expected[$path]) { Split-Path -Leaf $path }
    })
    New-Check 'Connection store and user files byte-identical' "$Prefix.user-data" ($changed.Count -eq 0) "$($Expected.Count) files checked; changed or missing: $(if ($changed.Count) { $changed -join ', ' } else { 'none' })."
}

function Invoke-NewSessionCommand([string] $Label, [string[]] $Arguments) {
    <# tiger-sqlcmd resolved from PATH as a new session would see it: the registry's machine and user PATH. #>
    $env:Path = (Get-MachinePath) + ';' + [Environment]::GetEnvironmentVariable('Path', 'User')
    $command = Get-Command 'tiger-sqlcmd.exe' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $command) { return [pscustomobject]@{ path = $null; exitCode = -1; stdout = ''; stderr = 'tiger-sqlcmd.exe is not on PATH' } }
    $run = Invoke-Program -Label $Label -FilePath $command.Source -Arguments $Arguments
    $run | Add-Member path $command.Source
    $run
}

function Test-Installed([string] $Prefix) {
    $checks = New-Object System.Collections.Generic.List[object]
    $registration = Get-ItemProperty -LiteralPath $Key -ErrorAction SilentlyContinue
    $checks.Add((New-Check 'Add/Remove Programs entry' "$Prefix.registration" ($null -ne $registration -and [string] $registration.DisplayVersion -eq $Version -and
        [string] $registration.Publisher -eq 'IT Tiger' -and [string] $registration.DisplayName -eq 'TigerSqlCmd') "HKLM ItTiger.TigerSqlCmd: $(if ($registration) { "$($registration.DisplayName) $($registration.DisplayVersion) by $($registration.Publisher)" } else { 'absent' })."))
    $registrations = @(Get-Registrations)
    $checks.Add((New-Check 'One TigerSqlCmd registration' "$Prefix.one-registration" ($registrations.Count -eq 1 -and $registrations[0] -eq 'ItTiger.TigerSqlCmd') "Entries: $($registrations -join ', ')."))
    $dll = Join-Path $Root 'tiger-sqlcmd.dll'
    $dllHash = $null
    if (Test-Path -LiteralPath $dll) { $dllHash = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash }
    $checks.Add((New-Check 'The candidate binaries are installed' "$Prefix.binaries" ($dllHash -eq [string] $Config.candidateDllSha256) "$dll SHA-256 $dllHash; candidate $($Config.candidateDllSha256)."))
    $terms = @('LICENSE.txt', 'THIRD-PARTY-NOTICES.md', 'PRIVACY.md', 'shell\tiger-sqlcmd-shell.cmd' | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Root $_)) })
    $checks.Add((New-Check 'Licence, notices, privacy statement and shell installed' "$Prefix.terms" ($terms.Count -eq 0) "Missing: $(if ($terms.Count) { $terms -join ', ' } else { 'none' })."))
    $checks.Add((New-Check 'One machine PATH entry' "$Prefix.path" ((Get-PathCount $Root) -eq 1) "Entries for $($Root): $(Get-PathCount $Root)."))
    $link = $null
    if (Test-Path -LiteralPath $Shortcut) { $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Shortcut) }
    $checks.Add((New-Check 'Start Menu shell' "$Prefix.shortcut" ($null -ne $link -and $link.TargetPath -ieq (Join-Path $Root 'shell\tiger-sqlcmd-shell.cmd')) "$Shortcut -> $(if ($link) { $link.TargetPath } else { 'absent' })."))
    $versionRun = Invoke-NewSessionCommand -Label "$Prefix-version" -Arguments @('--version')
    $checks.Add((New-Check 'tiger-sqlcmd from a new session' "$Prefix.command" ($versionRun.exitCode -eq 0 -and $versionRun.path -ieq (Join-Path $Root 'tiger-sqlcmd.exe') -and $versionRun.stdout -match "TigerSqlCmd version $([regex]::Escape($Version))") "$($versionRun.path) --version exit $($versionRun.exitCode): $($versionRun.stdout.Trim())"))
    $verify = Invoke-Program -Label "$Prefix-verify" -FilePath $Candidate -Arguments @('verify', '--json')
    $checks.Add((New-Check 'The installation verifies' "$Prefix.verify" ($verify.exitCode -eq 0) "Setup.exe verify exit $($verify.exitCode)."))
    $checks.ToArray()
}

function Invoke-RegisteredUninstall([string] $Label) {
    $registration = Get-ItemProperty -LiteralPath $Key -ErrorAction SilentlyContinue
    if ($null -eq $registration -or $null -eq $registration.PSObject.Properties['QuietUninstallString']) {
        return [pscustomobject]@{ command = 'none'; exitCode = -1 }
    }
    $command = Split-Command ([string] $registration.QuietUninstallString)
    $run = Invoke-Setup -Label $Label -FilePath $command.program -Arguments $command.arguments
    $run | Add-Member command ([string] $registration.QuietUninstallString)
    $run
}

function Test-Removed([string] $Prefix) {
    $checks = New-Object System.Collections.Generic.List[object]
    $checks.Add((New-Check 'Program files removed' "$Prefix.files" (-not (Test-Path -LiteralPath $Root)) "$Root exists: $(Test-Path -LiteralPath $Root)."))
    $checks.Add((New-Check 'Registration removed' "$Prefix.registration" (@(Get-Registrations).Count -eq 0) "Entries: $(@(Get-Registrations) -join ', ')."))
    $checks.Add((New-Check 'PATH entry removed' "$Prefix.path" ((Get-PathCount $Root) -eq 0 -and (Get-PathCount $LegacyCli) -eq 0) "Entries for $($Root): $(Get-PathCount $Root); for $($LegacyCli): $(Get-PathCount $LegacyCli)."))
    $checks.Add((New-Check 'Start Menu shell removed' "$Prefix.shortcut" (-not (Test-Path -LiteralPath $Shortcut)) "$Shortcut exists: $(Test-Path -LiteralPath $Shortcut)."))
    $checks.Add((New-Check 'Installer state removed' "$Prefix.state" (-not (Test-Path -LiteralPath $State)) "$State exists: $(Test-Path -LiteralPath $State)."))
    $runtimes = @(Get-RuntimeVersions)
    $checks.Add((New-Check '.NET runtime kept' "$Prefix.runtime" ($runtimes.Count -gt 0) "Microsoft.NETCore.App 10.x: $($runtimes -join ', ')."))
    $checks.ToArray()
}

try {
    # --- fresh install on a machine with no .NET runtime ------------------------------------------
    $checks = New-Object System.Collections.Generic.List[object]
    $runtimeBefore = @(Get-RuntimeVersions)
    $checks.Add((New-Check 'Clean guest' 'fresh.precondition' (@(Get-Registrations).Count -eq 0 -and -not (Test-Path -LiteralPath $Root) -and -not (Test-Path -LiteralPath $LegacyRoot)) "No TigerSqlCmd installed; .NET 10 runtimes before: $(if ($runtimeBefore.Count) { $runtimeBefore -join ', ' } else { 'none' })."))
    $install = Invoke-Setup -Label 'fresh-install' -FilePath $Candidate -Arguments @('install')
    $outcome = $install.stdout -replace '\s+', ' '
    $checks.Add((New-Check 'Quiet machine-wide install' 'fresh.install' ($install.exitCode -eq 0) ("Exit $($install.exitCode); outcome: " + $outcome.Substring(0, [Math]::Min(500, $outcome.Length)))))
    $runtimeAfter = @(Get-RuntimeVersions)
    $checks.Add((New-Check 'The .NET 10 runtime was acquired' 'fresh.runtime' ($runtimeBefore.Count -gt 0 -or ($runtimeAfter.Count -gt 0 -and $install.stdout -match 'Microsoft\.DotNet\.Runtime\.10')) "Before: $($runtimeBefore -join ', '); after: $($runtimeAfter -join ', ')."))
    foreach ($check in @(Test-Installed -Prefix 'fresh')) { $checks.Add($check) }
    $removal = Invoke-RegisteredUninstall -Label 'fresh-uninstall'
    $checks.Add((New-Check 'Registered quiet uninstall' 'fresh.uninstall' ($removal.exitCode -eq 0) "$($removal.command) -> exit $($removal.exitCode)."))
    foreach ($check in @(Test-Removed -Prefix 'fresh-cleanup')) { $checks.Add($check) }
    Add-Phase -Name 'fresh-install' -Checks $checks

    # --- the published Inno Setup installation, over seeded user data ------------------------------
    $checks = New-Object System.Collections.Generic.List[object]
    Add-Type -AssemblyName System.Security
    $secret = 'lab-synthetic-password-' + [Guid]::NewGuid().ToString('N')
    $protected = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($secret), $null, [Security.Cryptography.DataProtectionScope]::CurrentUser))
    $null = New-Item -ItemType Directory -Path $VendorDir, $UserDir -Force
    # Nothing listens on port 1: a connection that gets as far as the network proves its password decrypted.
    $storeJson = @"
[
  { "Name": "lab-sql", "Server": "tcp:127.0.0.1,1", "Authentication": 1, "Username": "lab-login",
    "EncryptedPassword": "$protected", "PasswordEncryption": 1, "Encrypt": 0, "ConnectTimeout": 5 },
  { "Name": "lab-integrated", "Server": "tcp:127.0.0.1,1", "Authentication": 0, "Encrypt": 0, "ConnectTimeout": 5 },
  { "Name": "lab-foreign", "Server": "tcp:127.0.0.1,1", "Authentication": 1, "Username": "lab-login",
    "EncryptedPassword": "Zm9yZWlnbi1ibG9i", "PasswordEncryption": 1, "Encrypt": 0 }
]
"@
    [IO.File]::WriteAllText($Store, $storeJson, $Utf8)
    [IO.File]::WriteAllText($OtherTool, '{ "owner": "another Tiger tool", "theme": "dark" }', $Utf8)
    [IO.File]::WriteAllText((Join-Path $UserDir 'report.sql'), "SELECT name FROM sys.databases;`r`n", $Utf8)
    [IO.File]::WriteAllText((Join-Path $UserDir 'results.csv'), "name`r`nmaster`r`n", $Utf8)
    $userData = Get-Hashes @($Store, $OtherTool, (Join-Path $UserDir 'report.sql'), (Join-Path $UserDir 'results.csv'))

    $legacyRun = Invoke-Program -Label 'legacy-install' -FilePath $Legacy -Arguments @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-',
        "/LOG=$(Join-Path $Artifacts 'legacy-install.log')")
    $checks.Add((New-Check "Published $LegacyVersion installed" 'legacy.install' ($legacyRun.exitCode -eq 0 -and (Test-Path -LiteralPath $LegacyKey) -and
        (Test-Path -LiteralPath (Join-Path $LegacyCli 'tiger-sqlcmd.exe')) -and (Test-Path -LiteralPath (Join-Path $LegacyRoot 'unins000.exe'))) "$($Config.legacy) exit $($legacyRun.exitCode); Inno registration: $(Test-Path -LiteralPath $LegacyKey)."))
    $checks.Add((New-Check 'Inno Setup PATH entry' 'legacy.path' ((Get-PathCount $LegacyCli) -eq 1) "Entries for $($LegacyCli): $(Get-PathCount $LegacyCli)."))
    $legacySql = Invoke-Program -Label 'legacy-sql' -FilePath (Join-Path $LegacyCli 'tiger-sqlcmd.exe') -Arguments @('run', '--non-interactive', '-c', 'lab-sql', '-q', 'SELECT 1')
    # 0.8.8 words a connection failure as the provider does; reaching the network at all is what
    # shows the password decrypted.
    $checks.Add((New-Check "$LegacyVersion decrypts the saved password" 'legacy.password' ($legacySql.exitCode -eq 4 -and $legacySql.stderr -match 'network-related or instance-specific error' -and $legacySql.stderr -notmatch 'decrypt') "Exit $($legacySql.exitCode): $($legacySql.stderr.Trim())"))
    Add-Phase -Name 'legacy' -Checks $checks

    # --- migration to the candidate ---------------------------------------------------------------
    $checks = New-Object System.Collections.Generic.List[object]
    $migration = Invoke-Setup -Label 'migration-install' -FilePath $Candidate -Arguments @('install')
    $outcome = $migration.stdout -replace '\s+', ' '
    $checks.Add((New-Check 'Quiet upgrade over the Inno Setup installation' 'migration.install' ($migration.exitCode -eq 0) ("Exit $($migration.exitCode); outcome: " + $outcome.Substring(0, [Math]::Min(700, $outcome.Length)))))
    $legacyLeft = @(Get-ChildItem -LiteralPath $LegacyRoot -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName.Substring($LegacyRoot.Length) })
    $leftText = 'nothing'
    if ($legacyLeft.Count) { $leftText = ($legacyLeft | Select-Object -First 10) -join ', ' }
    $legacyGone = -not (Test-Path -LiteralPath $LegacyKey) -and $legacyLeft.Count -eq 0 -and (Get-PathCount $LegacyCli) -eq 0
    $checks.Add((New-Check 'The Inno Setup installation is gone' 'migration.legacy-removed' $legacyGone "Inno registration: $(Test-Path -LiteralPath $LegacyKey); left under $($LegacyRoot): $leftText; old PATH entries: $(Get-PathCount $LegacyCli)."))
    foreach ($check in @(Test-Installed -Prefix 'migration')) { $checks.Add($check) }
    $checks.Add((Test-UserData -Prefix 'migration' -Expected $userData))
    $runLog = Join-Path $UserDir 'tiger-run.log'
    $sql = Invoke-NewSessionCommand -Label 'migration-sql' -Arguments @('run', '--non-interactive', '-c', 'lab-sql', '-q', 'SELECT 1', '--log-file', $runLog, '--log-level', 'Trace')
    $checks.Add((New-Check 'The password saved under 0.8.8 still decrypts' 'migration.password' ($sql.exitCode -eq 4 -and $sql.stderr -match "Could not connect to SQL Server using saved connection 'lab-sql'" -and $sql.stderr -notmatch 'decrypted') "Exit $($sql.exitCode): $($sql.stderr.Trim())"))
    $foreign = Invoke-NewSessionCommand -Label 'migration-foreign' -Arguments @('run', '--non-interactive', '-c', 'lab-foreign', '-q', 'SELECT 1')
    $checks.Add((New-Check 'A password from another account is reported as such' 'migration.foreign' ($foreign.exitCode -eq 4 -and $foreign.stderr -match 'could not be decrypted here') "Exit $($foreign.exitCode): $($foreign.stderr.Trim())"))
    $logText = ''
    if (Test-Path -LiteralPath $runLog) { $logText = [IO.File]::ReadAllText($runLog) }
    $leak = ($sql.stdout + $sql.stderr + $foreign.stdout + $foreign.stderr + $logText).Contains($secret)
    $checks.Add((New-Check 'No output or log contains the password' 'migration.no-secret' ($logText.Length -gt 0 -and -not $leak) "Log $runLog ($($logText.Length) characters); password found: $leak."))
    $checks.Add((New-Check 'The log records the error by type and message' 'migration.log-exception' ($logText -match 'Microsoft\.Data\.SqlClient\.SqlException: ' -and $logText -notmatch '   at ') 'SqlException recorded without a stack trace.'))
    $list = Invoke-NewSessionCommand -Label 'migration-list' -Arguments @('connection', 'list', '--non-interactive')
    $checks.Add((New-Check 'The saved connections are listed' 'migration.list' ($list.exitCode -eq 0 -and $list.stdout -match 'lab-sql' -and $list.stdout -match 'lab-integrated') "Exit $($list.exitCode)."))
    $userData[$runLog] = (Get-FileHash -LiteralPath $runLog -Algorithm SHA256).Hash
    Add-Phase -Name 'migration' -Checks $checks

    # --- uninstall after the migration ---------------------------------------------------------------
    $checks = New-Object System.Collections.Generic.List[object]
    $removal = Invoke-RegisteredUninstall -Label 'migration-uninstall'
    $checks.Add((New-Check 'Registered quiet uninstall' 'uninstall.run' ($removal.exitCode -eq 0) "$($removal.command) -> exit $($removal.exitCode)."))
    foreach ($check in @(Test-Removed -Prefix 'uninstall')) { $checks.Add($check) }
    $checks.Add((Test-UserData -Prefix 'uninstall' -Expected $userData))
    $checks.Add((New-Check 'The shared vendor folder is kept' 'uninstall.vendor' (Test-Path -LiteralPath $VendorDir -PathType Container) $VendorDir))
    $vendorParent = Join-Path $env:ProgramFiles 'ItTiger'
    $checks.Add((New-Check 'Nothing of either installation is left in Program Files' 'uninstall.program-files' (-not (Test-Path -LiteralPath $LegacyRoot)) "$LegacyRoot exists: $(Test-Path -LiteralPath $LegacyRoot); $vendorParent exists: $(Test-Path -LiteralPath $vendorParent)."))
    Add-Phase -Name 'uninstall' -Checks $checks
}
catch {
    Add-Phase -Name 'error' -Checks @(New-Check 'Payload completed' 'payload.error' $false ($_.Exception.Message + ' at ' + $_.InvocationInfo.PositionMessage))
}

$overall = 'PASS'
if (@($phases | Where-Object { $_.status -ne 'PASS' }).Count -gt 0) { $overall = 'FAIL' }
# ToArray rather than @(): Windows PowerShell 5.1 cannot build this object from a List inside @().
[pscustomobject][ordered]@{ status = $overall; version = $Version; legacyVersion = $LegacyVersion; phases = $phases.ToArray() } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $env:TIGERWINLAB_JOB_RESULT -Encoding UTF8

if ($overall -ne 'PASS') { exit 1 }
exit 0
