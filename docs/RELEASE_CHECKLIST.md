# Release checklist

The canonical release workflow is .github/workflows/ci.yml. Releases use bare SemVer tags, for example 2.0.0, 2.0.0-alpha.1, or 2.0.1-beta.1.

## Before release

- Confirm the tag exactly matches the GitVersion SemVer value.
- Confirm the tag is protected and the production environment requires the configured human approval.
- Run the workflow manually with dry_run=true to rehearse the complete validation path.

## Validation and artifacts

- Build and test the solution on Linux and Windows.
- Run the pinned Roslyn host matrix.
- Pack the three shipping projects once at the exact tag version.
- Verify exactly one .nupkg and one .snupkg for each package.
- Verify package API compatibility, package contents, clean-consumer mappings, trimming, Native AOT, checksums, and provenance.
- Retain the uploaded package, checksum, and provenance artifacts.

## Publish

- Publish only from a valid SemVer tag or an explicitly approved production workflow dispatch on main, release/**, or hotfix/**.
- The publish job downloads and verifies the uploaded artifacts, then pushes those exact six files without duplicate suppression.
- A dry run must report the artifacts that would be published and must never call dotnet nuget push or mutate NuGet.
