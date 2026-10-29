# Client behavior v1

Offline state, validation, persistence, and HTTP refresh are implemented in preview.3.
The Editor override section remains a future specification. See implementation-status.md
and verification results for the tested boundary.

## Definitions and initialization

The game registers unique keys, typed defaults, deserializers, and validators.
Duplicate keys or invalid defaults fail initialization clearly.

InitializeAsync loads defaults and compatible saved state without waiting for networking.
If saved active data is corrupt/incompatible, use defaults and report recovery.
Restore a valid pending record separately; do not automatically activate it.
Restore fetch metadata only when it matches the exact endpoint/context and the current
pending or active representation.

## Values and default resolution

Every registered key has a value after successful initialization.
Missing top-level feature keys use their bundled defaults. Do not merge against a
previous remote revision. Unknown remote keys are ignored.

A supplied feature must pass its complete validator. Invalid nested required fields,
types, or ranges reject the entire update. False, zero, and empty values are not
implicitly missing; their validity is defined by the feature schema.

Get<T> reads active state without I/O. Wrong types and unregistered keys are programmer
errors. Snapshot reads return deserialized copies, and captured snapshots stay stable
after activation. Game conversion delegates must be pure, thread-safe, and return new
instances for mutable objects.

## Refresh

The game requests refresh explicitly, normally at startup and resume. The sample wires
those lifecycle requests; the core has no hidden polling timer.

Default policy:

- 15-minute freshness interval since a valid persisted 200 or 304 response.
- 10-second timeout per request and 45-second total network/retry operation budget.
- At most three requests, including a 304 recovery request.
- Retry transient transport errors, timeouts, and HTTP 5xx only.
- Retry delays nominally 2 seconds then 5 seconds, with 20 percent jitter.
- HTTP 429 honors Retry-After. A delay that cannot fit the remaining budget returns
  Throttled with a next-eligible time instead of waiting indefinitely.
- Exhausted transient failures establish a 60-second cooldown.
- Force bypasses freshness only; it cannot bypass throttling, cooldown, request sharing,
  timeout, or attempt limits.

Each client shares one in-flight refresh. The first caller owns cancellation for that
operation. A later caller's cancellation stops only that caller's wait.
Disposal cancels the client-owned request. A non-cooperative transport cannot keep the
coordinator waiting past its bounded request timeout; late results are ignored.

HTTP and Unity transport callbacks are asynchronous. Configuration mutation is serialized
separately from network I/O so reads and local staging remain available.

## Conditional requests

Send If-None-Match only with a validated locally available representation for the exact
request endpoint/context. Pair ETag and response in storage.

A valid 304 updates freshness without staging a new revision or activating anything.
A 304 without a matching representation permits one unconditional recovery request
within the same attempt budget. A second unusable 304 fails.

Invalid 200 responses, permanent 4xx responses, unsupported contexts, and malformed
transport data are not retried automatically. Refresh failures preserve active/pending
values and do not emit ConfigActivated.

## Local authority and stale responses

StageJsonAsync is caller-authoritative and invalidates HTTP ETag/freshness metadata.
Restaging the active revision clears a different pending revision.

Refresh captures a mutation generation. If newer local staging occurs before acceptance,
the HTTP result is Superseded and cannot overwrite it. Activation of the same downloaded
representation does not invalidate its ETag. Revision strings are opaque; clients never
sort them to infer which publication is newer.

## Persistence and acceptance

Validate the envelope, context, compatibility, and all supplied registered features.
Resolve defaults to produce one immutable candidate.

HTTP metadata is written before pending state. If pending persistence fails, active and
pending state remain unchanged; unmatched orphan fetch metadata is ignored on restore.
A candidate becomes pending only after its durable save succeeds.
The same revision with changed content is a protocol violation.

Active/pending record format 1 stores context, response, and checksum.
Fetch record format 1 additionally stores exact source URL, ETag, and last-success UTC.
Future or invalid success timestamps force a refresh instead of claiming freshness.

Cache namespace includes project, environment, platform, app build, and game contract.
App-build isolation is conservative: a new build initially uses defaults until it gets a
matching saved or fetched configuration.

Temporary records are never treated as committed. Retain complete prior generations for
recovery, and do not delete the only valid copy before committing its replacement.
A recovered backup is used without rewriting the corrupt primary during initialization.

Freshness expiration does not delete usable active data.
Freshness uses elapsed time within a session and validated UTC age across restarts.
Cooldown/Retry-After deadlines remain session-local and are not restored after recreation.

## Activation

Only ActivatePendingAsync applies a downloaded configuration.
Commit active storage, swap the complete in-memory snapshot, and then emit ConfigActivated
on Unity's main thread. Clear pending storage after the active commit.

A failed active commit preserves active and pending state. Cleanup failure after a
successful active commit is diagnostic; startup deduplicates pending data already active.
A completed durable commit is the cancellation boundary and is not rolled back.

Game code chooses an activation point safe for ongoing gameplay and transactions.
Subscriber failures must not prevent other subscribers from receiving events.

## Diagnostics

ConfigStatus exposes initialization, active/pending revisions, last error, last attempt,
last successful fetch, next eligible refresh time, and whether a refresh is running.
Per-key sources currently are Default or Remote.

Errors include Initialization, Transport, Timeout, Throttled, Protocol, Compatibility,
Validation, Storage, Cancelled, and Subscriber.
RefreshFailed reports HTTP-operation failures on the injected main-thread dispatcher.

## Planned Editor overrides

Overrides remain deferred. When implemented, they must be opt-in, validated, context-
isolated, visibly marked, resettable, and unavailable to release builds. They must never
overwrite downloaded server response records.
