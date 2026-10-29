# Transport

IConfigTransport, HTTP request/response types, remote options, and IConfigClock are in
Runtime/Contracts/ConfigHttpContracts.cs. The platform-neutral coordinator is implemented
in Runtime/Core/ConfigClient.Refresh.cs and ConfigClient.FetchCache.cs.

UnityWebRequestConfigTransport lives in Runtime/Unity because it uses Unity networking.
It enforces response bounds, cancellation, and strict UTF-8. HTTPS is the default;
development HTTP is explicitly gated. Retry/freshness policy belongs to the core.

New platform clients/adapters should preserve the common wire contract and behavior
fixtures. Server delivery and publishing tools remain independent of Unity.
