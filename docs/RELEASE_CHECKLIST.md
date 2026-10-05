# Release checklist

The canonical release workflow is .github/workflows/ci.yml. Releases use bare SemVer tags, for example 3.0.0, 3.0.0-alpha.1, or 3.0.1-beta.1.

## Before release

- Confirm the tag exactly matches the GitVersion SemVer value.
- Confirm the tag is protected and the production environment requires the configured human approval.
- Run the workflow manually with dry_run=true on a branch or tag to rehearse the complete validation path. The publish job must remain skipped, regardless of the publish input.

## Validation and artifacts

- Build and test the solution on Linux and Windows.
- Run the pinned Roslyn host matrix.
- Pack the three shipping projects once at the exact tag version.
- Verify exactly one .nupkg and one .snupkg for each package.
- Verify package API compatibility, package contents, clean-consumer mappings, trimming, Native AOT, checksums, and provenance.
- Every native validation command must succeed before the next command or artifact upload. A nonzero exit stops the stage and reports the failed operation or package.
- Ensure the Windows consumer restore includes the `win-x64` runtime before no-restore trimmed/AOT publishes.
- Use Node 24-compatible artifact actions: `actions/upload-artifact@v6` and `actions/download-artifact@v7`.
- Retain the uploaded package, checksum, and provenance artifacts.

## Publish

- Publish automatically only on a push of a valid SemVer tag. Manual production publishing requires publish=true and dry_run=false on main, release/**, or hotfix/**; selecting a tag for a manual dispatch does not authorize publishing.
- The publish job downloads and verifies the uploaded artifacts, then pushes those exact six files without duplicate suppression.
- A dry run must report the artifacts that would be published and must never call dotnet nuget push or mutate NuGet.
- Check each package push immediately. A failed push stops publication and names the package and exit code; later packages must not conceal an incomplete publication.
