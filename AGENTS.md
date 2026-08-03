# TigerQuery agent guidance

## Long-running validation commands

- Run potentially quiet commands such as `dotnet docfx docs/api-docfx/docfx.json` through a yielded or otherwise monitored execution.
- Poll at intervals no longer than 30 seconds and give the user a progress update at least once per minute.
- If an interrupted command may have left a child process running, identify the exact process and command line before stopping it or retrying.
- If DocFX is idle without producing output in the sandbox, stop only the identified DocFX process and retry with normal process/network access after requesting approval.

## Live SQL and E2E validation

Run TigerQuery live SQL/E2E tests with normal host execution. They require SQL Server
access, normal filesystem and user-profile access, child-process execution for
`tiger-sqlcmd`, tooling named-pipe/process communication, and access to the selected
connection store.

The expected bootstrap is `TigerSqlCmdApp.DefaultE2eBootstrapConnectionName`
(`tiger-sqlcmd-e2e`). It must contain all three exact metadata entries:

- `ittiger.e2e.enabled=true`
- `ittiger.e2e.bootstrap=true`
- `ittiger.e2e.allow-database-create=true`

Missing configuration must runtime-skip before SQL activity. Invalid, malformed,
ambiguous, or unauthorized configuration must fail. A SQL-authentication profile may
intentionally skip integrated-authentication-only tests. Orphaned databases are reported
only and must never be deleted automatically.

### Default-store mode

Unset the override so the harness uses `TigerSqlCmdApp.DefaultConnectionStoreFile` and
resolves `TigerSqlCmdApp.DefaultE2eBootstrapConnectionName`:

```powershell
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter "FullyQualifiedName~ItTiger.TigerQuery.Tests.Live" `
  --no-restore --no-build --logger "console;verbosity=normal" -m:1 /nodeReuse:false
```

### Environment-selected mode

Set the override to an isolated store. It outranks the application default but is only a
store-path override, not the E2E enable switch; the selected store must independently
contain the same expected bootstrap name and exact authorization metadata.

```powershell
$env:TIGERQUERY_CONNECTION_STORE_FILE = 'C:\temp\tigerquery-e2e.json'
dotnet test ItTiger.TigerQuery.Tests\ItTiger.TigerQuery.Tests.csproj `
  --filter "FullyQualifiedName~ItTiger.TigerQuery.Tests.Live" `
  --no-restore --no-build --logger "console;verbosity=normal" -m:1 /nodeReuse:false
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
```

### Unconfigured and final validation

Run the normal unconfigured full suite against a unique missing store, verify that the
store was not created, and always remove the process override:

```powershell
$unconfiguredStore = Join-Path $env:TEMP (Join-Path ('TigerQuery-unconfigured-' + [Guid]::NewGuid().ToString('N')) 'connections.json')
$env:TIGERQUERY_CONNECTION_STORE_FILE = $unconfiguredStore
dotnet test TigerQuery.sln --no-restore --no-build --logger "console;verbosity=normal" -m:1 /nodeReuse:false
$testExit = $LASTEXITCODE
$storeCreated = Test-Path -LiteralPath $unconfiguredStore
Remove-Item Env:TIGERQUERY_CONNECTION_STORE_FILE -ErrorAction Ignore
"UnconfiguredStoreCreated=$storeCreated"
exit $testExit
```

Run the remaining gates with:

```powershell
dotnet build TigerQuery.sln -c Release --no-restore -m:1 /nodeReuse:false
dotnet docfx docs/api-docfx/docfx.json
git diff --check
```

Do not repeatedly run the live workflow in a restricted sandbox that blocks SQL Server,
child processes, `File.Replace`, named pipes, or normal profile access. Sandbox-only TLS,
atomic-file replacement, Roslyn named-pipe, or child-process failures must be rerun with
normal host access before they are treated as product defects. After a normal-host run,
diagnose only the exact failing tests instead of repeatedly rerunning the entire suite.
