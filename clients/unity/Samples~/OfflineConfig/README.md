# Local and HTTP configuration sample

## Setup

1. Install bijiSDK in Unity 6000.0.68f1 and import the Offline Config sample.
2. Create an empty GameObject and attach OfflineConfigSample.
3. Leave Use Remote Server enabled to use the development server below.
4. The default server base URL is http://127.0.0.1:8765.
5. Enter Play mode. Defaults become readable before the background startup fetch completes.

The sample targets project bijisdk-offline-sample, environment development, app build 120,
game contract 1. It derives editor/android/ios from the running platform.

## Start the development source

From the bijiSDK project root, run:

    python backend/publishing/publish.py init-demo
    python backend/delivery/server.py

Keep the server terminal open while testing. Initialization seeds an example publication
for Editor, Android, and iOS; the server defaults to port 8765 and backend/dev-data.

To publish an edited example:

    python backend/publishing/publish.py publish --file clients/unity/Samples~/OfflineConfig/offline-config.json --platform editor --reason "Test update" --validate-example-features

Publishing generates a fresh revision. See backend documentation for full rollback,
platform, data-directory, and server options.

## Controls and expected behavior

- **Refresh from server** respects the normal minimum interval.
- **Force refresh (development)** bypasses freshness, but not server throttling or retry bounds.
- A fetch changes the pending revision only. Active gameplay values remain unchanged.
- **Activate pending** applies the complete validated config.
- **Reload saved state** recreates the client without automatically fetching or activating.
- Stop the server, reload, and confirm the active values remain available offline.
- Publish changed values, force refresh, activate, and repeat with a rollback publication.
- A normal refresh after a successful fetch may return FreshCache. Force refresh when
  deliberately testing ETag/304 behavior in the Editor.

Status labels show active/pending revisions, last attempt/success, and next eligible time.
Startup and resume request a background refresh; the SDK applies its freshness/backoff rules.
Active reads and local controls remain available while fetching. Reload waits until fetch
completion to avoid replacing an in-flight client.

## Local-only checks

Disable Use Remote Server for the original offline demonstration.
Optionally assign offline-config.json to Sample Json. Otherwise the component builds the
same local example in memory. Stage sample JSON validates and saves it as pending;
activation is always explicit.

Defaults are energy maximum 80 / refill 180 seconds; the bundled example uses maximum 100 / refill 120 seconds.
Changing a present value to an invalid value rejects the complete update. When editing
local JSON, change its revision as well; revision reuse with different content is rejected.

Local staging is authoritative. Staging an active revision cancels a different pending
revision. Do not mix local overrides and HTTP publications without understanding this rule.

## Android and iOS development

127.0.0.1 refers to the device itself. Android emulator commonly uses 10.0.2.2 for the host;
physical devices need a reachable development-machine LAN address and a server listening
on that interface. Follow the server's explicit bind options and local firewall settings.

The Allow Development Http setting is accepted only in the Editor/development builds.
Android cleartext and iOS transport policy may still block plain HTTP. Configure a narrow
development policy in the consuming test project or use HTTPS. The SDK never edits those
project/platform policies and never bypasses certificate validation.
Use HTTPS for release builds.

The cache lives under Application.persistentDataPath/bijisdk. This sample's project/context
is separate from real game config. Removing a test cache requires stopping its client first.

## Implementation notes

EnergySettings is immutable, with explicit JSON conversion and required-field validation.
The sample cancels work and detaches events when destroyed. It does not auto-activate a
downloaded revision. Device/IL2CPP execution remains a separate validation requirement.

## GitHub Pages mobile test mode

Enable **Use Remote Server** and **Use GitHub Pages**. Leave **Pages Base Url** at
`https://viju-tag.github.io/bijiSDK/`. This mode requires HTTPS and ignores
Allow Development Http, so no cleartext exception is needed on mobile.

The sample-only PagesConfigTransport maps its known API context to
`configs/bijisdk-offline-sample/development/{platform}/contract-1/config.json`.
The runtime API and production transport remain unchanged. Exact JSON URLs avoid
redirects. The response still passes all context, build, schema, and feature validation.

Test on each device: start with defaults, fetch, check pending values, explicitly
activate, then restart offline and confirm saved values. To test a later update,
change the fixture values and its revision, publish Pages, then force refresh in a
development build. Pages deployment/CDN caching can delay visibility. Rollback also
needs a fresh revision. Static hosting does not implement build-based routing,
controlled errors, or publishing endpoints; use the local fault tests for those cases.

Fixtures currently cover app build 120 and game contract 1. This is public test data,
not a place for secrets or production game configuration.
