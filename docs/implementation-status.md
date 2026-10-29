# Implementation status

## Delivered: 0.1.0-preview.4, 2026-10-29

The preview.4 release records the Android validation and adds repeatable test tooling.
Runtime/sample source is unchanged from the tested preview.3 APK; historical reports
retain the actual tested version. iOS testing is deferred by the user.

The Unity client supports typed defaults, strict JSON/envelope and feature validation,
immutable snapshots, local staging, explicit async activation, per-key sources, and
main-thread event delivery.

HTTP refresh now uses UnityWebRequest through an injectable transport. The coordinator
enforces bounded requests, retries, freshness, rate-limit delays, request sharing, and
stale-response protection. A local Python development server and publishing/rollback
commands provide an end-to-end configuration source without choosing production hosting.

Initialization remains local and network-independent. The sample explicitly requests
refresh on startup/resume and exposes manual refresh/activation controls. The core does
not start a hidden polling loop.

## Persistence

Version-1 active/pending records contain hashed request context, the full response, and
a SHA-256 checksum. Backups support recovery; uncommitted temporary files are ignored.
Deletion markers prevent stale records from returning after interrupted cleanup.

A separate fetch record stores the validated response, ETag, exact request URL, and
last successful fetch time. Restore uses it only when its context/source and response
match the current pending or active representation. Old offline records still load.

Fetch metadata is saved before pending state. If staging then fails, unmatched orphan
metadata is ignored during recovery. A failed metadata write does not change pending
or active configuration. Activation commits storage before changing memory.

## Refresh and cancellation

One refresh runs per client. The first caller owns cancellation of that shared operation;
later callers can cancel their own wait without cancelling everyone else's request.
Disposal cancels client-owned work and suppresses later events.

Local staging is authoritative and invalidates network metadata. If it occurs while a
request is in flight, the stale HTTP result returns Superseded instead of replacing the
newer local state. Revision strings are opaque and never used for ordering.

ETag/freshness survive restart. Failure cooldowns and server Retry-After deadlines are
session-local: they survive pause/resume on the same client, but are not persisted across
client recreation or process restart.

## Verification

See ../verification/README.md and ../verification/results-http-2026-10-29.md for commands, outcomes,
and the exact tested scope. Core, transport, local server, and Unity integration checks
are separate from mobile player validation.

The current Unity 6000.0.68f1 Editor run passed 35 core/HTTP/Pages groups, main-thread
and persistence integration, live publication/ETag/activation/rollback/offline restart,
and seven controlled transport cases. A clean project also passed 15 live Pages and
storage checks in Editor and in an ARM64 IL2CPP development player on a Nox Android 12
emulator. Manual activation, background/resume, force-stop/relaunch, and approved hosted
update/rollback passed there. See [emulator results](../verification/results-mobile-2026-10-29.md).

The same APK also passed all 15 checks on a physical Motorola moto g45 5G running
Android 15 / API 35 with native ARM64 execution. The build uses IL2CPP, Low managed
stripping, minimum API 23, and target API 35. Manual sample/probe refresh kept 80/180
active until explicit activation changed it to 100/120. Repeated refresh,
background/resume, and force-stop/relaunch with saved values passed. The automatic
suite also verified local update/rollback and durable file replacement on the phone.
A separately approved hosted update/rollback sequence passed in the imported sample:
the 125/120 update stayed pending while 100/120 remained active; after activation,
the 100/120 rollback stayed pending while 125/120 remained active. Explicit rollback
activation restored 100/120, which survived another process restart. The lifecycle
probe retained its prior active rollback and was not used for these new hosted revisions.
See [physical Android results](../verification/results-physical-android-2026-10-29.md).
Real network-loss/reconnection testing was skipped by the user. iOS execution remains
unverified.

## Remaining work

- Choose production hosting, publishing access controls, and operational ownership.
- Expand physical Android coverage to other devices/OS versions, higher stripping,
  controlled transport failures, and background/resume during an active refresh.
- Complete real network-loss/reconnection checks when requested, and build/run on iOS.
- Add an Editor inspector and validated debug overrides.
- Confirm license/ownership before external distribution.

A/B tests, player targeting, and content delivery remain outside this milestone.

## Limits

JSON responses are limited to 1 MiB by default and depth 64. The core rejects non-finite
numbers and integers outside the exact interoperable JSON integer range.

One writer owns each cache directory. Cross-process and competing-client writes are
unsupported. File replacement is verified on Windows, in the tested Android emulator,
and on the Motorola moto g45 5G with Android 15. Unsupported safe replacement fails
while preserving existing committed data.

The local server is a development tool. It is not a production hosting/security model.
Physical Android results cover the recorded device/build settings. Other Android
configurations and iOS still require their own runtime checks.
