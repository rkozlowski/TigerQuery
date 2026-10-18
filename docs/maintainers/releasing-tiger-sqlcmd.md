# Releasing TigerQuery and TigerSqlCmd

This maintainer-only guide is intentionally excluded from DocFX. It follows the TigerAiCore
release model: broad acceptance runs while preparing, before the release commit freezes the
release; after the release action builds the release set once, validation proves those exact
artifacts rather than repeating acceptance. The workflow contract and recovery semantics are
in `docs/design/release-preparation-and-automation.md`. `Version.props` is the only release
version source.

## Branches

TigerQuery uses the permanent development branch workflow (see `AGENTS.md`):

- a release is prepared on `dev`; committing the prepared, green release changes to `dev`
  freezes nothing;
- the Architect promotes `dev` to `main` with a merge that keeps `dev`'s commits, never a
  squash or rebase merge, and the commit that promotion produces on `main` is the release
  commit;
- if `main` carried commits `dev` lacked, the promoted tree differs from the accepted one:
  rerun the acceptance that difference can affect before starting the release action;
- the release workflow, the tag, and the GitHub Release come from `main` only.

## 1. Prepare (on `dev`)

1. Set the version in `Version.props` and update the other intentional version reference:
   the `version` input default in `.github/workflows/publish-packages.yml`. Update the
   documentation the release changes. Do not manufacture release artifacts.
2. Run the cheap gates first: Debug build and the full unconfigured suite, Release build,
   dependency audit, release tooling tests, and `git diff --check` (`AGENTS.md`,
   *Verification gates*).
3. Then run the release acceptance against a local candidate built from the prepared tree.
   These are the checks whose failure can still change the release commit:
   - live SQL/E2E in default-store mode, under normal host access;
   - DocFX;
   - package and tool validation: pack the four packages outside the repository, run
     `Assert-TigerQueryLibraryPackages.ps1`, `Assert-TigerSqlCmdToolPackage.ps1`, and
     `Assert-ReleaseSymbolPackages.ps1`, install the tool into an isolated `--tool-path`
     from `--source`, and smoke `--version`, `--help`, `exec --help`, and
     `--completion pwsh`;
   - installer build and metadata (`BuildInstaller.ps1`, then
     `Assert-TigerSqlCmdInstaller.ps1` without `-ValidateBehavior`);
   - WinGet manifests: `eng\winget\Prepare-TigerSqlCmdWinGet.ps1 -Validate` against the
     candidate installer;
   - documentation claims checked against the behavior being released.
4. Leave the evidence and the proposed release commit message; under the `dev` workflow the
   prepared changes may be committed and pushed to `dev`.

Acceptance a candidate already passed is not repeated for a later change that cannot affect
it; a later change reopens exactly the acceptance it can affect. A local candidate is never a
release artifact.

## 2. Promote (Architect)

Promote `dev` to `main` as described under *Branches* and push `main`. Nothing runs on the
push; start the release action when it is done.

## 3. Release action (Architect starts it)

In GitHub Actions, run **Release TigerQuery** from `main` with the exact version from
`Version.props`. The workflow builds once, validates and records the closed artifact set,
publishes those exact NuGet bytes, creates the annotated tag `v<version>` at the workflow
commit with a message naming the SHA-256 of `release-artifacts.json` and the run, and
creates a draft GitHub Release carrying the verified assets. The `release` environment and
the NuGet Trusted Publishing policy remain in force.

NuGet publication is irreversible and happens inside the release action, before anyone
reviews the draft. A package defect found afterwards ships as the next version.

## 4. Release validation (before publishing)

Prove the draft, not a local rebuild:

1. Download every draft asset into a new empty directory outside the repository with an
   authenticated `gh release download v<version>`, which also finds draft releases.
2. Run `eng\release-automation\Assert-ReleaseArtifactManifest.ps1` over that directory with
   the release commit as `-ExpectedCommit`: the set is closed and every payload has the
   recorded name, length, and SHA-256.
3. Fetch the tag and confirm that it is annotated (`git cat-file -t v<version>` is `tag`),
   points at the release commit, and names the SHA-256 of the downloaded
   `release-artifacts.json` (`git tag -l --format='%(contents)' v<version>`).
4. Confirm the release commit is the promoted commit whose candidate step 1 accepted.

Report READY TO PUBLISH, or the failure. A failure here that the release commit already
carried means the check belongs in preparation; the draft is abandoned and the fix ships as
the next patch version.

## 5. Publish (Architect)

Review the draft. Edit the generated notes for accuracy, compatibility, and useful
user-facing detail, confirm it is still a draft and that the tag resolves to the release
commit, then publish it. Nothing publishes it automatically.

## 6. WinGet

After publication, run the WinGet validation against the live installer URL. See
[Preparing the TigerSqlCmd WinGet package](winget-tiger-sqlcmd.md); the whole step is two
commands and ends in a `PASS` or a `FAIL`. Only after `PASS`, submit the validated manifests
unchanged to `winget-pkgs` and follow its validation and review. The TigerQuery workflow
never submits that pull request.

The workflow artifact named `TigerSqlCmd-WinGet-<version>-<commit>` contains the
pre-publication manifests. They use the expected final URL and exact installer hash; the
live URL is what the post-publication validation proves.

## Partial failure

Do not blindly rerun after any NuGet push. The workflow intentionally rejects published
versions and never uses duplicate-skipping.

- If validation fails before publication, fix the cause on `dev`, promote again, and
  dispatch the new exact `main` commit.
- If publication is partial, retain the workflow run and download its release artifact.
  Verify the manifest hash from the validation log and determine exactly which NuGet
  packages and symbols exist. Under explicit maintainer control, publish only missing
  files from that retained artifact. Do not rebuild.
- If NuGet is complete but the tag, draft, or assets are incomplete, check out the exact
  validated commit and use the retained artifact with
  `eng/release-automation/Publish-GitHubDraftRelease.ps1`. Run `-PlanOnly` first. Execution accepts
  only the same annotated tag/commit naming the same release record, the same compatible
  draft, and identical existing asset digests; it uploads only missing assets. It finds
  drafts through the authenticated releases list, retains the numeric release ID, and
  rechecks that exact draft by ID. If the helper fix necessarily postdates the validated
  release commit, run it from the fixed checkout with `-AllowDifferentHeadForRecovery`; the
  retained manifest and tag must still match the original release commit exactly.
- Never move an existing tag. Never pass `--clobber` for a release asset. If a draft asset
  has different bytes, inspect and remove it manually only after confirming the retained
  artifact is authoritative. Never alter a published release asset.
- If the same version is associated with another commit, stop and choose a new version or
  resolve the release state explicitly.

The retained workflow artifacts expire after 30 days. Preserve them and their run logs
immediately when recovering a partial publication.
