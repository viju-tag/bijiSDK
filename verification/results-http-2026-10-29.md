# HTTP milestone verification — 2026-10-29

Package: `com.biji.sdk` version `0.1.0-preview.3`.
Environment: Windows, Unity 6000.0.68f1 Editor, .NET console harness, Python development server.

## Results

- Standalone client: 31/31 groups passed (15 offline + 16 HTTP).
- Recoverable filesystem storage: 12/12 checks passed.
- Development delivery/publishing server: 15/15 tests passed.
- Unity Editor: the same 31 client groups passed, plus main-thread callbacks and real-file restart.
- Live Unity HTTP: fetch 200, explicit activation, conditional 304, publish changed values, rollback as a new revision, and offline restoration passed.
- Unity transport faults: response byte limit, invalid UTF-8, timeout, cancellation, blocked redirect, and 429 header preservation passed.
- Final Unity marker: `BIJISDK_UNITY_CHECKS_COMPLETE failures=0`; runner exit code 0.

## Commands

```powershell
dotnet run --project tools/OfflineChecks --verbosity quiet
dotnet run --project verification/storage/harness/StorageTests.csproj --verbosity quiet
python -m unittest discover -s backend/tests -v
./verification/Run-HttpUnityChecks.ps1
```

The HTTP runner uses isolated publication data and owns its temporary loopback servers. It stops those servers and restores environment variables when finished. See `unity-editor-checks.log` for the most recent Editor run; rerunning overwrites that log.

## Corrections verified

Retry-After accepts surrounding whitespace. Atomic timing updates preserve throttle deadlines under concurrent completion. Received redirect status codes remain non-retryable HTTP responses even when Unity reports its disabled redirect limit as a connection error; no redirect is followed.

## Limits

These results do not certify Android/iOS device behavior, IL2CPP, mobile filesystem replacement, mobile network policies, production TLS/hosting/authentication, or interactive sample playtesting. Failure cooldown and 429 deadlines are session-local; validated ETags and successful-fetch freshness persist across restart. The included Python backend is a local development implementation.
