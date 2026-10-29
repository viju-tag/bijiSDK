# Physical Android testing — 2026-10-29

**Passed on a physical Motorola moto g45 5G running Android 15:** all 15 automatic
SDK checks, manual Pages staging/activation, repeated refresh, background/resume,
force-stop/relaunch, refresh after restoring saved state, and the separately approved
hosted update/rollback sequence. The existing ARM64
IL2CPP APK was used without rebuilding or changing SDK code.

Offline/reconnection remained **not run, skipped by the user**. Two new fixture-only
commits/pushes were explicitly approved for the physical hosted tests. iOS remains blocked.

## Environment

| Item | Observed value |
| --- | --- |
| Device | Physical Motorola **moto g45 5G**, authorized USB ADB connection |
| Android | **15 / API 35**, build `V1UGS35H.75-14-9-3-1-4/d59ef-4b53f9` |
| CPU | Native **arm64-v8a**, Qualcomm hardware; runtime processor `ARM64 FP ASIMD AES` |
| Supported ABIs | `arm64-v8a,armeabi-v7a,armeabi` |
| Display | Phone reports 720 × 1600; test app/screenshots run in 1600 × 720 landscape |
| Unity | **6000.0.68f1**, revision `e1e9baaf294b` |
| SDK package | `com.biji.sdk` **0.1.0-preview.3** |
| Player settings | **IL2CPP**, **ARM64**, **Low** managed stripping, development APK, script debugging off |
| Android build settings | Minimum API **23**, target/compile API **35** |
| Test app | `com.biji.sdk.verification.d20261029`, version 1.0.0 / code 1 |
| APK | `mobile-runs/2026-10-29/BijiSDKMobileTest-arm64-development.apk` |
| APK SHA-256 | `C08038BC99E2F7CB3B8F9BBEEEEEE97E9466981529D05DDAB0FFCB26420C4BB1` |
| Network | Live HTTPS GitHub Pages; no cleartext exception or phone network settings changed |
| Initial hosted revision | `pages-android-rollback-91fef65280734582ac0b5fc91f724b92`, energy **100 / 120** |
| Final hosted/sample revision | `pages-android-physical-rollback-0407b42450c148dcae9ae261e85b979a`, energy **100 / 120** |
| Initial state | Dedicated package absent before installation; fresh isolated app data |

Device discovery initially failed in the preceding attempt. The phone later appeared
as an authorized USB target and Windows ADB Interface. Discovery logged an ADB
client/server version 36/41 mismatch and automatic server restart; subsequent phone
commands consistently used the matching client on port 5037 with an explicit device
serial. The serial and full device fingerprint are retained only in the ignored evidence
directory. No driver installation, phone data clearing, or app uninstallation occurred.

## Automatic checks

The player log reported:

```text
BIJISDK_MOBILE_CHECKS_COMPLETE platform=android passed=15 failures=0
```

The original result JSON was pulled from the phone and independently checked for
exactly 15 passed entries and zero failures. It identifies the Motorola device,
Android 15, Unity version, and native ARM64 processor. These are physical-device results.

| Check | Status | Verified behavior |
| --- | --- | --- |
| Defaults before network | **passed** | ReadyWithDefaults, 80/180, no transport calls during initialization |
| Pages fetch without activation | **passed** | HTTP 200 creates pending config; active stays 80/180 |
| Repeated pending refresh | **passed** | FreshCache avoids a request; forced request returns Unchanged/304; pending preserved |
| Explicit activation/main-thread callback | **passed** | Active becomes 100/120, Remote source, pending empty; callback on Unity main thread |
| Repeated active refresh | **passed** | Unchanged/304; active stable; no duplicate activation event |
| Client recreation | **passed** | Real storage restores saved 100/120 before a new request |
| Persisted ETag | **passed** | Recreated client sends validator and receives HTTP 304 |
| Wrong context | **passed** | Candidate rejected; active data/revision preserved |
| Invalid energy | **passed** | Candidate rejected; active data/revision preserved |
| Malformed JSON | **passed** | Candidate rejected; active data/revision preserved |
| Local update pending | **passed** | New local revision stays pending; active remains 100/120 |
| Local update replacement | **passed** | Explicit activation durably commits 125/120 using real file storage |
| Local rollback pending | **passed** | Another local revision stays pending; active remains 125/120 |
| Local rollback activation | **passed** | Explicit activation durably restores 100/120 |
| Restore replaced file without transport | **passed** | New client restores 100/120 after durable replacement |

Local update/rollback uses unique local revisions and does not publish hosted changes.
Transport-free client recreation is storage evidence; it is not a loss-of-network test.

## Manual and lifecycle checks

The imported sample and persistent lifecycle probe use separate caches from each
other and from the automatic suite. Both manual clients were checked independently.
Controls were operated through ADB-injected touchscreen presses on the physical phone;
this does not constitute a human-finger usability assessment.

| Check | Status | Observation |
| --- | --- | --- |
| Fresh launch | **passed** | Sample and probe show 80/180 with empty active/pending revisions |
| Live Pages refresh | **passed** | Both stage the initial hosted rollback revision |
| Before activation | **passed** | Active stays 80/180, source Default; hosted revision remains pending |
| Repeated pending refresh | **passed** | Unchanged; defaults and pending state preserved |
| Explicit activation | **passed** | Both become active 100/120, source Remote, pending empty |
| Repeated active refresh | **passed** | Unchanged; during this initial manual phase, sample emitted exactly one pending and one activation event |
| Background/resume | **passed** | Same PID **8245**; both lifecycle callbacks read active 100/120 without a crash |
| Force-stop/relaunch | **passed** | New PID **9124**; ReadyWithSavedConfig, correct active revision, 100/120, pending empty |
| Refresh after relaunch | **passed** | Unchanged with no error; active revision and 100/120 preserved |
| Relaunch after real network loss | **not run** | Prior explicit user skip retained |
| Network restoration sequence | **not run** | No connectivity-loss sequence was performed |
| New hosted update/rollback on this phone | **passed** | Fresh approval, two new revisions, pending preservation, explicit activation, and latest rollback restoration verified in imported sample |
| Controlled mobile timeout/429/oversized/UTF-8 responses | **not run** | Those controlled transport cases remain Editor evidence |

Only the dedicated app was installed and force-stopped. Existing app data was preserved.
The phone and app were left with active **100/120** and no pending configuration.
The sample's startup refresh is disabled; saved startup was observed before manual
refresh. No system-wide or phone connectivity settings were changed.

## Approved physical hosted update and rollback

After the initial physical checks passed, the user explicitly approved two additional
fixture-only commits and pushes. Both candidates passed strict envelope/feature
validation before publication; each changed publication used a new opaque revision.
Only `site/configs/bijisdk-offline-sample/development/android/contract-1/config.json`
was included in each commit. No branch, Git configuration, signing, or repository
visibility changes were made. Test code and documentation remain uncommitted.

| Phase | Publication | Physical imported sample result |
| --- | --- | --- |
| Update | `pages-android-physical-update-0877d892a9454728841fec2cfd9d811c`; [commit 5a787eb](https://github.com/viju-tag/bijiSDK/commit/5a787eb5ad21004bfe75c230ba12b6b41809bedf); [Pages run 40873303123](https://github.com/viju-tag/bijiSDK/actions/runs/40873303123), success | Live 125/120 becomes pending while active remains 100/120. Explicit activation changes active to 125/120 and clears pending. |
| Rollback | `pages-android-physical-rollback-0407b42450c148dcae9ae261e85b979a`; [commit 26fa7e2](https://github.com/viju-tag/bijiSDK/commit/26fa7e25684716e7a2fa5905d6c5fb52f152438c); [Pages run 40873547627](https://github.com/viju-tag/bijiSDK/actions/runs/40873547627), success | Live 100/120 becomes pending while active remains 125/120. Explicit activation restores 100/120 and clears pending. |
| Process restart after rollback | PID **9124 → 9647** | Sample shows ReadyWithSavedConfig, latest physical rollback revision, Remote source, 100/120, pending empty. Committed record matches UI. |

This hosted sequence exercised the **imported sample**. The separate persistent probe
retained its earlier rollback revision at 100/120 with no pending data; its initial
background/resume and restart checks are reported above. It was not used to fetch the
two new hosted revisions. Both clients finish with 100/120, and the hosted endpoint
matches the final repository fixture and supports HTTP 200 / conditional 304.
Another manual refresh after the latest rollback restart returned Unchanged, preserved
100/120, and emitted no new pending or activation event.

Screenshots and copied committed active/pending records establish the before/after
values, not merely a successful button press. One initial directory-wide cache copy
raced normal cleanup of a pending tombstone and could not copy the disappearing file.
The stable committed records were subsequently copied directly and validated. This
was an evidence-capture race, not an SDK storage failure.

## Evidence and reproduction

Evidence directory:
`verification/mobile-runs/2026-10-29/physical-moto-g45-174802/`.

- `device-environment.txt`, `device-selection.json`, `apk-sha256.json`, `launch.log`.
- `first-launch-full.log`, `auto-suite/results.json`, `auto-suite/hosted-android.json`,
  and the automatic suite's original storage records.
- `pending-state.json`, `pending.log`, `repeated-pending-state.json`,
  `repeated-pending.log`, `activated-state.json`, `activated.log`.
- `repeated-active-state.json`, `repeated-active.log`, `lifecycle-state.json`,
  `background-resume.log`, `relaunch-state.json`, `relaunch.log`.
- `post-relaunch-refresh-state.json`, `post-relaunch-refresh.log`,
  `hosted-android-final.json`, `sample-cache-final/`, `probe-cache-final/`.
- `first-launch.png`, `pending.png`, `activated.png`, `relaunch.png`, `final.png`.
- `fixture-proposals/`: exact validated physical update/rollback JSON and proposal.
- `hosted-update-commit.txt`, `hosted-rollback-commit.txt`,
  `publication-commit-audit.txt`, `hosted-update-deployment.json`,
  `hosted-rollback-deployment.json`, `hosted-final-verification.json`.
- `hosted-update-pending-state.json`, `hosted-rollback-pending-state.json`,
  `hosted-*-pending-active.json`, `hosted-*-pending-pending.json`,
  `hosted-update-active.json`, `hosted-rollback-active.json`.
- `hosted-update-pending.log/.png`, `hosted-update-active.log/.png`,
  `hosted-rollback-pending.log/.png`, `hosted-rollback-active.log`.
- `hosted-rollback-relaunch-state.json`, `hosted-rollback-relaunch.log`,
  `hosted-rollback-restored-active.json`, `hosted-final-restored.png`.
- `hosted-final-refresh.log`, `hosted-final-unchanged.png`.
- `hosted-android-update.json`, `hosted-android-rollback.json`,
  `hosted-android-final.json`, `evidence-validation.json`.

Result JSON originated at:
`/storage/emulated/0/Android/data/com.biji.sdk.verification.d20261029/files/mobile-verification/runs/8dac9dd30e594d8ba82c65e7dfc573e4/`.
Logs were filtered by the dedicated app's UID or PID; global logs were not cleared.

To reproduce, use the existing APK on an authorized ARM64 device, record its environment,
and follow the [mobile guide](../docs/github-pages-testing.md). Inspect actual screenshots
before targeting buttons because long revision labels wrap and move the probe controls.
The lifecycle probe skips its automatic suite once saved active data exists: require a
new result directory/marker before claiming a fresh 15-check run, and do not erase data
to manufacture a first launch. No SDK test failures were observed in this physical run.

## Changes and remaining risks

No SDK source changes or APK rebuild were needed. Updated the physical report, aggregate
mobile report, README, implementation status, and verification guide. Generated evidence
remains ignored. The two additional approved fixture commits/pushes are recorded above;
all other work remains uncommitted. The original game project was untouched.

The .NET/backend/Editor suites were not repeated: the tested code and APK were unchanged,
and their earlier results remain in the aggregate report. Physical evidence covers this
one phone, Android 15, native ARM64, and Low stripping. Other versions/devices/architectures,
higher stripping, real offline/reconnection, controlled mobile faults, long-running or
in-flight lifecycle stress, and all iOS build/signing/device behavior remain unverified.
