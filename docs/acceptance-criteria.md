# v1 acceptance criteria

These are the complete v1 acceptance criteria. Offline and HTTP core checks, local
development-server checks, and Unity integration checks are implemented. Consult the
../verification/results-http-2026-10-29.md for the completed HTTP milestone run; mobile/IL2CPP and production hosting
criteria remain pending.

## Contract checks

- Validate each example against the transport and example feature schemas.
- Reject wrong project/environment/platform or incompatible build/contract version.
- Reject invalid present registered data as a complete update.
- Resolve absent registered top-level keys to bundled defaults.
- Ignore unknown keys; preserve explicit empty/false/zero values according to feature schemas.
- Treat revision as opaque; accept a rollback publication with a new revision.
- Handle conditional fetch and a 304 with a lost cache.
- Restore ETag/freshness only for a validated representation and exact endpoint/context.
- Reject corrupt, unmatched, or future-dated freshness metadata safely.

## Client checks

- First launch offline returns valid defaults without waiting for network.
- Restart offline restores the last committed active config.
- A pending config survives restart and remains inactive until explicitly activated.
- Failed fetch/parse/validation/persistence preserves the active revision.
- Interrupted active/pending writes recover a complete valid record.
- Concurrent refresh requests share one operation and obsolete responses are ignored.
- Retry, timeout, throttling, and cooldown bounds remain correct across pause/resume.
- The first caller owns shared cancellation; cancelling a joiner does not abort others.
- Bound a request even if an injected transport ignores cancellation.
- Local staging supersedes older HTTP results without activating either automatically.
- HTTP metadata write failures preserve active/pending state.
- Document session-only cooldown/throttle deadlines; do not claim restart persistence.
- Activation changes all keys together; callbacks occur on Unity's main thread.
- Environment/project/build partitions cannot leak values between contexts.
- Release builds do not load debug overrides.

## Integration checks

- Install package into a clean Unity 6000.0.68f1 project with no cgSDK dependency.
- Integrate one game-defined feature using registered defaults and validation.
- Verify Editor behavior in Unity 6000.0.68f1 and device behavior on Android and iOS.
- Verify IL2CPP serialization, code stripping, callbacks, and persistence; record the tested build settings.
- Publish, fetch, activate, restart offline, and roll back that feature.
- Inspect status to distinguish active, pending, default, remote, and overridden values.

## Definition of done

An operational sample, automated contract/core tests, device results, documented supported
versions, and a repeatable publishing procedure must exist before calling v1 complete.
