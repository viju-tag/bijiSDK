# Unity public API: HTTP milestone

Namespace BijiSDK contains the core; BijiSDK.Unity contains Unity adapters.

## Construction

ConfigClient(options, definitions, storage, dispatcher, transport = null,
remoteOptions = null, clock = null)

Supply both transport and remoteOptions, or neither.
UnityConfigClientFactory.Create(options, definitions) creates a local-only client.
UnityConfigClientFactory.Create(options, definitions, remoteOptions) additionally
constructs UnityWebRequestConfigTransport.

Call Unity's factory from Awake/Start on its main thread. It uses recoverable storage
under Application.persistentDataPath/bijisdk and captures Unity's synchronization context.
Use one writer per cache context/directory; await work before replacing its client.

## Operations

| Operation | Behavior |
| --- | --- |
| InitializeAsync(cancellationToken) | Validate defaults and restore local active/pending/fetch state; no network |
| RefreshAsync(cancellationToken = default, force = false) | Fetch, validate, and persist pending data without activation |
| StageJsonAsync(json, cancellationToken) | Authoritatively stage local JSON and invalidate HTTP metadata |
| ActivatePendingAsync(cancellationToken) | Commit and activate the complete pending snapshot |
| Get<T>(key) | Read a registered active value without I/O |
| GetSnapshot() | Capture a stable view for consistent multi-key reads |
| GetStatus() | Read configuration and refresh diagnostics |
| Dispose() | Cancel owned work and suppress later event delivery |

Force bypasses freshness only. It does not bypass cooldown, Retry-After, or attempt limits.
Concurrent refreshes share the first caller's operation and cancellation ownership.
A later caller can cancel its own wait without cancelling that shared request.

Initialization is independent of networking. Fetching and activation are always separate.
The sample explicitly refreshes on startup and resume.

## Options

ConfigOptions(projectId, environment, platform, long appBuild, int configContractVersion,
int maxPayloadBytes = 1048576)

ConfigRemoteOptions(Uri baseUri, bool allowInsecureHttp = false,
TimeSpan? minimumRefreshInterval = null, TimeSpan? requestTimeout = null,
int maxAttempts = 3, TimeSpan? operationTimeout = null, TimeSpan? failureCooldown = null)

Defaults: 15 minutes freshness, 10 seconds per request, three attempts, 45 seconds total,
and 60 seconds failure cooldown. Request and operation timeouts are limited to one day.

BaseUri must be absolute HTTPS, with no credentials, query, or fragment. Development
HTTP requires explicit opt-in; Unity's adapter additionally restricts it to the Editor
or a development build. Platform transport policies can still reject HTTP.

## Typed definitions

ConfigDefinition<T>(key, defaultValue, serialize, deserialize, validate = null)

- serialize: Func<T, JToken>
- deserialize: Func<JToken, T>
- validate: Func<T, string>; null or empty means valid

Delegates must be pure and thread-safe. Core mutations run on a worker; reads deserialize
on the caller's thread. Mutable objects must be newly allocated. The core supplies cloned
JSON tokens. Game feature classes remain independent of wire DTOs and Unity serialization.

Wrong requested types throw InvalidOperationException. Unregistered keys throw
KeyNotFoundException. Reads after disposal throw ObjectDisposedException.
Async operations after disposal return Cancelled.

## Transport and clock interfaces

IConfigTransport.GetAsync(ConfigHttpRequest, CancellationToken) returns Task<ConfigHttpResponse>.

ConfigHttpRequest exposes Url, IfNoneMatch, Timeout, and MaxResponseBytes.
ConfigHttpResponse exposes StatusCode, Body, ETag, and RetryAfter.

ConfigTransportException(message, isTimeout = false, isRetryable = true) distinguishes
temporary failures from permanent payload/encoding errors.

IConfigClock exposes UtcNow, monotonic Elapsed, and DelayAsync for deterministic policy
tests. SystemConfigClock is the default.

## Results and diagnostics

ConfigResult exposes Outcome, Error, and IsSuccess.
Outcomes: ReadyWithDefaults, ReadyWithSavedConfig, UpdateAvailable, Unchanged, Activated,
NoPendingUpdate, FreshCache, Throttled, Superseded, Cancelled, Failed.
IsSuccess is false for Failed, Cancelled, Throttled, and Superseded.

ConfigStatus exposes IsInitialized, ActiveRevision, PendingRevision, LastError,
LastAttemptUtc, LastSuccessUtc, NextEligibleRefreshUtc, and IsRefreshing.

ConfigSnapshot exposes Revision, Get<T>, and GetSource(key).
Sources are Default or Remote. Debug overrides are deferred.

## Events

UpdateAvailable and ConfigActivated carry revision strings.
ErrorOccurred and RefreshFailed carry ConfigError.
Unity delivers these on its main thread and isolates throwing subscribers.

A stale HTTP result superseded by local staging does not replace that state.
A successful durable commit is not rolled back by cancellation arriving afterward.

## Persistence

ETag/freshness persist with their validated response and exact request URL.
Cooldown and Retry-After deadlines are session-local; they survive resume on the same
client but not recreation/restart. Active and pending data persist independently.
