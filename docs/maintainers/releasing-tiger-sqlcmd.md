# Releasing TigerQuery and TigerSqlCmd

This maintainer-only guide is intentionally excluded from DocFX. The design and recovery
contract is in `docs/design/release-preparation-and-automation.md`. `Version.props` is the
only release version source.

## Normal release

1. Prepare the next version locally. Update `Version.props` and all intentional
   version-dependent documentation or samples, but do not manufacture release artifacts.
2. Run the repository release gates, including the configured live SQL/E2E workflow under
   normal host access exactly as specified by `AGENTS.md`. Review package and installer
   changes locally when relevant.
3. Review, commit, and push the complete release preparation to `main`.
4. In GitHub Actions, run **Release TigerQuery** from `main` and enter the exact version
   from `Version.props`. Do not use a different ref.
5. Wait for validation and publication. The workflow builds once, validates and hashes
   the closed artifact set, publishes those exact NuGet bytes, creates annotated tag
   `v<version>` at the workflow commit, and creates a draft GitHub Release with verified
   assets. The `release` environment and NuGet Trusted Publishing policy remain in force.
6. Review the generated draft release. Edit generated notes for accuracy, compatibility,
   and useful user-facing detail. Confirm it is still a draft and that the tag resolves to
   the workflow commit.
7. Publish the draft manually.
8. Download the installer, packages, and checksum files from the public release. Verify
   every payload against `release-artifacts.json` and `SHA256SUMS.txt`.
9. Prepare and validate the WinGet package against the now-live installer URL. See
   [Preparing the TigerSqlCmd WinGet package](winget-tiger-sqlcmd.md); the whole step is
   two commands and ends in a `PASS` or a `FAIL`.
10. Only after that reports `PASS`, submit the manifests separately to `winget-pkgs` and
    monitor its validation and review. The TigerQuery workflow never submits that PR.

The workflow artifact named `TigerSqlCmd-WinGet-<version>-<commit>` contains the
pre-publication manifests. They use the expected final URL and exact installer hash, but
the live URL must still be verified after the draft is published.

## Partial failure

Do not blindly rerun after any NuGet push. The workflow intentionally rejects published
versions and never uses duplicate-skipping.

- If validation fails before publication, fix and commit the cause, then dispatch the new
  exact `main` commit.
- If publication is partial, retain the workflow run and download its release artifact.
  Verify the manifest hash from the validation log and determine exactly which NuGet
  packages and symbols exist. Under explicit maintainer control, publish only missing
  files from that retained artifact. Do not rebuild.
- If NuGet is complete but the tag, draft, or assets are incomplete, check out the exact
  validated commit and use the retained artifact with
  `eng/release-automation/Publish-GitHubDraftRelease.ps1`. Run `-PlanOnly` first. Execution accepts
  only the same annotated tag/commit, the same compatible draft, and identical existing
  asset digests; it uploads only missing assets. It finds drafts through the authenticated
  releases list, retains the numeric release ID, and rechecks that exact draft by ID. If
  the helper fix necessarily postdates the validated release commit, run it from the fixed
  checkout with `-AllowDifferentHeadForRecovery`; the retained manifest and tag must still
  match the original release commit exactly.
- Never move an existing tag. Never pass `--clobber` for a release asset. If a draft asset
  has different bytes, inspect and remove it manually only after confirming the retained
  artifact is authoritative. Never alter a published release asset.
- If the same version is associated with another commit, stop and choose a new version or
  resolve the release state explicitly.

The retained workflow artifacts expire after 30 days. Preserve them and their run logs
immediately when recovering a partial publication.
