# GitHub Pages mobile testing

The test site is intended to be served at `https://viju-tag.github.io/bijiSDK/`.
The `site/` directory contains only public demonstration configs and a small index page.
Repository visibility is public for this testing phase. A later move to private source
and different hosting requires a separate repository/hosting change.

## Unity sample setup

1. Add `clients/unity/package.json` through Package Manager and import the Offline Config sample.
2. Attach `OfflineConfigSample` to an empty GameObject.
3. Enable `useRemoteServer` and `useGitHubPages`.
4. Set `pagesBaseUrl` to `https://viju-tag.github.io/bijiSDK/`.
5. Pages mode always enforces HTTPS and ignores `allowDevelopmentHttp`; no cleartext exception is needed.
6. Run in the Editor or build for Android/iOS. The sample selects the matching platform.

The sample context is project `bijisdk-offline-sample`, environment `development`,
app build `120`, contract version `1`. An existing saved config may replace bundled
defaults on startup. For a clean first-launch test use a fresh installation/data directory.

The Pages transport maps the logical request to a static file, for example:

`https://viju-tag.github.io/bijiSDK/configs/bijisdk-offline-sample/development/android/contract-1/config.json`

The shared backend API stays unchanged. This adapter is a sample-only option. Pages
cannot select a response from query parameters or run the Python delivery server.
The client still validates the returned platform, project, environment, build range,
and contract version. The supplied fixtures allow app build 120 only.

## Device checklist

- First launch: bundled energy defaults are 80 maximum and 180 refill seconds.
- Refresh: a pending revision appears; current gameplay values remain unchanged.
- Activate pending: energy becomes 100 maximum and 120 refill seconds.
- Restart without network: the committed active values should restore.
- Restore network and refresh: successful unchanged responses must preserve active state.
- Publish a changed fixture with a new revision, refresh, and verify it stays pending until activation.
- Background/resume the app and verify active values remain readable while refresh runs.

Record results independently for Android and iOS, including IL2CPP builds. Hosted JSON
availability or passing Editor tests does not establish mobile compatibility.

## Updating and rolling back fixtures

Edit the appropriate platform's `site/configs/.../config.json`. Keep its context fields
and compatibility correct. For every changed publication, assign a new opaque `revision`
and UTC `publishedAt`. Never change values while reusing a published revision.
Deploy the updated site, check the exact JSON URL, then use the sample's development
force-refresh button or wait for freshness expiry. Force refresh bypasses the client's
15-minute freshness interval; it does not bypass CDN propagation, HTTP caching,
failure cooldown, or server throttling. Conditional responses and ETags are controlled
by the host. A normal 200 with unchanged values is also valid.

To roll back, restore the older values under another new revision and publish again.
The client applies rollback only after explicit activation. Keep Git history to retain
previous fixture contents; the static file itself represents the latest test publication.

## Limits

These are public, non-secret test values. Do not place credentials, private game data,
or player data in the site. This static test path does not perform server-side app-build
selection, targeting, authentication, or publishing access control. Different build ranges
need separately selected test fixtures or a real backend.

GitHub Pages cannot provide the controlled 429, delayed, malformed, or oversized response
scenarios used by `verification/Run-HttpUnityChecks.ps1`; retain the local fault server
for those checks. Mobile offline behavior and lifecycle/storage failures still require
device testing. The Python backend remains available for local API-contract tests.
