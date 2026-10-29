# Verification

## Standalone core and HTTP checks

From the project root:

    dotnet run --project tools/OfflineChecks/BijiSDK.OfflineChecks.csproj

Requires .NET SDK 9. The harness defaults to the SDK's Newtonsoft.Json assembly.
To use Unity's installed package assembly, pass:

    -p:NewtonsoftJsonPath=<absolute-path-to-Newtonsoft.Json.dll>

The harness compiles runtime sources directly while excluding Unity adapters.
The current suite contains 31 behavior groups: the 15 offline groups and 16 HTTP
coordination groups. Offline coverage includes defaults, snapshots, staging/activation,
restart, validation, corruption recovery, context isolation, cancellation, and failures.
HTTP coverage uses injected transports/clocks to exercise refresh policy and ordering.

## Filesystem checks

    dotnet run --project verification/storage/harness/StorageTests.csproj

Twelve checks exercise the actual file adapter. See storage/README.md.
Generated data remains inside verification/storage.

## Development backend checks

    python backend/tests/test_development_server.py

Fifteen stdlib Python tests exercise local delivery, ETags/304, compatible revision
selection, publishing, rollback, validation, atomic pointer failures, and publication locks.
Tests stop their temporary loopback servers and clean their isolated test directories.

## Unity checks

In PowerShell:

    .\verification\Run-UnityChecks.ps1

The optional -UnityPath selects the executable; the default is Unity 6000.0.68f1
installed through Unity Hub on Windows. The script updates a separate UnityProject,
imports the local package, copies checks/sample sources, and runs Unity in batch mode.
It waits for the process and returns its exit code.

The offline integration check covers actual Unity main-thread callbacks and real-file
restart. The sample is compiled; this does not constitute an interactive UI/device test.

Output: unity-editor-checks.log. A successful run includes:
BIJISDK_UNITY_CHECKS_COMPLETE failures=0

## Live HTTP Unity workflow

    .\verification\Run-HttpUnityChecks.ps1

This wrapper creates an isolated publication store under verification/http-runs,
initializes sample configs, starts short-lived loopback delivery/fault servers, and
runs the Unity checks against those real HTTP endpoints. It restores temporary
environment variables and stops its servers on completion.

The live run exercises fetch/activation, publication update, ETag response, restart,
rollback, and Unity transport fault handling. The fault server supplies oversized bodies,
invalid UTF-8, delayed responses, redirects, and 429 Retry-After responses.
It has no publishing endpoint and binds only to loopback.

The final live Unity HTTP run passed with BIJISDK_UNITY_CHECKS_COMPLETE failures=0.
It covered all 31 core groups, main-thread/persistence integration, live publication,
ETag/activation/rollback/offline behavior, and six transport fault checks.
See [HTTP milestone results](results-http-2026-10-29.md).

## Current verified scope

- Standalone core/HTTP behavior: 31 groups passed.
- Windows filesystem adapter: 12 checks passed.
- Local development backend: 15 tests passed.
- Offline Unity Editor compilation, callbacks, and disk restart: previously passed.
- Final live HTTP Unity run: passed, including six transport fault checks.

Generated UnityProject, http-runs, and build outputs are verification artifacts, not
SDK dependencies. Unity may download package dependencies when not cached.

Android/iOS device execution, IL2CPP builds, production hosting/authentication, and
interactive sample playtesting are not covered by these Windows checks.

## GitHub Pages sample checks

The console and Unity runners also execute four Pages adapter groups: context mapping,
isolation, cancellation, and explicit activation/validation. The console total is now
35 groups. Static fixture validation uses the backend envelope and feature validators.
See [mobile setup](../docs/github-pages-testing.md) for device tests.
