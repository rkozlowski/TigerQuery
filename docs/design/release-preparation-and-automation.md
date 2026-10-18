# TigerQuery release preparation and automation

## Purpose and status

This document defines the repository contract for preparing and publishing a TigerQuery
release. It implements the TigerAiCore release model (`docs/release-model.md` in
TigerAiCore) for TigerQuery's artifact set and TigerSqlCmd distribution channels. The
implemented workflow is `.github/workflows/publish-packages.yml`; the operational checklist,
including where acceptance runs relative to the release commit, is
`docs/maintainers/releasing-tiger-sqlcmd.md`.

The release workflow prepares and publishes a version that is already committed. It does
not choose or edit the version, prepare feature notes, commit changes, publish a GitHub
Release, or submit WinGet manifests. `Version.props` remains authoritative.

## Release invariants

The following are hard requirements, not aspirations:

- A release begins only through one `workflow_dispatch` workflow with an intentional
  `version` input. There is no push, pull-request, tag, or schedule trigger.
- The workflow accepts only `main`, checks out the immutable dispatch commit, and uses
  that commit for build inputs, NuGet repository metadata, the annotated tag, the draft
  release, and artifact provenance.
- The input version must be syntactically valid and exactly equal the value in
  `Version.props`.
- All release payloads are built once in the validation job. Later jobs may only download,
  verify, publish, tag, and upload those bytes; they never rebuild or repack.
- Every payload has an expected filename and a recorded byte length and SHA-256. The
  machine-readable manifest also records the version and source commit. Its SHA-256 is
  carried separately across the workflow-artifact boundary, then all payload hashes are
  recalculated.
- No locally built package or installer is a release artifact. Local outputs are useful
  only for preparation and validation.
- NuGet packages, symbol packages, the TigerSqlCmd installer, checksum files, and WinGet
  preparation all originate from the same commit and validation job.
- Irreversible publication begins only after build, tests, documentation, package,
  installer, checksum, artifact-transfer, and WinGet manifest checks have succeeded.
- The release freezes the terms that apply to it: `LICENSE` and the TigerSqlCmd privacy
  statement `PRIVACY.md`, as they are at the release commit, are release assets recorded
  with kinds `License` and `PrivacyStatement`, and the WinGet manifests name exactly those
  copies.
- Automation removes repeatable mechanics while retaining explicit boundaries: NuGet
  publication, tag creation, draft creation, asset upload, human draft publication, and
  WinGet submission remain visible stages with precise failure behavior.

## Why TigerQuery is not TigerCli

TigerQuery has four NuGet identities rather than TigerCli's simpler package set:

- `ItTiger.TigerQuery.Core`;
- `ItTiger.TigerQuery`;
- `ItTiger.TigerQuery.CliCore`; and
- `ItTiger.TigerSqlCmd`.

The first three are libraries with package dependencies and `lib/net10.0` payloads.
TigerSqlCmd is a `DotnetTool` package whose application and dependencies are carried under
`tools/net10.0/any`; it must not accidentally become a library-style package or expose
package dependencies.

TigerSqlCmd also has a Windows installer, built with TigerSetup. Its stable package identity,
machine-wide administrative installation model, Program Files location, system PATH
behavior, .NET runtime prerequisite, migration from the earlier Inno Setup installation,
and uninstall cleanup are release contracts. That installer is a first-class GitHub Release
asset, not an optional local convenience build.

WinGet adds a second publication boundary. Its manifests require the SHA-256 of the final
installer and the immutable URL formed from the final tag and exact asset filename. A
draft release is not public, so pre-publication manifests can validate structure and the
expected URL but cannot prove that the live download exists. WinGet submission therefore
stays separate and manual initially.

## Workflow architecture

TigerQuery uses one unified workflow with two jobs, rather than a reusable build workflow
plus a thin publisher. A reusable workflow would add inputs and artifact-transfer
boundaries without creating a second legitimate consumer. Keeping construction and
publication together is the simplest design that enforces:

> Build once, validate once, publish the same bytes.

The existing filename `publish-packages.yml` is deliberately retained because the
NuGet.org Trusted Publishing policy identifies it. The validation job has read-only
repository permission. The publication job alone receives `contents: write` for the tag,
draft release, and assets, plus `id-token: write` for the short-lived NuGet credential.
The existing `release` environment remains the protected publication boundary. Concurrent
runs for one version are serialized and never cancelled in progress.

## Target flow

One dispatch performs this ordered flow:

1. Require a release version and dispatch from `main`.
2. Check out exactly `github.sha`, verify a clean checkout, and retain that SHA.
3. Require the input version to match `Version.props` exactly.
4. Inspect `v<version>` and fail if it already exists; report separately whether it
   resolves to this commit or a conflicting commit.
5. Reject the version if any of the four package IDs already exists on NuGet.org.
6. Guard and clean only repository-local release staging.
7. Restore tools and dependencies and build the solution in Release once.
8. Run the focused live-test filter against a unique missing connection store, requiring
   safe runtime skips before SQL activity and requiring that no store is created.
9. Run the full unconfigured Release suite against another unique missing store. Real
   configured SQL validation remains a release-preparation gate under `AGENTS.md` because
   a public hosted runner has no authorized E2E bootstrap store or SQL Server.
10. Build DocFX.
11. Pack all four exact project paths with `--no-build`, producing all four `.nupkg` and
    four `.snupkg` files.
12. Validate package IDs, versions, exact filenames, library payloads, symbol identities
    and PDBs, READMEs, icons, dependencies, forbidden payloads, TigerSqlCmd `DotnetTool`
    metadata, repository URL, and repository commit. Install, execute, and uninstall the
    staged .NET tool.
13. Provision the TigerSetup release `ItTiger.TigerSqlCmd.Installer/tigersetup.json` pins,
    refused unless its downloaded installer has the pinned SHA-256. Publish the already-built
    TigerSqlCmd output into installer staging with `--no-build`, and build
    `TigerSqlCmd-<version>-Setup.exe` from `ItTiger.TigerSqlCmd.Installer/TigerSetup.toml`
    straight into release staging.
14. Check the installer without running it: `tiger-setup verify`, and its declared contract
    from `tiger-setup inspect --json` — identity, version, machine scope, install root,
    registration key, payload, the frozen terms it carries, PATH, shortcut, the Inno Setup
    installation it replaces, and the .NET runtime prerequisite.
15. Generate the three WinGet manifests from that installer with `tiger-setup winget
    prepare` and `finalize`, its actual hash, and the expected `releases/download/v<version>/...`
    URL, and check them against everything the version implies. The runner has no WinGet
    client; `winget validate` runs in preparation and in TigerWinLab.
16. Add the WinGet manifest archive, `LICENSE`, and `PRIVACY.md`, then calculate SHA-256 and
    byte length for the twelve payloads and write `release-artifacts.json` and
    `SHA256SUMS.txt`.
17. Upload the fourteen exact release files as a workflow artifact and the three WinGet
    files as a separate workflow artifact. Retain the JSON manifest hash as a job output.18. In the publication job, download only that same-run artifact, verify the manifest
    hash, recalculate all payload hashes and lengths, and recheck version availability on
    NuGet.org immediately before publication.
19. Obtain a short-lived NuGet.org key through Trusted Publishing. Immediately before
    each explicit package push, revalidate the complete artifact set. Push in dependency
    order without duplicate-skipping.
20. After every package and symbol push succeeds, create annotated tag `v<version>` at
    exactly the validated commit, with a message naming the SHA-256 of
    `release-artifacts.json` and the workflow run, and push it without force.
21. Create a consistently titled draft GitHub Release using the verified tag and the
    checked-in `.github/release-notes/<version>.md`.
22. Upload the exact installer, its WinGet manifest archive, four NuGet packages, four
    symbol packages, `LICENSE`, `PRIVACY.md`, and both checksum files. Recheck GitHub's
    SHA-256 digest for every asset. Never use clobber.
23. Leave the release in draft state for human review and editing. A human publishes it.
24. After publication, run `eng/winget/Test-TigerSqlCmdWinGet.ps1`: it downloads and hashes
    the live installer, checks the manifests against its immutable URL, validates and
    exercises the package through WinGet in the TigerWinLab guest, and reports `PASS` or
    `FAIL`. Submit to `winget-pkgs` separately, and only after `PASS`.

The two checksum files describe the twelve publishable payloads. They are integrity
metadata rather than recursively self-hashing payloads; the JSON file's own transfer hash
is carried independently by the workflow job output.

## Package and artifact identity

The release set is closed: unexpected or missing files fail validation. Package upload
paths are written explicitly. The manifest binds each payload to the requested version
and source commit, and the publication checkout must itself equal that commit. Hashes are
checked after construction, after Actions artifact transfer, immediately before each
NuGet push, and against GitHub's release-asset digest after upload.

NuGet package immutability is fail-closed. Availability is checked before expensive work
and again at the publication boundary. `--skip-duplicate` is intentionally absent: an
existing version is a release-state conflict, not a success condition. Repository commit
metadata is required for all packages, including the tool package.

## Installer contract

`ItTiger.TigerSqlCmd.Installer/TigerSetup.toml` is the installer's package definition and
`BuildInstaller.ps1` its one build entry point. The version, description, and copyright are
evaluated from `Version.props` through the CLI project, and TigerSetup refuses a published
`tiger-sqlcmd.exe` that disagrees. The stable contract, asserted from the built installer by
`eng/release-automation/Assert-TigerSqlCmdInstaller.ps1`:

- package and Add/Remove Programs identity `ItTiger.TigerSqlCmd`, which is also the WinGet
  identifier and product code;
- machine scope only, an administrator's approval, `%ProgramFiles%\TigerSqlCmd`, x64;
- one system PATH entry for the install root, behind the `path` option, on by default;
- the .NET 10 runtime as the one prerequisite (`Microsoft.DotNet.Runtime.10`), detected under
  `%ProgramFiles%\dotnet\shared\Microsoft.NETCore.App` and, when missing, acquired from the
  WinGet catalog and installed only when its SHA-256 matches; `BuildInstaller.ps1` refuses a
  publish whose runtimeconfig needs another framework;
- `[legacy]` names the Inno Setup registration `ItTiger.TigerSqlCmd_is1` of 0.8.8 and earlier,
  so the first TigerSetup install removes that installation with its own quiet uninstaller
  before installing;
- `LICENSE.txt`, `THIRD-PARTY-NOTICES.md`, and `PRIVACY.md` in the payload, byte for byte the
  repository's files;
- no custom action, and no resource that refers to the connection store or other user data
  (`InstallerUserDataContractTests`), so neither the migration nor an uninstall touches it.

The builder is pinned, not whatever TigerSetup a machine has: `tigersetup.json` names one
release and its installer's SHA-256. The release runner installs exactly that release per
user, without PATH, through `eng/release-automation/Install-TigerSetup.ps1`; local builds
resolve `tiger-setup.exe` from PATH and refuse another version.

The runner never installs TigerSqlCmd. Installation behavior — the migration from the
published 0.8.8, a fresh install that acquires the runtime, the saved DPAPI-protected
password surviving, PATH, registration, and uninstall — is release-preparation acceptance in
TigerWinLab (`eng/lab/Test-TigerSqlCmdInstaller.ps1`), and release validation installs the
exact released bytes there through WinGet.

## Release notes

Each version has checked-in release notes, `.github/release-notes/<version>.md`, whose first
heading is `# TigerQuery <version>`. The workflow refuses a version without them
(`Assert-ReleaseNotes.ps1`) and uses them unchanged as the draft's notes. GitHub-generated
notes are not used. No release-specific notes or future version are hard-coded in the
workflow.
## Failure and rerun semantics

The workflow is idempotent where the destination exposes trustworthy identity and digest
state, but it never makes a destructive guess.

| State | Behavior and recovery |
| --- | --- |
| Validation, build, package, installer, DocFX, hash, or WinGet validation fails before NuGet publication | Fix the source or infrastructure, commit the fix, and dispatch the new exact commit. Nothing public was changed. |
| A package version appears between preflight and publication | The second availability check fails before any push. Choose a new version or investigate the conflicting publication. |
| One or more NuGet pushes succeeded and a later push failed | Stop. A normal rerun fails the already-published-version check. Download the retained same-run artifact, verify the manifest hash and published package state, then publish only genuinely missing exact files under explicit maintainer control. Do not rebuild, skip duplicates, tag, or release until all required NuGet identities and symbols are accounted for. |
| NuGet succeeds but tag or draft creation fails | A normal rerun intentionally fails NuGet preflight. Recover from the retained exact artifact and validated commit. Run the draft-release helper under maintainer control; it can create a missing annotated tag or accept the same annotated tag at the same commit. |
| Tag exists but no release exists | A tag at another commit, a lightweight tag, or a tag naming another release record is a hard conflict. The same annotated tag at the validated commit, naming the retained record's SHA-256, is safe for the helper to resume; it creates the missing draft. Never move or replace the tag. |
| Compatible draft exists and an upload failed | The helper verifies every existing asset's name, size, and GitHub SHA-256, skips identical assets, and uploads only missing assets. |
| Existing release asset has the wrong hash or incomplete state | Fail without `--clobber`. A maintainer must inspect the draft and retained artifact, remove the bad draft asset deliberately, and rerun the helper. Published release assets are never edited. |
| Workflow is rerun from a different commit with the same version | Version availability, tag target, package repository-commit metadata, and manifest commit checks fail. No tag or artifact is replaced. |
| Draft was already published | Automation refuses to edit it. Post-publication corrections require an explicit maintainer decision and normally a new version. |

`Publish-GitHubDraftRelease.ps1 -PlanOnly` validates the local artifact contract and prints
the intended tag, draft, and asset operations without calling GitHub. Its execution mode
is deliberately resumable for exact matching state, even though the main workflow's
strict NuGet preflight prevents a blind whole-workflow rerun after publication begins.
Draft lookup uses the authenticated list-releases API, which includes drafts for callers
with repository push access. That list is eventually consistent, so after creating a draft
the helper retries the lookup with a bounded backoff of about a minute before it fails;
`gh release upload` resolves a draft through the same list. The helper requires one exact tag
match, retains its numeric release ID, and uses `/releases/{release_id}` for metadata and asset inspection before and
after upload. It never uses the published-release-only tag endpoint to inspect a draft.

## WinGet's two stages

Before the draft is public, the workflow can prove manifest structure, package identity,
installer type and scope, dependency metadata, filename, SHA-256, and the deterministic
future GitHub URLs of the installer and the frozen terms. It cannot prove that an
unauthenticated client can download them. Preparation and release validation run the
TigerWinLab WinGet scenario on the candidate or the draft's installer with
`-CandidateInstallerPath`, which serves it to WinGet over loopback, so WinGet's install,
command, and uninstall path is proven before publication.

After human publication, `eng/winget/Test-TigerSqlCmdWinGet.ps1` closes that gap in one
command: it downloads the installer from the live URL, compares it with the manifests,
`release-artifacts.json`, and `SHA256SUMS.txt`, and then hands that downloaded payload to
TigerWinLab, which validates and exercises the WinGet install/command/uninstall path in a
clean Windows guest. Validating the published bytes rather than a local rebuild is the
point: it is the only way the artifact tested is the artifact users will get.

The environment is deliberately not this repository's concern. TigerQuery owns package
policy - what the manifests must say and what TigerSqlCmd must look like once installed -
and TigerWinLab owns the Windows guest. That separation is why this repository builds no
validation sandbox of its own. See `docs/maintainers/winget-tiger-sqlcmd.md`.

Only after a `PASS` is a separate manual WinGet PR appropriate. Neither the release
workflow nor the validation command authenticates to, forks, or submits to `winget-pkgs`.

## Boundaries that never move

A failure at any stage is a failure boundary to diagnose. None justifies falling back to
locally built artifacts, weakening hashes, skipping package conflicts, overwriting a tag or
asset, or automating WinGet submission.
