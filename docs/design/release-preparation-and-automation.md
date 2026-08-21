# TigerQuery release preparation and automation

## Purpose and status

This document defines the repository contract for preparing and publishing a TigerQuery
release. It follows the release philosophy established by TigerCli, while accounting for
TigerQuery's larger artifact set and TigerSqlCmd distribution channels. The implemented
workflow is `.github/workflows/publish-packages.yml`; the operational checklist is
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
  installer, checksum, artifact-transfer, and WinGet validation have succeeded.
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

TigerSqlCmd also has an Inno Setup Windows installer. Its stable AppId, machine-wide
administrative installation model, default Program Files location, system PATH behavior,
runtime prerequisite behavior, reinstall identity, and uninstall cleanup are release
contracts. That installer is a first-class GitHub Release asset, not an optional local
convenience build.

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
13. Publish the already-built TigerSqlCmd output into installer staging with
    `--no-build`, compile Inno Setup in CI, and copy the exact output into release staging.
14. Validate installer filename, file version, description, stable AppId, product code,
    administrator/machine scope, default location, clean install, reinstall, command
    behavior, one system PATH entry, and clean uninstall.
15. Calculate SHA-256 and byte length for the nine payloads. Write
    `release-artifacts.json` and `SHA256SUMS.txt`.
16. Generate the three WinGet manifests from that installer, its actual hash, and the
    expected `releases/download/v<version>/...` URL; run `winget validate`.
17. Upload the eleven exact release files as a workflow artifact and the three WinGet
    files as a separate workflow artifact. Retain the JSON manifest hash as a job output.
18. In the publication job, download only that same-run artifact, verify the manifest
    hash, recalculate all payload hashes and lengths, and recheck version availability on
    NuGet.org immediately before publication.
19. Obtain a short-lived NuGet.org key through Trusted Publishing. Immediately before
    each explicit package push, revalidate the complete artifact set. Push in dependency
    order without duplicate-skipping.
20. After every package and symbol push succeeds, create annotated tag `v<version>` at
    exactly the validated commit and push it without force.
21. Create a consistently titled draft GitHub Release using the verified tag, a short
    curated header, and GitHub-generated notes.
22. Upload the exact installer, four NuGet packages, four symbol packages, and both
    checksum files. Recheck GitHub's SHA-256 digest for every asset. Never use clobber.
23. Leave the release in draft state for human review and editing. A human publishes it.
24. After publication, download and hash the live installer, regenerate or verify the
    manifests against its immutable URL, rerun `winget validate`, test through WinGet,
    and submit to `winget-pkgs` separately.

The two checksum files describe the nine publishable payloads. They are integrity
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

`BuildInstaller.ps1` reads `Version.props`, optionally requires an exact expected version,
and generates ignored Inno preprocessor definitions. It no longer rewrites the tracked
`Installer.iss`; therefore a CI build cannot disguise source drift. The stable source
contract remains:

- `AppId=ItTiger.TigerSqlCmd` and Apps & Features product code
  `ItTiger.TigerSqlCmd_is1`;
- `PrivilegesRequired=admin` and a machine-wide Program Files destination;
- x64-compatible installation, the CLI under `{app}\cli`, and one machine PATH entry;
- the runtime prerequisite derived from the published runtimeconfig;
- repeat installation as an upgrade/reinstall under the same identity; and
- complete command, registry, PATH, and file cleanup on uninstall.

The workflow does not use the Inno Setup version that happens to be on `windows-latest`.
It downloads the official 64-bit Inno Setup 7.1.0 installer from the upstream immutable
GitHub release, verifies its pinned SHA-256 and Authenticode publisher, installs it
silently, and verifies the exact registered version and `ISCC.exe` path. Direct download
is used instead of Chocolatey because the `innosetup` package does not express the required
major-version contract, and instead of WinGet because WinGet itself may need provisioning
on a hosted runner. The resolved absolute compiler path is passed explicitly to
`BuildInstaller.ps1`; local builds retain registry discovery for an already-installed
Inno Setup 7.

The hosted Windows runner is expected to be elevated and disposable, making full
install/reinstall/uninstall validation practical. A failure to provision Inno Setup, a
runner policy that removes elevation, or a runner-specific reboot/registry restriction is
an infrastructure failure to diagnose, not a reason to omit the gate silently.

## Draft notes and human review

`docs/maintainers/release-notes-header.md` supplies a short stable provenance and review
header. GitHub-generated notes are appended as a useful starting point. They are not
treated as authoritative feature documentation. Before publication, a maintainer must
edit the draft for user-visible changes, compatibility information, omissions, and noisy
commit-derived content. No release-specific notes or future version are hard-coded in the
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
| Tag exists but no release exists | A tag at another commit or a lightweight tag is a hard conflict. The same annotated tag at the validated commit is safe for the helper to resume; it creates the missing draft. Never move or replace the tag. |
| Compatible draft exists and an upload failed | The helper verifies every existing asset's name, size, and GitHub SHA-256, skips identical assets, and uploads only missing assets. |
| Existing release asset has the wrong hash or incomplete state | Fail without `--clobber`. A maintainer must inspect the draft and retained artifact, remove the bad draft asset deliberately, and rerun the helper. Published release assets are never edited. |
| Workflow is rerun from a different commit with the same version | Version availability, tag target, package repository-commit metadata, and manifest commit checks fail. No tag or artifact is replaced. |
| Draft was already published | Automation refuses to edit it. Post-publication corrections require an explicit maintainer decision and normally a new version. |

`Publish-GitHubDraftRelease.ps1 -PlanOnly` validates the local artifact contract and prints
the intended tag, draft, and asset operations without calling GitHub. Its execution mode
is deliberately resumable for exact matching state, even though the main workflow's
strict NuGet preflight prevents a blind whole-workflow rerun after publication begins.

## WinGet's two stages

Before the draft is public, the workflow can prove manifest structure, package identity,
installer type and scope, dependency metadata, filename, SHA-256, and the deterministic
future GitHub URL. It cannot prove that an unauthenticated client can download that URL.

After human publication, a maintainer must download the installer from the live URL,
compare it with `release-artifacts.json` and `SHA256SUMS.txt`, regenerate or verify the
manifests with `Prepare-TigerSqlCmdWinGet.ps1`, rerun `winget validate`, and exercise the
WinGet install/upgrade/command/uninstall path. Only then is a separate manual WinGet PR
appropriate. The release workflow does not authenticate to, fork, or submit to
`winget-pkgs`.

## First-live-run assumptions and risks

Static and local tests cannot completely emulate GitHub Actions. The first real release
run must pay particular attention to:

- download, hash/signature validation, silent installation, and discovery of the pinned
  Inno Setup 7 compiler;
- WinGet provisioning through pinned `Microsoft.WinGet.Client` when `winget` is absent;
- the .NET 10 SDK installed by `actions/setup-dotnet`, and the hosted image's Git and
  GitHub CLI commands, which are checked before publication begins;
- elevation and machine PATH/registry behavior on the hosted runner;
- NuGet.org Trusted Publishing still matching this repository, workflow filename,
  environment, and configured `NUGET_USER`;
- GitHub's release-asset REST response exposing SHA-256 digests after upload; and
- generated release-note behavior and the draft's suitability for human editing.

These are explicit failure boundaries. None justifies falling back to locally built
artifacts, weakening hashes, skipping package conflicts, overwriting a tag or asset, or
automating WinGet submission.

