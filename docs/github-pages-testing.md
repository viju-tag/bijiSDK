# GitHub Pages mobile testing

The test site is intended to be served at `https://viju-tag.github.io/bijiSDK/`.
The `site/` directory contains only public demonstration configs and a small index page.
Repository visibility is public for this testing phase. A later move to private source
and different hosting requires a separate repository/hosting change.

## Unity sample setup

1. Add `clients/unity/package.json` through Package Manager and import the Offline Config sample.
2. Attach `OfflineConfigSample` to an empty GameObject.
3. Enable `useRemoteServer` and `useGitHubPages`.
4. Set `pagesBaseUrl` to `https://viju-tag.github.io/bijiSDK/`.
5. Pages mode always enforces HTTPS and ignores `allowDevelopmentHttp`; no cleartext exception is needed.
6. Run in the Editor or build for Android/iOS. The sample selects the matching platform.

The sample context is project `bijisdk-offline-sample`, environment `development`,
app build `120`, contract version `1`. An existing saved config may replace bundled
defaults on startup. For a clean first-launch test use a fresh installation/data directory.

The Pages transport maps the logical request to a static file, for example:

`https://viju-tag.github.io/bijiSDK/configs/bijisdk-offline-sample/development/android/contract-1/config.json`

The shared backend API stays unchanged. This adapter is a sample-only option. Pages
cannot select a response from query parameters or run the Python delivery server.
The client still validates the returned platform, project, environment, build range,
and contract version. The supplied fixtures allow app build 120 only.

## Device checklist

Use the dedicated project below for repeatable execution and evidence. Record each test
as passed, failed, blocked, or not run; keep Android and iOS results separate.

Record results independently for Android and iOS, including IL2CPP builds. Hosted JSON
availability or passing Editor tests does not establish mobile compatibility.

## Prepare and build the isolated test app

From the SDK root in PowerShell:

```powershell
.\verification\Run-MobileUnityChecks.ps1 -Mode Prepare
.\verification\Run-MobileUnityChecks.ps1 -Mode Editor
.\verification\Run-MobileUnityChecks.ps1 -Mode Android
```

`Prepare` creates `verification/MobileUnityProject`, installs the local SDK through
UPM, imports the real Offline Config sample through Package Manager, and saves
`Assets/Scenes/BijiSDKMobileTest.unity`. Newtonsoft 3.2.1 must resolve automatically
through bijiSDK; the script rejects a direct Newtonsoft manifest entry or a missing
transitive lock entry. The application identifier is
`com.biji.sdk.verification.d20261029`, separate from the original game.

`Editor` runs 15 checks using real Pages HTTPS and disk storage. `Android` builds a
development APK using IL2CPP, ARM64, Low managed stripping, minimum API 23, and target
API 35. Inspect connected device ABIs before choosing this build; an ARM64 APK is not
evidence of native x86/x86_64 support. Do not install a new emulator or change the
toolchain as part of a normal verification run.

The default outputs are under `verification/mobile-runs/2026-10-29`:

- `package-import-result.txt`, `unity-package-import.log`, and `unity-project-setup.log`.
- `unity-pages-editor.log` and `editor-check-<id>/results.json`.
- `unity-android-build.log`, `android-build-summary.json`, and, on build success,
  `BijiSDKMobileTest-arm64-development.apk`.

Use `-OutputRoot <directory>` to select another evidence directory inside the SDK.
The generated project, logs, builds, and caches are ignored by Git. None of these
commands modify the original game project or publish hosted fixtures.

## Understand the saved scene

The scene has independent storage for three purposes:

| Component | Storage and behavior |
| --- | --- |
| Imported Offline Config sample | Uses `Application.persistentDataPath/bijisdk`. Pages mode is enabled. Startup refresh is disabled; inspect defaults/saved data before pressing refresh. Resume refresh stays enabled. |
| Automatic suite | Uses a fresh unique directory under `mobile-verification/runs` in players. The 15 checks fetch Pages, verify pending/activation/ETags, reject wrong context/invalid values/malformed JSON, recreate clients, and exercise local update/rollback file replacement. It never changes hosted fixtures. |
| Lifecycle probe | Uses `mobile-verification/relaunch`. Its own refresh and activation buttons control persistent test state, and its launch/background/resume logs are separate from the sample. |

On a fresh probe launch, the automatic suite runs with its own storage. A passing run
ends with `BIJISDK_MOBILE_CHECKS_COMPLETE ... passed=15 failures=0` and writes
`mobile-verification/runs/<id>/results.json` beneath `Application.persistentDataPath`.
The suite does not activate the lifecycle probe or imported sample.

After the probe has saved active values, subsequent launches log
`BIJISDK_LIFECYCLE` with `operation: launch_before_network` and
`outcome: ReadyWithSavedConfig`. They skip the automatic network suite and leave probe
refresh manual, allowing restoration to be inspected before any request. Client
recreation without a transport in the automatic suite is useful storage evidence;
it does not establish a real loss-of-network test.

## Android device execution

First identify the device or emulator, Android version, and supported ABIs. Record the
Unity version, scripting backend, architecture, stripping level, and app identifier
from the build summary. Clearly label emulator results, including any emulated ARM64
execution; repeat on a physical Android device for physical-device evidence.

Install only the dedicated APK. For an existing test installation, preserve its data;
do not uninstall it or use `pm clear` to manufacture a fresh launch. The automatic
suite already creates isolated fresh storage. Select a specific serial when multiple
devices are present. For example, using Unity's bundled `adb`:

```powershell
$adb = 'C:\Program Files\Unity\Hub\Editor\6000.0.68f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
& $adb devices -l
$serial = 'DEVICE_SERIAL' # Replace with the selected serial from devices -l.
& $adb -s $serial shell getprop ro.product.cpu.abilist
& $adb -s $serial install -r verification/mobile-runs/2026-10-29/BijiSDKMobileTest-arm64-development.apk
& $adb -s $serial shell monkey -p com.biji.sdk.verification.d20261029 -c android.intent.category.LAUNCHER 1
& $adb -s $serial logcat -d -s Unity
```

Save logs inside the evidence directory. Do not clear unrelated device logs or data.
Use the UI buttons or equivalent dedicated-app interaction for these checks:

1. Inspect defaults `80 / 180` and empty revisions before a sample/probe refresh.
2. Wait for the automatic suite's result marker and inspect all 15 result entries.
3. In the imported sample, refresh from Pages. Confirm pending revision appears while
   active values remain `80 / 180`, then activate explicitly and confirm `100 / 120`.
4. In the lifecycle probe, press **Probe: force Pages refresh**, inspect pending state,
   then **Probe: activate pending** and confirm active `100 / 120`. These buttons do
   not control the sample above them.
5. Background the app and resume. Capture `BIJISDK_LIFECYCLE` background/resume state;
   confirm no crash and active values remain readable. Repeat refresh and confirm no
   duplicate activation or corrupt state.
6. Force-stop only the test app with
   `adb -s <serial> shell am force-stop com.biji.sdk.verification.d20261029`, relaunch,
   and inspect `launch_before_network` / `ReadyWithSavedConfig` with `100 / 120`.
7. Ask the user to disable all usable device connectivity, then repeat force-stop and
   relaunch. Verify saved active values before requesting refresh. Record how connectivity
   was disabled and any refresh failure separately; do not infer true offline behavior
   merely because no refresh was called. Do not silently change system/device networking.
8. Have the user restore connectivity, force refresh the probe, and verify successful
   unchanged state. Force refresh still respects any active failure cooldown.

Published update/rollback checks require approved new hosted revisions. Local
update/rollback checks in the automatic suite establish a different, narrower result.

## iOS work remaining

This Windows Unity installation lacks iOS Build Support. Local source/fixture review
and Editor results do not establish an iOS build or runtime pass.

On a Mac, install Unity 6000.0.68f1 with iOS Build Support and compatible Xcode. Copy
the isolated project and SDK, update the local package path for the Mac, and verify
UPM/sample compilation. Select iOS with a dedicated bundle identifier, ARM64, IL2CPP,
a development build, and a recorded stripping level. Export to Xcode, use existing
authorized signing/provisioning, and run on an iPhone. Record Mac/Xcode, device/iOS,
signing prerequisites, logs, and the same explicit-activation/lifecycle/relaunch/offline
checks. Do not change credentials or purchase services to unblock signing.

## Updating and rolling back fixtures

Prepare the exact proposed change to the appropriate platform's
`site/configs/.../config.json` and obtain approval before publishing. Keep its context fields
and compatibility correct. For every changed publication, assign a new opaque `revision`
and UTC `publishedAt`. Never change values while reusing a published revision.
Deploy the updated site, check the exact JSON URL, then use the sample's development
force-refresh button or wait for freshness expiry. Force refresh bypasses the client's
15-minute freshness interval; it does not bypass CDN propagation, HTTP caching,
failure cooldown, or server throttling. Conditional responses and ETags are controlled
by the host. A normal 200 with unchanged values is also valid.

To roll back, restore the older values under another new revision and publish again.
The client applies rollback only after explicit activation. Keep Git history to retain
previous fixture contents; the static file itself represents the latest test publication.

## Limits

These are public, non-secret test values. Do not place credentials, private game data,
or player data in the site. This static test path does not perform server-side app-build
selection, targeting, authentication, or publishing access control. Different build ranges
need separately selected test fixtures or a real backend.

GitHub Pages cannot provide the controlled 429, delayed, malformed, or oversized response
scenarios used by `verification/Run-HttpUnityChecks.ps1`; retain the local fault server
for those checks. Mobile offline behavior and lifecycle/storage failures still require
device testing. The Python backend remains available for local API-contract tests.
