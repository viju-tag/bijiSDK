# Integrating local and remote configuration

1. Install clients/unity/package.json in Unity 6000.0.68f1 using Package Manager.
2. Register typed ConfigDefinition<T> entries, explicit JSON converters, and validators.
3. Create ConfigOptions for project, environment, platform, build, and game contract.
4. Create ConfigRemoteOptions for the chosen configuration base URL.
5. On Unity's main thread, call UnityConfigClientFactory.Create(options, definitions,
   remoteOptions). Omit remoteOptions for a local-only client.
6. Await InitializeAsync and inspect the result. Defaults/saved data are available
   without waiting for network access.
7. Request RefreshAsync in the background. A valid update becomes pending.
8. At a safe point, await ActivatePendingAsync and check its result.
9. Read through Get<T> or capture one GetSnapshot for a transaction's consistent values.
10. On resume, request RefreshAsync again; the SDK enforces freshness/backoff.
11. Await in-flight work before replacing a client, and dispose it when ownership ends.

Import the Offline Config sample for local and HTTP controls. Its README includes commands
to initialize the local development publications, start the server, and publish updates.
Production hosting is still a separate decision.

## Update behavior

Fetching never activates settings automatically. Missing top-level registered keys use
defaults; invalid supplied values reject the full update. Unknown keys are ignored.
An expired freshness interval does not remove usable active configuration.

For Editor testing, force: true skips the freshness interval but still respects server
throttling, cooldowns, and request limits. A valid 304 leaves active/pending state unchanged.

StageJsonAsync remains available for local complete responses. It invalidates HTTP metadata
and supersedes an older in-flight response. Treat it as authoritative input, not an
untracked debug overlay. Restaging the active revision clears a different pending revision.

## Cancellation and lifetime

The first RefreshAsync caller owns cancellation of the shared operation. A later caller
may cancel its own wait. Dispose cancels client-owned work and suppresses queued callbacks.
Durable commits completed before cancellation remain successful.

The core does not automatically subscribe to application lifecycle events. Wire startup/
resume in the game, as the sample does, and choose activation timing explicitly.

## Development transport

The sample defaults to http://127.0.0.1:8765 with explicit development HTTP opt-in.
Loopback on a device refers to that device. Follow the sample/server documentation for
emulator or LAN addresses. The SDK does not change Android/iOS transport security settings.
Use HTTPS for release builds.

## Storage and verification

Unity stores data under Application.persistentDataPath/bijisdk, partitioned by hashed
project/environment/platform/build/contract context. ETag metadata also checks the exact
endpoint URL. Cooldowns/throttle deadlines are session-local.

To simulate a fresh install, stop the test client before removing only its test cache.
Editor checks do not replace Android/iOS device, IL2CPP, stripping, and filesystem tests.
