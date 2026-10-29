# Versioning and releases

## Current release

Package: `com.biji.sdk` version `0.1.0-preview.4`.
Git tag: `v0.1.0-preview.4`. GitHub release: prerelease.

Install using Unity Package Manager > Install package from Git URL:

```text
https://github.com/viju-tag/bijiSDK.git?path=/clients/unity#v0.1.0-preview.4
```

Use tagged URLs when sharing. Branch URLs are for development and do not identify a fixed release. Tags must never be moved or reused for changed content.

## Version rules

- `clients/unity/package.json` is the source of truth for the SDK version.
- Use semantic versioning: MAJOR.MINOR.PATCH, with `-preview.N` while in preview.
- Increment the preview number for each published preview in the current release series.
- A future stable version drops the preview suffix only after an explicit release decision.
- For stable releases, use patch for compatible fixes, minor for compatible additions, and major for incompatible public API changes. During 0.x, document breaking changes explicitly and increment the minor version.
- Each release has a matching `v`-prefixed annotated Git tag, changelog entry, and GitHub release notes.
- Record validation scope and limitations; do not infer device coverage from Editor tests.

## Separate version identifiers

SDK version, game app build, config envelope schemaVersion, game configContractVersion, and publication revision serve different purposes. An SDK release does not automatically change the wire schema, game's contract, app build, or hosted configuration. Publication revisions remain unique opaque identifiers; never reuse a revision with changed data.

## Release procedure

1. Review the working tree and include only intended source, documentation, and tests. Exclude generated builds, caches, logs, credentials, and device evidence.
2. Update package version, changelog, current status, and pinned installation links.
3. Run checks appropriate to the changes. Preserve historical reports and the exact version/build they tested.
4. Commit the release with explicit user authorization for Git changes.
5. Create an annotated tag matching package.json, push the release commit and that tag, then publish matching GitHub release notes. Keep previews marked prerelease.
6. Verify the remote tag resolves to the release commit and the published package version matches.

## Validation provenance for preview.4

The Android APK tested on Nox and the physical Motorola phone reports preview.3. Runtime and sample C# source are unchanged in preview.4. This release adds the recorded tests and tooling, documentation, and version metadata; it does not claim a rebuilt preview.4 APK was device-tested. Real offline/reconnection was skipped. iOS testing is deferred for now.
