# Verification

## Standalone core and HTTP checks

From the project root:

    dotnet run --project tools/OfflineChecks/BijiSDK.OfflineChecks.csproj

Requires .NET SDK 9. The standalone harness restores Newtonsoft.Json 13.0.3 from
NuGet. Unity uses its separate declared package dependency, Newtonsoft 3.2.1.

The harness compiles runtime sources directly while excluding Unity adapters.
The current suite contains 35 behavior groups: 15 offline, 16 HTTP coordination,
and four Pages adapter groups. Offline coverage includes defaults, snapshots, staging/activation,
restart, validation, corruption recovery, context isolation, cancellation, and failures.
HTTP coverage uses injected transports/clocks to exercise refresh policy and ordering.

## Filesystem checks

    dotnet run --project verification/storage/harness/StorageTests.csproj

Twelve checks exercise the actual file adapter. See storage/README.md.
Generated data remains inside verification/storage.

## Development backend checks

    python -m unittest discover -s backend/tests -v

Fifteen stdlib Python tests exercise local delivery, ETags/304, compatible revision
selection, publishing, rollback, validation, atomic pointer failures, and publication locks.
Tests stop their temporary loopback servers and clean their isolated test directories.

## Unity checks

In PowerShell:

    .\verification\Run-UnityChecks.ps1

The optional -UnityPath selects the executable; the default is Unity 6000.0.68f1
installed through Unity Hub on Windows. The script updates a separate UnityProject,
imports the local package, copies checks/sample sources, and runs Unity in batch mode.
It waits for the process and returns its exit code.

The offline integration check covers actual Unity main-thread callbacks and real-file
restart. The sample is compiled; this does not constitute an interactive UI/device test.

Output: unity-editor-checks.log. A successful run includes:
BIJISDK_UNITY_CHECKS_COMPLETE failures=0

## Live HTTP Unity workflow

    .\verification\Run-HttpUnityChecks.ps1

This wrapper creates an isolated publication store under verification/http-runs,
initializes sample configs, starts short-lived loopback delivery/fault servers, and
runs the Unity checks against those real HTTP endpoints. It restores temporary
environment variables and stops its servers on completion.

The live run exercises fetch/activation, publication update, ETag response, restart,
rollback, and Unity transport fault handling. The fault server supplies oversized bodies,
invalid UTF-8, malformed JSON, delayed responses, redirects, and 429 Retry-After responses.
It has no publishing endpoint and binds only to loopback.

The earlier HTTP milestone passed with BIJISDK_UNITY_CHECKS_COMPLETE failures=0.
That historical run covered 31 groups, main-thread/persistence integration, live
publication, ETag/activation/rollback/offline behavior, and six transport fault checks.
See [HTTP milestone results](results-http-2026-10-29.md).

The current runner includes the four Pages groups, for a total of 35, and seven
controlled transport cases: byte limit, invalid UTF-8, timeout, cancellation, blocked
redirect, throttling headers, and malformed JSON. The seventh case receives an HTTP 200
body through UnityWebRequest, rejects it as a protocol error during staging, and checks
that defaults and empty active/pending state remain intact. Check the latest run log
before treating a newly added case as passed.

## Previous verified scope

- Standalone core/HTTP/Pages behavior: 35 groups passed.
- Windows filesystem adapter: 12 checks passed.
- Local development backend: 15 tests passed.
- Offline Unity Editor compilation, callbacks, and disk restart: previously passed.
- Earlier live HTTP Unity milestone: passed, including the original six transport cases.

Generated UnityProject, http-runs, and build outputs are verification artifacts, not
SDK dependencies. Unity may download package dependencies when not cached.

Android/iOS device execution, mobile IL2CPP behavior, production hosting/authentication,
and interactive sample playtesting are not established by these Windows Editor checks.

## GitHub Pages sample checks

The console and Unity runners also execute four Pages adapter groups: context mapping,
isolation, cancellation, and explicit activation/validation. The console total is now
35 groups. Static fixture validation uses the backend envelope and feature validators.
See [mobile setup](../docs/github-pages-testing.md) for device tests.

## Dedicated mobile test project

Run from the SDK root using Unity 6000.0.68f1:

```powershell
.\verification\Run-MobileUnityChecks.ps1 -Mode Prepare
.\verification\Run-MobileUnityChecks.ps1 -Mode Editor
.\verification\Run-MobileUnityChecks.ps1 -Mode Android
```

`Prepare` creates the separate `verification/MobileUnityProject`, adds the local UPM
package, imports its Offline Config sample through Package Manager, and saves
`Assets/Scenes/BijiSDKMobileTest.unity`. It confirms Newtonsoft 3.2.1 resolves
transitively: the generated manifest has no direct Newtonsoft entry, and the lock file
must record it at depth 1 or greater. This is separate from the older `UnityProject`.

The dedicated application identifier is `com.biji.sdk.verification.d20261029`.
The Android build uses IL2CPP, ARM64, Low managed stripping, a development APK, minimum
API 23, target API 35, and HTTPS without a cleartext exception. Check device ABIs before
building/installing; this runner does not produce an x86/x86_64 variant.

The default output directory is `verification/mobile-runs/2026-10-29`; override it with
`-OutputRoot`. Outputs include package-import evidence, Unity logs,
`android-build-summary.json`, and `BijiSDKMobileTest-arm64-development.apk` when the
build succeeds. Editor JSON results appear under `editor-check-<id>/results.json`.
Generated project/output directories are ignored by Git. Reusable sources remain in
`verification/mobile/` and the runner script.

There are three separate clients/storage scopes in the saved scene:

- The automatic 15-check suite uses a fresh unique storage directory. It checks live
  Pages fetch/ETags, explicit activation, rejection, main-thread callbacks, disk
  recreation, and local update/rollback replacement. Its local revisions do not publish
  hosted changes. Look for `BIJISDK_MOBILE_CHECKS_COMPLETE ... passed=15 failures=0`.
- The imported sample uses its normal `Application.persistentDataPath/bijisdk` cache.
  Startup refresh is disabled so defaults or restored values can be inspected first;
  refresh and activation are manual. Resume refresh remains enabled.
- The lifecycle probe uses `mobile-verification/relaunch`. Its buttons refresh and
  activate this separate client, and `BIJISDK_LIFECYCLE` records launch/background/resume
  state. Once saved active data exists, a later launch skips the automatic network suite
  and leaves probe refresh manual. `launch_before_network` with
  `ReadyWithSavedConfig` provides the startup restoration evidence.

On Android, suite results are written under
`Application.persistentDataPath/mobile-verification/runs/<id>/results.json`. A passing
automatic suite does not prove background/resume, process relaunch, or true offline
behavior. Follow the [device steps](../docs/github-pages-testing.md) and capture logcat.
Label emulator results explicitly; keep physical-device results separate. Have the user
operate connectivity for real offline/reconnection checks. Do not erase existing app
data or silently change system/device networking.

## Physical Android results — 2026-10-29

The same APK used for the emulator run passed all 15 automatic checks on a physical
Motorola moto g45 5G running Android 15 / API 35 with native ARM64 execution. Its
SHA-256 remained `C08038BC99E2F7CB3B8F9BBEEEEEE97E9466981529D05DDAB0FFCB26420C4BB1`;
no code change or rebuild was needed. It uses Unity 6000.0.68f1, IL2CPP, ARM64,
Low managed stripping, a development APK, minimum API 23, and target API 35.

The dedicated package was absent before installation. Manual sample and lifecycle
probe checks verified defaults 80/180, live Pages data remaining pending, explicit
activation to 100/120, repeated refresh without duplicate application, background/resume,
and restoration after force-stop/relaunch. The automatic suite verified local
update/rollback file replacement on the native device as well.

See [physical Android results](results-physical-android-2026-10-29.md) for the exact
evidence. Keep the earlier [emulator results](results-mobile-2026-10-29.md) separate.
The imported sample also passed a separately approved hosted update/rollback sequence:
the update to 125/120 stayed pending while active values remained 100/120, and the
rollback to 100/120 stayed pending while active values remained 125/120. Each required
explicit activation, and the final rollback restored after another process restart.
The lifecycle probe retained its prior active rollback; it was not used to exercise
these new hosted revisions. Offline/reconnection remained skipped at the user's request.
Controlled mobile fault responses, other device/OS versions, higher stripping, and
background/resume during an active refresh remain untested on physical hardware.

Windows can inspect the iOS code/fixture but cannot establish Xcode signing or iPhone
execution. This installation also lacks iOS Build Support. Finish the iOS steps in the
mobile guide on a Mac with a supported Xcode, authorized signing, and an iPhone.
