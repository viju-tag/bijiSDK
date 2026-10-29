# API contract v1

## Request

GET /v1/projects/{projectId}/environments/{environment}/config?platform=android&appBuild=120&configContractVersion=1

Use HTTPS in deployed environments. A development-only local transport may use HTTP.
Project/environment are path segments. Platform, app build, and config contract version
are required query parameters and must be URL encoded correctly.

- projectId and environment: lowercase identifiers matching ^[a-z][a-z0-9-]*$.
- platform: a lowercase identifier; android, ios, and editor are example values.
- appBuild: nonnegative integer, monotonically increasing within a project's platform.
- configContractVersion: positive integer describing the game's feature schema.
- No player identifier or targeting attributes are sent in v1.
- Config keys are case-sensitive. Numeric feature bounds are defined by the game schema;
  values must be finite and interoperable across supported clients. Avoid integers beyond
  the exact JSON interoperability range (-(2^53 - 1) through 2^53 - 1).

The logical endpoint may be served by a custom service or an adapter over hosted JSON.
The storage provider's URLs and layout are not the public SDK contract.

## Response

See ../contracts/schemas/config-response.schema.json and
../contracts/examples/valid-config.json.

schemaVersion describes the transport envelope. configContractVersion describes
game feature data. revision is an opaque publication identifier, not a number clients
may sort. publishedAt is UTC metadata, not the ordering mechanism.

The response echoes projectId, environment, platform, and configContractVersion.
compatibility contains inclusive minAppBuild and maxAppBuild bounds; null maxAppBuild
means no upper bound. Explicit bounds allow offline compatibility checks. The server
selects a compatible publication and the client independently verifies these fields.

Each 200 response is a full configuration. Patch responses are outside v1.
A values entry may contain any JSON value; a registered feature schema determines
which types are allowed for that key.

Unknown envelope fields and unknown feature keys are tolerated. Required fields
and registered feature content remain validated. JSON Schema alone cannot enforce
all context checks: the client must compare the response against its request and
verify minAppBuild <= maxAppBuild when a maximum exists.

## Missing values

An absent registered top-level key resolves to that key's bundled default.
A present value must pass that key's type and semantic validator. A malformed present
value rejects the entire response; it does not silently resolve to a default.
Required nested fields are defined by the game feature schema. Do not merge nested
objects or copy missing settings from a previous remote revision.

An empty values object is valid and means use defaults for every registered key.
An empty HTTP body is invalid.

## HTTP outcomes

| Status | Required handling |
| --- | --- |
| 200 | Parse, validate, persist as pending; update freshness only after success |
| 304 | Reuse the validated representation associated with the sent ETag; no new revision |
| 400 | Configuration/request error; no automatic retries |
| 401 / 403 | Access error; preserve active data; no automatic retries |
| 404 | No matching resource/config; preserve active data; use defaults if no active remote data |
| 429 | Honor Retry-After; never retry earlier than that value |
| 5xx / network timeout | Retry within the bounded refresh policy |

Send If-None-Match only when a validated, compatible local representation for that
exact request context is available. Keep the ETag paired with that representation.
A 304 without the matching representation triggers one unconditional recovery fetch,
subject to the refresh operation's request budget.

ETags describe the complete selected response. Backend/CDN cache keys must vary by
project, environment, platform, appBuild, and configContractVersion. A failed response
must never be stored as a successful empty config.

## Compatibility

Unsupported schemaVersion or configContractVersion rejects an update.
The client must not accept a response for another project/environment/platform.
A remote revision can be republished with old values for rollback; the new publication
gets a new revision and ETag. Clients must not reject a valid rollback based on timestamps
or lexicographic revision comparisons.

## Access model

Client configuration is readable data, not a place for secrets. Publishing requires
separate authenticated access. Provider-specific authentication is an open decision;
administrative credentials must never be embedded in the Unity package.
