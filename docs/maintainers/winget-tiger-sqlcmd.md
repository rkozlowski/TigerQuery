# Preparing the TigerSqlCmd WinGet package

This maintainer-only guide is intentionally excluded from DocFX. It covers step 9 of
[Releasing TigerQuery and TigerSqlCmd](releasing-tiger-sqlcmd.md): turning a published
GitHub release into a `winget-pkgs` pull request that has actually been proven to work.

Everything here runs after the GitHub release is **published**. Manifests must name the
live immutable asset URL, and that URL does not resolve while the release is a draft.

## The two commands

Run both from an **elevated** PowerShell 7 session at the repository root. Elevation is
TigerWinLab's requirement, not this repository's: Hyper-V management needs it.

```powershell
.\eng\winget\Prepare-TigerSqlCmdWinGet.ps1
.\eng\winget\Test-TigerSqlCmdWinGet.ps1 -Version <version>
```

`Prepare-TigerSqlCmdWinGet.ps1` writes the three manifests to
`artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\<version>\` from `Version.props`, the
built installer, and the deterministic release URL. You can skip it and reuse the
`TigerSqlCmd-WinGet-<version>-<commit>` workflow artifact instead; the manifests are
byte-identical either way.

`Test-TigerSqlCmdWinGet.ps1` is the gate. It exits `0` on `PASS` and `1` on `FAIL`, prints
a check table, and leaves both readings of the run in
`artifacts\winget\validation\<version>\`:

| File | |
| --- | --- |
| `result.json` | the machine-readable record — verdict, counts, every check, installer digests, and the submission set |
| `summary.txt` | the same report as printed, so the record outlives the terminal |
| `tigerwinlab-spec.json` | the specification handed to the lab for this run |
| `tigerwinlab-result.json` | the lab's own result envelope |
| `tigerwinlab-artifacts\` | the lab's logs and evidence |
| `published\` | the installer downloaded from the release, which is what was installed |

Useful switches:

| Switch | Effect |
| --- | --- |
| `-Json` | Emits the result record instead of the table. |
| `-SkipLab` | Manifest and published-asset checks only. Fast, and never a submission-ready `PASS`. |
| `-TimeoutMinutes` | Bounds the guest scenario. Default 45. |
| `-TigerWinLabRoot` | Where TigerWinLab lives, if it is not `$env:TIGERWINLAB_ROOT` or a sibling checkout. |

## What it proves

Three things have to be true before a `winget-pkgs` pull request is honest, and the run
covers all three. Every check is named in the table and in `result.json`.

**`manifest/*` — the manifests say what this release implies.** Package identity, the same
version in all three documents, `ManifestType`/`ManifestVersion` agreement, the default
locale, installer type `inno`, machine scope, x64, the immutable asset URL, a well-formed
SHA-256, the `AppsAndFeaturesEntries` product code and display version, the `tiger-sqlcmd`
command, the `Microsoft.DotNet.Runtime.10` dependency this framework-dependent package
needs, the release-notes URL, and UTF-8 without a byte-order mark.

**`release/*` — the published asset is the one the manifests describe.** The asset is
downloaded from the immutable URL exactly as an unauthenticated client would, hashed, and
compared with the manifest's `InstallerSha256`, with `SHA256SUMS.txt`, with
`release-artifacts.json`, and with the locally retained installer. The last three are
cross-checks against `artifacts\winget-input\v<version>\`; if that directory is absent
they warn rather than fail, because the published asset and the manifest are the pair that
actually matters.

**`lab/*` — WinGet can really install it.** The downloaded release asset — not a local
rebuild — is handed to TigerWinLab, which restores its Windows 11 guest to a clean
checkpoint and runs the WinGet scenario there: `winget validate` on the manifest set, the
declared dependency, a local-manifest install with hash verification enforced, installed
files, version, machine `PATH`, registration, the command's own smoke checks, `winget`
uninstall, and cleanup. It also runs two refusal probes, so a green result is not green by
construction: `winget validate` must reject a manifest with an invalid installer type, and
`winget install` must refuse a manifest whose hash has been altered.

A `WARN` is something to read, not something that blocks a submission. Only a `FAIL` does.

## The submission

A `PASS` means these three files are ready, unchanged, for a `winget-pkgs` fork at
`manifests/i/ItTiger/TigerSqlCmd/<version>/`:

```text
artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\<version>\ItTiger.TigerSqlCmd.installer.yaml
artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\<version>\ItTiger.TigerSqlCmd.locale.en-US.yaml
artifacts\winget\manifests\i\ItTiger\TigerSqlCmd\<version>\ItTiger.TigerSqlCmd.yaml
```

`result.json` records each file's path, length, and SHA-256 under `submission.files`, so
the files submitted can be shown to be the files validated. Copy them verbatim — a manifest
edited after the run is a manifest nothing validated.

**Opening the pull request is a human step and stays one.** Nothing in this repository
authenticates to, forks, or submits to `microsoft/winget-pkgs`, and nothing should: that
account is a person's, and the submission is a public act. Fork `microsoft/winget-pkgs`,
copy the three files to the path above, commit, open the pull request, and follow its
validation and review.

## Prerequisites and failures

TigerWinLab must be checked out and provisioned. It is found through `-TigerWinLabRoot`,
then `$env:TIGERWINLAB_ROOT`, then a `TigerWinLab` checkout beside this repository. See
that repository's `README.md`; `.\New-TigerWinLab.ps1` builds the guest and
`.\Test-TigerWinLab.ps1` reports whether it is ready.

Failures worth recognising:

- `lab/result` says no result was written — the lab never ran. Confirm the session is
  elevated and the guest is provisioned.
- `lab/scenario` says TigerWinLab is in use — another process holds the lab's exclusive
  lease. There is one mutable VM; wait for it.
- `lab/watchdog` fired — the scenario outlived its own timeout plus ten minutes. Its
  artifacts are under `artifacts\winget\validation\<version>\tigerwinlab-artifacts\`.
- `release/asset-sha256` failed — the manifests and the published release disagree.
  Never adjust the manifest to match a surprise; establish which artifact is wrong first.

Nothing in this flow installs anything on the host or changes the host's WinGet settings.
The install happens in the lab guest, which is discarded afterwards.

## Reusing this for the next release

The flow is version-driven, not release-specific. For the next version, publish the
release, then run the same two commands with the new version. The lab specification is
generated per run into `artifacts\winget\validation\<version>\tigerwinlab-spec.json` from
`eng\winget\tiger-sqlcmd.labspec.template.json`, which is where TigerSqlCmd's installed
shape is described — expected files, machine `PATH` entry, runtime dependency, and smoke
commands. Edit the template when the product's installed shape changes; the version, the
manifest path, the installer path, and the expected URL are filled in for you.

`eng\winget\tests\TigerSqlCmdWinGet.Tests.ps1` covers the manifest reasoning, the generated
specification, and the verdict, with no VM and no network. Run it after touching anything
under `eng\winget\`.
