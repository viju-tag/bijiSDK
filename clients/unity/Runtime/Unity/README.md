# Unity adapters

UnityMainThreadDispatcher captures Unity's synchronization context. Construct it on the
main thread from Awake/Start; core events then arrive on that thread.

UnityConfigClientFactory.Create(options, definitions) creates an offline client.
The overload with ConfigRemoteOptions adds UnityWebRequestConfigTransport while keeping
initialization local and network-independent.

The factory uses Application.persistentDataPath/bijisdk with hashed context directories.
Use one writer/client per context and await ongoing work before replacing the client.

## HTTP transport

- Creates, aborts, reads, and disposes UnityWebRequest objects on Unity's main thread.
- Supports cancellation and a per-attempt timeout, including fractional durations.
- Bounds the downloaded response bytes and rejects invalid UTF-8.
- Preserves HTTP status, ETag, and Retry-After for the core coordinator.
- Disables redirects; use the final configured endpoint.
- Keeps Unity's standard certificate validation.
- Allows HTTP only with explicit opt-in in the Editor or a development build.
- Does not change PlayerSettings or platform networking policy.

The core coordinator owns retries, request sharing, freshness, 304 handling, and staging.
The adapter's timeout and cancellation path stop the Unity request; neither activates data.
Mobile build/device networking and filesystem behavior still require verification.
