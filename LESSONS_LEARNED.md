# Lessons Learned

Current, non-obvious, TigerQuery-specific knowledge that prevents repeating costly mistakes.

## `dotnet test --no-build` can run a stale Debug build without saying so

**Area:** every `dotnet test --no-build` gate in `AGENTS.md`

**Status:** Active

**Symptom:** a test run after a `-c Release` build reported green, yet tests added in the
same session were never discovered.

**Cause:** the gate commands pass no `-c`, so `dotnet test --no-build` uses the Debug output.
When only Release was rebuilt, it ran the previous Debug assembly, which lacked the new
test classes. Nothing fails: the run just reports fewer tests.

**Do:** run a plain `dotnet build TigerQuery.sln --no-restore` (Debug) in the same session
before trusting a `--no-build` result, and confirm with `--list-tests` that new test classes
appear.

## An installer upgrade test between two builds of the same version upgrades nothing

**Area:** installer lifecycle acceptance (TigerWinLab), any candidate built without a version bump

**Status:** Active

**Symptom:** a TigerWinLab run installed the published 0.8.8, "upgraded" it with a 0.8.8
candidate built from `dev`, and every candidate behavior check failed: the upgraded install
still ran the published binaries.

**Cause:** Inno Setup replaces a versioned file only when the new file's version is higher.
`BuildInstaller.ps1` stamps every assembly with `Version.props`, so two builds of one version
carry identical file versions, and the setup log says `Same version. Skipping.` for each one.
Non-versioned files are replaced, so the install looks updated.

**Do not:** use a published release with the candidate's own version as the upgrade
baseline, or infer from a successful setup exit code that the candidate's binaries are
installed.

**Use instead:** upgrade from a release with a lower version than the candidate, and assert
that an installed binary's hash equals the candidate build's before testing behavior.

## A clean build does not prove a clean dependency graph

**Area:** dependency updates and release preparation

**Status:** Active

**Symptom:** `dotnet build --no-restore` reported zero warnings while
`System.Security.Cryptography.Pkcs` 9.0.13, with known high and moderate advisories, was in
every project's resolved graph.

**Cause:** NuGet's audit (`NU1901`–`NU1904`) runs during restore only. A build that skips
restore never prints it. Updating `Microsoft.Data.SqlClient` alone did not help either: even
7.1.1 still resolved an affected 9.0.x Pkcs, so the fix needed a direct reference.

**Do:** after any `PackageReference` change, run `dotnet restore` and
`dotnet list TigerQuery.sln package --vulnerable --include-transitive`, and inspect the packed
outputs, not only the project graph.
