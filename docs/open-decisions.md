# Open decisions

## Confirmed

- Product name: bijiSDK.
- Unity baseline: 6000.0.68f1; target platforms Android and iOS.
- Remote values only; one shared backend contract for future platform clients.
- JSON dependency: com.unity.nuget.newtonsoft-json 3.2.1.
- Typed game definitions use explicit conversion and validation delegates.
- Response limit: configurable, default 1 MiB; JSON depth limit 64.
- Recoverable active/pending storage and validated persistent ETag/freshness metadata.
- Unity HTTP adapter with bounded retries, request sharing, and explicit activation.
- Development source: local Python server plus publication/rollback commands.
- Local UPM distribution during development.
- A/B tests, targeting, and content delivery deferred.

## Remaining decisions/work

| Item | Next action |
| --- | --- |
| Production backend provider | Select hosting that preserves the current API contract |
| Game feature schemas | Game team supplies defaults, schemas, and validators |
| Device compatibility | Build/run Android and iOS; verify IL2CPP and code stripping |
| Mobile transport policy | Verify HTTPS; opt into narrow development HTTP policies only when needed |
| Mobile storage | Test replacement, recovery, and termination on devices |
| Production publication access | Define authenticated publishing and operational ownership |
| External SDK distribution | Select registry/distribution and support policy |
| License and ownership | Confirm before external distribution |

HTTP defaults are implemented: 15-minute freshness, 10-second request timeout, up to
three attempts, 45-second operation budget, and 60-second exhausted-failure cooldown.
Consumers may tune supported options. Request and operation timeouts cannot exceed
one day because timer APIs impose finite ranges.

Cooldowns and Retry-After deadlines are currently session-local. Persisting those across
process restarts is a possible future enhancement; persistent configuration and ETag
recovery are already implemented.

Windows Editor success is not evidence of mobile/device compatibility.
