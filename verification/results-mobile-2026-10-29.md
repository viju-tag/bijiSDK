# Mobile testing results — 2026-10-29

Executed the supplied `BIJISDK_TESTING_HANDOFF.md`. The Android ARM64 IL2CPP APK built,
installed, and passed 15 automatic checks in the existing Nox emulator. Manual sample
activation, background/resume, process relaunch, and approved hosted update/rollback
also passed. A later physical Motorola moto g45 5G run on Android 15 passed the same
15 checks, manual activation/repeated refresh, background/resume, force-stop/relaunch,
and a separately approved hosted update/rollback sequence with new revisions.
See [physical results](results-physical-android-2026-10-29.md). iOS remains blocked.
The user explicitly skipped the offline/reconnection check.

## Environment and isolation

| Item | Observed value |
| --- | --- |
| Host | Windows 10.0.19045; PowerShell 7.6.5; .NET SDK 9.0.302; Python 3.12.10 |
| Unity | 6000.0.68f1, revision e1e9baaf294b |
| SDK | `com.biji.sdk` 0.1.0-preview.3 through local UPM package |
| Repository | Branch `codex/initial-upload`; initial tree contained only untracked handoff |
| Generated project | `verification/MobileUnityProject` |
| Saved scene | `Assets/Scenes/BijiSDKMobileTest.unity` inside that project |
| Application | `com.biji.sdk.verification.d20261029`, version 1.0.0 / code 1 |
| Player | Development APK; IL2CPP; ARM64; Low managed stripping; script debugging off |
| Android API | Minimum 23; target/compile 35; installed SDK platforms 34, 35, 36; build-tools 36.0.0 |
| Native toolchain | NDK 27.2.12479018 / r27c; bundled Temurin OpenJDK 17.0.9 |
| Networking | INTERNET permission; HTTPS Pages; insecure HTTP not allowed |
| Signing | Unity development/debug signing; no custom keystore or signing credentials changed |
| Test target | **Nox 7.0.6.2 emulator**, existing instance `nox`, ADB `127.0.0.1:62001` |
| Android guest | Android 12 / API 32; reports Samsung SM-N976N; this is **not a physical Samsung phone** |
| CPU | Guest primary ABI x86_64; advertises ARM64; ARM64 APK actually executed in emulator compatibility environment |
| iOS | iOS Build Support absent; Windows has no Mac/Xcode/signing/device build environment |

The emulator reported `x86_64,arm64-v8a,x86,armeabi-v7a,armeabi`. It was inspected before
installation. The dedicated identifier was absent before installation. The existing
emulator closed during the build and was reopened without recreating or erasing it.
An installation attempt before Android finished booting reported missing package service;
installation succeeded after `sys.boot_completed=1` and the package service became ready.
The initial bundled ADB invocation automatically restarted an incompatible older shared
ADB server. Device checks later used a task-owned ADB server on port 5041. Unity's
Editor cleanup also removed earlier connections, which were re-established explicitly.

All test sources, projects, builds, and reports are inside bijiSDK. The original
`merge-fever-part2` project was not modified. The unrelated open Unity project was
not stopped. Hosted fixture commits/pushes were separately approved by the user;
their publication results are recorded below. No branches or Git configuration were changed.
No existing applications were uninstalled and no app data was cleared.

## Results by stage

### Physical-device follow-up

An initial physical-device attempt on 2026-10-29 was **blocked at device discovery**.
The existing Nox ADB server on port 5037 listed only `127.0.0.1:62001`; the isolated
bundled ADB server on port 5041 listed no targets. Windows also showed no physical
phone/ADB/MTP interface. Repeated checks, including a 40-second watch after the user
requested another check, found no physical or unauthorized phone entry.

The existing APK hash was reconfirmed, and the live hosted Android rollback fixture
still returned 100/120. No APK was installed on a physical phone, and none of the
physical runtime/lifecycle tests ran. Phone model and USB/wireless-debugging connection
details were requested to diagnose the connection. The earlier offline/reconnection
skip remains in effect. Evidence: `mobile-runs/2026-10-29/physical-android-174224/`
(`device-discovery.txt`, `apk-sha256.json`, `hosted-android-before.json`).

That discovery blocker was subsequently resolved: an authorized USB **Motorola moto
g45 5G** appeared, running **Android 15 / API 35** with native **ARM64**. The unchanged
APK passed all 15 automatic checks, manual staging/activation and repeated refresh,
same-process background/resume, new-process saved-state restoration, and refresh after
relaunch. The user then separately approved two new fixture-only commits/pushes for
physical hosted update/rollback; both deployments and sample pending/activation checks
passed, and the latest rollback survived another process restart. Full evidence:
`mobile-runs/2026-10-29/physical-moto-g45-174802/`; see the
[physical-device report](results-physical-android-2026-10-29.md) for scope and logs.

### Stage outcomes

| Check | Status | Evidence |
| --- | --- | --- |
| Repository/setup inspection | **passed** | Initial status/branch inspected; installed Android tools and emulator ABIs recorded |
| Standalone client baseline | **passed** | 35/35 groups: 15 offline, 16 HTTP, 4 Pages; exit 0 |
| Actual filesystem baseline | **passed** | 12/12 checks; exit 0 |
| Python backend baseline | **passed** | 15/15 tests; exit 0 |
| Existing live Unity HTTP suite | **passed** | 35 client groups, main-thread/disk integration, local publication/activation/rollback, seven fault cases; exit 0 |
| Clean local UPM installation | **passed** | Actual Package Manager sample import; sample/runtime compilation succeeded |
| Automatic Newtonsoft resolution | **passed** | Manifest omits Newtonsoft; lock records registry version 3.2.1 at depth 1 |
| Saved sample scene | **passed** | Pages enabled; manual startup refresh/activation; no cleartext exception |
| Live Pages fixture availability | **passed** | Editor/Android/iOS each 200; exact local JSON match; conditional 304 |
| Live Pages/storage suite in Editor | **passed** | 15/15; `BIJISDK_MOBILE_EDITOR_COMPLETE failures=0` |
| Android IL2CPP build | **passed** | Build succeeded in 266.142 seconds; zero build errors/warnings |
| APK integrity/static inspection | **passed** | Signature verifies; arm64-v8a only; `libil2cpp.so`/`libunity.so`; identifier/API/INTERNET/debuggable confirmed |
| Android emulator install/launch | **passed** | Fresh dedicated install; logs confirm IL2CPP/ARM64/Unity version |
| Android automatic suite | **passed** | 15/15; `BIJISDK_MOBILE_CHECKS_COMPLETE platform=android passed=15 failures=0` |
| Physical Android testing | **passed** | Motorola moto g45 5G, Android 15/API 35, native ARM64: 15/15 automatic checks plus manual activation, repeated refresh, lifecycle, process relaunch; offline remains skipped |
| Hosted update/rollback publication | **passed** | Two approved fixture-only publications tested in emulator, then two separately approved fresh publications tested on physical Motorola; pending/activation/restart verified in both scopes |
| iOS source/fixture readiness inspection | **passed** | iOS context, HTTPS adapter, platform-independent assemblies reviewed; hosted iOS fixture matched local |
| iOS export/Xcode build/signing/device/IL2CPP | **blocked** | Requires Mac, matching Unity iOS support, Xcode, authorized signing, and iPhone |

Baseline commands:

```powershell
dotnet run --project tools/OfflineChecks --verbosity quiet
dotnet run --project verification/storage/harness/StorageTests.csproj --verbosity quiet
python -m unittest discover -s backend/tests -v
./verification/Run-HttpUnityChecks.ps1
./verification/Run-MobileUnityChecks.ps1 -Mode Prepare
./verification/Run-MobileUnityChecks.ps1 -Mode Editor
./verification/Run-MobileUnityChecks.ps1 -Mode Android
```

The HTTP suite was run once as a baseline and again after adding the missing malformed
HTTP body case. The additional case receives HTTP 200 through UnityWebRequest, rejects
truncated JSON as Failed/Protocol, and preserves defaults/empty revisions. The other
fault cases cover oversized bytes, invalid UTF-8, timeout, cancellation, redirects, and
429 Retry-After headers. They ran in Editor against task-owned loopback servers, not
on the emulator; mobile controlled-fault execution is **not run**.

## Automatic live Pages checks

Each row below **passed in Editor, the Android IL2CPP emulator player, and the physical
Motorola Android 15 ARM64 player**.
All initial energy expectations were checked against the current hosted JSON.

| Check | Verified result |
| --- | --- |
| Defaults before network | ReadyWithDefaults, 80/180, zero transport calls during initialization |
| Fetch without activation | HTTP 200; pending hosted revision; active remains defaults 80/180 |
| Repeated pending refresh | FreshCache avoids a request; force refresh returns Unchanged/304; pending preserved |
| Explicit activation/main-thread callback | Active becomes 100/120, source Remote, pending cleared; callback on Unity main thread |
| Repeated active refresh | Unchanged/304; no duplicate activation event; active unchanged |
| Client recreation | ReadyWithSavedConfig and 100/120 before any new request |
| Persisted ETag | Recreated client sends validator; HTTP 304; state preserved |
| Wrong context | Rejected; valid active revision/data preserved |
| Invalid energy | Rejected; valid active revision/data preserved |
| Malformed JSON | Rejected; valid active revision/data preserved |
| Local update pending | New unique local revision staged; active stays 100/120 |
| Local replacement | Explicit activation commits 125/120 via real file storage |
| Local rollback pending | Another unique local revision staged; active stays 125/120 |
| Local rollback activation | Explicit activation restores 100/120 |
| Replacement/recreation without transport | New client restores 100/120 after repeated durable replacement |

The last check proves transport-free client restoration, not a real network-loss test.
Local update/rollback does not change the hosted fixtures.

## Android emulator manual and lifecycle matrix

| Handoff test | Status | Observation |
| --- | --- | --- |
| Fresh isolated launch | **passed** | Imported sample and persistent probe show defaults 80/180; revisions empty |
| Pages refresh | **passed** | Both create pending `pages-android-001` |
| Before activation | **passed** | Screenshot/logs retain defaults 80/180 and pending revision |
| Activate pending | **passed** | Both show active `pages-android-001`, source Remote, 100/120 |
| Background/resume | **passed** | Same PID 3500; background/resume probe logs preserve active 100/120; no crash |
| Force-stop/relaunch | **passed** | PID 3500 → 4383; ReadyWithSavedConfig and 100/120 before any network request |
| Relaunch with network unavailable | **not run** | User explicitly chose to skip the offline check; no simulated failure counted as real offline |
| Restore network and refresh | **not run** | User skipped the network-loss/reconnection sequence |
| Repeated refresh | **passed** | Automatic pending/active checks plus manual post-relaunch force refresh returned Unchanged and preserved 100/120 |
| Physical touch interaction | **not run** | Synthetic short taps were unreliable in Nox; 600 ms touchscreen presses operated sample/probe controls |

## Approved hosted update and rollback

The user explicitly approved both fixture commits and pushes after reviewing the
proposal. Each commit contains only the Android fixture. Testing sources, guides,
and report remain uncommitted; no branch switch, fetch, merge, or Git configuration
change was performed.

| Phase | Publication and deployment | Emulator result |
| --- | --- | --- |
| Update | Revision `pages-android-update-629cb3eeff39450389ce0c7556ba2836`; [commit 15cc325](https://github.com/viju-tag/bijiSDK/commit/15cc32562da3ed3b9afb171ecfe47e5a294bf689); [Pages run 40869042414](https://github.com/viju-tag/bijiSDK/actions/runs/40869042414), success | HTTP 200 confirmed 125/120. Both clients kept active 100/120 until activation; explicit activation changed them to 125/120. |
| Rollback | Revision `pages-android-rollback-91fef65280734582ac0b5fc91f724b92`; [commit 7f9a2bc](https://github.com/viju-tag/bijiSDK/commit/7f9a2bc93d435dfad94efbb61abc92d8e0f18fc6); [Pages run 40869244325](https://github.com/viju-tag/bijiSDK/actions/runs/40869244325), success | HTTP 200 confirmed 100/120. Both clients kept active 125/120 until activation; explicit activation restored 100/120. |
| Final process restart | New PID 4851 | ReadyWithSavedConfig; rollback revision active, pending empty, Remote source, 100/120 before network. |

At the end of the emulator run, hosting and the emulator app retained that run's rollback
revision at 100/120. The later physical run published its own approved update/rollback;
the final hosted revision is now `pages-android-physical-rollback-0407b42450c148dcae9ae261e85b979a`
at 100/120. See the physical report for those two commits/deployments. Editor/iOS fixtures
were not changed. Long revision strings wrap the probe's
diagnostic label and move its buttons: initial coordinate-based activation/refresh
attempts hit another control or no control. Targets were re-read from screenshots,
then the expected state was asserted from the captured JSON logs. Those interaction
attempts do not indicate an SDK activation/storage failure.

## Artifacts and evidence

Paths below are relative to `verification/mobile-runs/2026-10-29/`:

- `BijiSDKMobileTest-arm64-development.apk`: **24,993,426 bytes**. SHA-256:
  `C08038BC99E2F7CB3B8F9BBEEEEEE97E9466981529D05DDAB0FFCB26420C4BB1`.
- `android-build-summary.json`, `apk-badging.txt`, `apk-native-libraries.txt`,
  `apk-signature.txt`, `apk-sha256.txt`.
- `baseline-offline-170140.log`, `baseline-storage-170159.log`,
  `baseline-backend-170159.log`, `baseline-environment-170140.log`.
- `baseline-unity-http.log`, `unity-http-with-malformed.log`.
- `unity-package-import.log`, `unity-project-setup.log`, `package-import-result.txt`,
  `unity-pages-editor.log`, `unity-android-build.log`.
- `editor-check-25cb80f5b3b54815bcd64d345f1e51e4/results.json`.
- `android-auto-suite/results.json`: pulled original Android result JSON and hosted body.
- `android-device-environment.txt`, `android-install.log`, `android-first-launch-full.log`,
  `android-pending.log`, `android-activation.log`, `android-background-resume.log`,
  `android-online-relaunch.log`, `android-repeated-refresh.log`.
- `android-first-launch.png`, `android-pending.png`, `android-activated.png`,
  `android-online-relaunch.png`, `android-repeated-refresh.png`.
- `hosted-fixtures.json`, `hosted-editor.json`, `hosted-android.json`, `hosted-ios.json`.
- `hosted-android-update.json`, `hosted-android-rollback.json`; `hosted-*-commit.txt`,
  `hosted-*-deployment.json`, `hosted-*-pending-state.json`, `hosted-*-active-state.json`.
- `android-hosted-update-pending.log/.png`, `android-hosted-update-active.log/.png`,
  `android-hosted-rollback-pending.log/.png`, `android-hosted-rollback-active.log/.png`.
- `android-hosted-rollback-relaunch.log`, `hosted-rollback-relaunch-state.json`,
  `android-final-restored.png`, `android-sample-cache-final/`, `android-probe-cache-final/`.
- `fixture-proposals/android-update.json`, `fixture-proposals/android-rollback.json`,
  `fixture-proposals/publication-proposal.json`.

Android automatic results originated at
`/storage/emulated/0/Android/data/com.biji.sdk.verification.d20261029/files/mobile-verification/runs/d92b4e9c03304333b231208cd4b03ec3/`.
The persistent lifecycle cache and imported sample cache are separate. See the mobile
guide for the three storage scopes. Generated projects/logs/builds are ignored; rerunning
the runner overwrites its named logs/APK, so choose another `-OutputRoot` to retain a run.

APK signature verification succeeded with v1/v2 signatures. `apksigner --verbose` also
prints warnings that META-INF metadata entries are outside the JAR v1 signature; its
verification succeeded and v2 APK signing was present. This development APK is not a
production release/signing assessment.

## Fixes and remaining work

No SDK runtime fixes were required. Hosted Android fixture changes were made only for
the separately approved update/rollback tests. Added the repeatable mobile
test project generator, diagnostic suite/probe, and malformed-response fault case;
updated the testing/status documents and ignore rules. All new runtime C# compiled in Editor
and IL2CPP. The first project-import attempt failed because build-summary fields used
unsigned integers while Unity returns signed counts. The helper was corrected to `int`,
an obsolete symbols-setting call was removed, and clean import/Editor/Android runs passed.
The first failed import log is retained as `unity-package-import-first-attempt.log`.

Both prepared fixture candidates passed the backend strict envelope/feature validator
before publication. The proposal timestamps were normalized from `+00:00` to the
required UTC `Z` form before their first publication. No published revision was reused
for changed content.

Files touched:

- `.gitignore`: generated mobile project/evidence exclusions.
- `README.md`, `docs/implementation-status.md`: actual emulator-tested scope.
- `docs/github-pages-testing.md`, `verification/README.md`: repeatable commands and limits.
- `verification/Run-MobileUnityChecks.ps1`: isolated project/import/Editor/APK runner.
- `verification/mobile/MobileProjectBuilder.cs`: real sample import, saved scene, build settings.
- `verification/mobile/MobileConfigChecks.cs`, `verification/mobile/MobileConfigProbe.cs`:
  live transport/storage suite and separate persistent lifecycle diagnostics.
- `verification/http_fault_server.py`, `verification/UnityHttpIntegration.cs`,
  `verification/UnityOfflineValidation.cs`: seventh fault case and completion text.
- `verification/results-mobile-2026-10-29.md`: this report.
- `verification/results-physical-android-2026-10-29.md`: later physical-device evidence.
- `site/configs/bijisdk-offline-sample/development/android/contract-1/config.json`:
  approved update then rollback, each committed/pushed separately.

Final checks: PowerShell parser validation, Python compilation, strict validation of
all final platform fixtures and both publication candidates, expected APK contents/
signature/hash, exact result counts/log-state assertions, ignored-output checks, and
`git diff --check`. No remaining failing SDK checks were observed.

Remaining risks: broader physical Android coverage beyond the tested Motorola/Android 15;
true loss of network/reconnection (skipped by the user);
other Android versions/architectures and higher stripping; mobile controlled-fault cases;
long-running/concurrent lifecycle stress; all iOS build/device
behavior. UPM remains the only distribution format; no `.unitypackage` was created.

For iOS, use a Mac with Unity 6000.0.68f1 plus iOS Build Support and compatible Xcode,
update the local UPM path, build ARM64 IL2CPP, use existing authorized signing, and run
the full matrix on an iPhone. Record model/iOS/Xcode/stripping and actual logs. No signing
credentials, repository visibility, or system-wide network settings were changed here.
