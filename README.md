# bijiSDK

Standalone remote configuration SDK, starting with a Unity client and a shared
platform-independent backend contract.

## Install the versioned preview

In Unity Package Manager, choose **Install package from Git URL** and paste:

```text
https://github.com/viju-tag/bijiSDK.git?path=/clients/unity#v0.1.0-preview.4
```

Unity resolves the declared Newtonsoft dependency automatically. Git must be available to Unity.
See [versioning rules](docs/versioning.md).

## Current status: Android-tested preview

Version 0.1.0-preview.4 includes HTTP refresh, conditional requests/ETags, bounded retries,
timeouts, freshness/cooldown rules, and protection against stale responses. Downloaded
values still require explicit activation through the validated pending-state pipeline.

Typed defaults, strict JSON validation, immutable snapshots, recoverable storage,
and offline restart remain available. The Unity sample now supports fetching from the
local development server as well as staging local JSON.

Target baseline: Unity 6000.0.68f1, Android and iOS. Windows/.NET and live Windows
Unity Editor HTTP checks passed, including publish/activate/rollback and transport faults.
The ARM64 IL2CPP development APK passed 15 Pages/storage checks in an Android 12 Nox
emulator and on a physical Motorola moto g45 5G running Android 15. Manual activation,
background/resume, and saved-state relaunch also passed on the phone. The imported
sample passed an approved hosted update/rollback sequence, with each download staying
pending until activation and the final rollback surviving a process restart.
Offline/reconnection was skipped by the user; iOS execution remains unverified. See the separate
[emulator results](verification/results-mobile-2026-10-29.md) and
[physical Android results](verification/results-physical-android-2026-10-29.md) for
the exact build settings, evidence, and remaining checks.

A Python development server and publication/rollback CLI are included.
Production hosting is still undecided. Editor override tooling, A/B assignment, and
targeting remain deferred.

## Try the local HTTP workflow

From this project root, run:

    python backend/publishing/publish.py init-demo
    python backend/delivery/server.py

The init-demo command seeds the Unity sample context for Editor, Android, and iOS.
The server serves those publications at http://127.0.0.1:8765. Stop it with Ctrl+C.

1. In Unity 6000.0.68f1, use Package Manager > Add package from disk.
2. Select clients/unity/package.json.
3. Import the Offline Config sample from the package's Samples section.
4. Attach OfflineConfigSample to an empty GameObject and follow its README.
5. Fetch a published config, activate it explicitly, then reload saved state.

See [Development backend](backend/README.md) for publication and rollback commands.
The sample's local HTTP allowance is for development; production endpoints use HTTPS.
The package declares com.unity.nuget.newtonsoft-json 3.2.1.

## Layout

- contracts/: shared schemas, example responses, and compatibility cases.
- backend/: local HTTP server, publication/rollback CLI, and backend tests.
- clients/unity/: Unity package, runtime, transport adapter, and sample.
- tools/OfflineChecks/: standalone offline and HTTP behavior harness.
- verification/: repeatable Unity, HTTP, and filesystem checks plus generated artifacts.
- docs/: architecture, behavior, API, publishing, acceptance, and implementation status.

## Read first

1. [Implementation status](docs/implementation-status.md)
2. [Unity public API](clients/unity/Documentation~/public-api.md)
3. [Integration guide](clients/unity/Documentation~/integration.md)
4. [Shared API contract](docs/api-contract.md)
5. [Client behavior specification](docs/client-behavior.md)
6. [Verification](verification/README.md)
7. [Open decisions](docs/open-decisions.md)

The next production decisions are hosting, publication access, and supported-device
verification. Future native clients will implement the same shared wire contract.

bijiSDK does not depend on cgSDK or the MergeFever project.
Source repository: https://github.com/viju-tag/bijiSDK (public during testing).

## Mobile tests with GitHub Pages

See [GitHub Pages testing](docs/github-pages-testing.md) for HTTPS setup and device checks.
Only the static `site/` test fixtures are deployed by the Pages workflow.
