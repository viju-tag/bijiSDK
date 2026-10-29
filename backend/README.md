# bijiSDK local development backend

The stdlib Python server and publishing CLI implement the shared config delivery
contract for development. Python 3.12 is sufficient; no pip dependencies are needed.

## Start the Unity sample

From the bijiSDK project root:

    python backend/publishing/publish.py init-demo
    python backend/delivery/server.py

The seed publishes separate editor, android, and ios responses for
bijisdk-offline-sample / development, app builds 100..199, game contract 1.
The server listens on http://127.0.0.1:8765 and serves the Unity sample's default URL.
Stop the server with Ctrl+C.

To isolate a test run, use --data-dir before the publishing command:

    python backend/publishing/publish.py --data-dir backend/dev-data-test init-demo
    python backend/delivery/server.py --data-dir backend/dev-data-test --port 8766

## Publish an update

Edit a copy of the sample JSON, then publish it:

    python backend/publishing/publish.py publish --file clients/unity/Samples~/OfflineConfig/offline-config.json --platform editor --reason "Test energy update" --validate-example-features

The CLI creates a fresh opaque revision and UTC timestamp and prints metadata as JSON.
The input file's revision and timestamp are validation placeholders; they are replaced
in the new immutable publication. Publish separately for other platforms.

Unknown feature keys are allowed. --validate-example-features validates the illustrative
energy/interstitial schemas; real games must supply their own feature validators before
production publishing.

## Roll back

Copy a previously printed revision and publish its old values under a new revision:

    python backend/publishing/publish.py rollback --project bijisdk-offline-sample --environment development --platform editor --contract-version 1 --revision release-REPLACE_WITH_PREVIOUS_ID --reason "Restore previous values"

Rollback creates a new publication and ETag without editing the old file.
Optional --expected-revision on publish/rollback rejects a changed publication pointer.

## Rules

- HTTP delivery is read-only; publication is a local CLI/filesystem operation.
- Data defaults to backend/dev-data, isolated by project/environment/platform/contract.
- Revisions and audit records are immutable; the index is replaced atomically.
- Requests select the newest published revision compatible with the requested build.
  An older compatible publication remains available when the latest targets newer builds.
- Missing/incompatible contexts return 404. Invalid query parameters return 400.
- Matching If-None-Match returns 304 without a body.
- Corrupt publications return 500; unpublished orphan files are not served.
- Audit records include author, reason, previous revision, and rollback source.
- An exclusive per-context lock prevents concurrent publication. A crash can leave
  publish.lock; verify no publisher is running before removing a stale lock manually.

This is a development backend. TLS termination, authentication, remote administration,
distributed locking, and production retention are not supplied.
No server is automatically started in the background.

## Device access

Loopback on a phone refers to the phone. For device testing, explicitly select a LAN
host/address and configure the sample URL appropriately. SDK development HTTP opt-in
and platform cleartext allowances are separate requirements. Do not disable certificate
validation or ship development cleartext settings as production defaults.

## Tests

    python backend/tests/test_development_server.py

Tests use short-lived loopback servers on ephemeral ports and isolated directories under
backend/tests. No service remains running after completion.
