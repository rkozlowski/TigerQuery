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

## An installer exit code does not prove which binaries are installed

**Area:** installer lifecycle acceptance (TigerWinLab)

**Status:** Active

**Symptom:** a TigerWinLab run installed the published 0.8.8, "upgraded" it with a 0.8.8
candidate built from `dev`, and every candidate behavior check failed: the upgraded install
still ran the published binaries, although setup exited 0.

**Cause:** the Inno Setup installers of 0.8.8 and earlier replaced a versioned file only when
the new file's version was higher, and two builds of one version carry identical file
versions. Any installer can leave different bytes in place than the test assumes — a skipped
file, a rolled-back step, a file kept because it was modified.

**Do not:** infer from a successful setup exit code that the candidate's binaries are
installed, or use a release with the candidate's own version as the upgrade baseline.

**Use instead:** assert that an installed binary's hash equals the candidate's
(`eng/lab/installer/accept.ps1` compares `tiger-sqlcmd.dll` with the one inside the candidate
installer) before testing behavior.
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
