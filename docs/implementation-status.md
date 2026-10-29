# Implementation status

## Delivered: 0.1.0-preview.3, 2026-10-29

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

The final Unity 6000.0.68f1 Editor run passed the 31 core/HTTP groups, main-thread
and persistence integration, live publication/ETag/activation/rollback/offline restart,
and six transport fault checks. Android/iOS and IL2CPP remain unverified.

## Remaining work

- Choose production hosting, publishing access controls, and operational ownership.
- Build/run Android and iOS; verify IL2CPP, stripping, transport policy, and device storage.
- Add an Editor inspector and validated debug overrides.
- Confirm license/ownership before external distribution.

A/B tests, player targeting, and content delivery remain outside this milestone.

## Limits

JSON responses are limited to 1 MiB by default and depth 64. The core rejects non-finite
numbers and integers outside the exact interoperable JSON integer range.

One writer owns each cache directory. Cross-process and competing-client writes are
unsupported. File replacement is verified on Windows; unsupported safe replacement
fails while preserving existing committed data.

The local server is a development tool. It is not a production hosting/security model.
Editor/.NET results do not establish Android/iOS or IL2CPP compatibility.
