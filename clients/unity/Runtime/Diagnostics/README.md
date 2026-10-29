# Diagnostics

ConfigStatus exposes active/pending revisions, initialization, last error, last attempt,
last success, next eligible refresh time, and IsRefreshing.
ConfigSnapshot exposes per-key Default/Remote sources.

ErrorOccurred reports categorized client errors. RefreshFailed reports HTTP-operation
failures through the configured dispatcher. Subscriber failures are isolated.

Freshness is persisted with a validated representation. Cooldown/Retry-After deadlines
are session-local. DebugOverride diagnostics remain deferred.
