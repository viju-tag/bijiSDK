# Changelog

## 0.1.0-preview.4 - 2026-10-29

- Added repeatable Android IL2CPP build, live Pages checks, and lifecycle diagnostics.
- Recorded 15 passing automatic checks on Nox and a physical Motorola moto g45 5G (Android 15), plus hosted update/rollback and restart checks.
- Added a malformed HTTP response fault check and updated testing instructions.
- Established immutable release tags and pinned UPM installation links.
- SDK runtime and sample source are unchanged from the tested preview.3 APK; this release changes package metadata, documentation, and verification tooling.
- Real offline/reconnection testing remains skipped. iOS testing is deferred; broader Android coverage remains unverified.

## 0.1.0-preview.3 - 2026-10-29

- Added sample-only HTTPS GitHub Pages mapping for Editor, Android, and iOS test fixtures.

- Added HTTP refresh, UnityWebRequest transport, conditional ETags, and persisted fetch metadata.
- Added freshness/cooldown rules, bounded retries and timeouts, shared refresh, and stale-response protection.
- Extended the sample with remote refresh controls while preserving explicit activation.
- Added a local Python server, immutable publication/rollback CLI, and backend tests.
- Expanded standalone checks to 31 behavior groups and added live Unity HTTP verification tooling.
- Passed the final live Unity HTTP workflow and six transport fault checks; mobile/IL2CPP validation remains pending.

## 0.1.0-preview.2 - 2026-10-29

- Implemented typed definitions/defaults, strict response validation, and defensive snapshots.
- Added local JSON staging and explicit asynchronous activation.
- Added recoverable active/pending storage with checksums and deletion markers.
- Added Unity main-thread dispatch, a persistence factory, and an importable offline sample.
- Added automated core, filesystem, and Unity Editor checks.
- Declared com.unity.nuget.newtonsoft-json 3.2.1.
- Kept HTTP, publishing, debug overrides, and mobile/IL2CPP verification pending.

## 0.1.0-preview.1 - 2026-10-29

- Established package metadata, folder structure, and proposed specifications.
- Recorded Unity 6000.0.68f1 and Android/iOS as the target baseline.
- Initial scaffold contained no runtime implementation.
