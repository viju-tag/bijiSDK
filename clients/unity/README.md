# bijiSDK Unity package

Version 0.1.0-preview.3 includes the offline core and HTTP refresh integration.

## Available

- InitializeAsync, StageJsonAsync, RefreshAsync, ActivatePendingAsync, typed reads,
  immutable snapshots, status, and events.
- Typed defaults with explicit JSON conversion and feature validation delegates.
- Strict response validation and project/environment/platform/build isolation.
- Recoverable active/pending records, corruption fallback, and offline startup.
- Conditional HTTP requests, matching ETags, freshness/cooldown checks, bounded retries,
  request/operation timeouts, shared in-flight refresh, and stale-response protection.
- Unity main-thread callbacks, persistentDataPath storage, and UnityWebRequest transport.
- Sample controls for local staging, remote refresh, explicit activation, and reload.

## Installation and local server

Use Package Manager > Add package from disk and select this package.json.
Unity resolves com.unity.nuget.newtonsoft-json 3.2.1.
Import Offline Config from Samples, then follow its README.

From the full bijiSDK repository root:

    python backend/publishing/publish.py init-demo
    python backend/delivery/server.py

The default sample URL is http://127.0.0.1:8765. The local backend uses no third-party
Python packages. Development HTTP is explicitly enabled; use HTTPS for production.
A phone's loopback address refers to the phone, so device access needs a deliberate
LAN/emulator address and appropriate development platform networking settings.

The intended minimum is Unity 6000.0.68f1. Windows Editor compilation, offline behavior,
and live HTTP integration checks passed. Android/iOS are the targets, with device,
IL2CPP, and later Unity-version verification still outstanding.

See [Public API](Documentation~/public-api.md) and [Integration](Documentation~/integration.md).

## Boundaries

BijiSDK.Runtime has no UnityEngine dependency. BijiSDK.Unity provides paths, callback
dispatch, and UnityWebRequest transport. Game feature classes and safe activation
timing belong to the game. Fetch completion never automatically activates new values.

Production hosting and publication access, Editor overrides, A/B tests, and targeting
remain separate work.
