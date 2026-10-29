# Architecture

## Ownership

| Component | Owns |
| --- | --- |
| Backend | Configuration publication, environment isolation, revision history, compatible response selection |
| Shared contracts | Wire format, status semantics, compatibility rules, cross-client examples |
| Unity package | Fetch coordination, generic validation, persistence, active/pending state, diagnostics |
| Game | Registered typed definitions, defaults, feature-specific validation, safe activation points |
| Future clients | Equivalent behavior using the same API and contract cases |

The backend must not expose Unity class names, serialized Unity assets, or Unity-specific identifiers.
Feature data is ordinary JSON. Each game defines its feature schemas independently of the transport schema.

## Unity boundaries

Runtime/Core contains platform-independent coordination and state rules.
Runtime/Contracts contains public interfaces and wire models.
Runtime/Transport adapts Unity networking to the transport interface.
Runtime/Storage adapts local filesystem operations to the storage interface.
Runtime/Diagnostics exposes status without embedding a particular analytics SDK.
Editor contains inspection and local override tools and must not be referenced by Runtime.

Feature classes such as EnergyConfig belong to the game. The package does not depend on
game singletons, scene objects, advertising, purchase, analytics, or cgSDK APIs.

The package supports one initialized config context per client instance. Multiple instances
may use different projects/environments; no global mutable singleton is required.

## Data flow

Initialize definitions and defaults; restore compatible saved state; expose active reads.
Refresh obtains and validates a complete response, then persists it as pending.
Explicit activation replaces the resolved active configuration as one operation.
Game code reads immutable values from that active configuration.

## Persistence boundary

Persist the server response plus its request context, ETag, and validation metadata.
Keep active and pending records distinct. Do not persist C# type names as wire data.
Validate saved records against the current definitions before reuse.

## Deferred capabilities

A/B assignment, user targeting, content/asset delivery, background OS jobs, and a web
administration dashboard are outside v1. Future targeting requires an explicit identity,
cache-partition, assignment, and exposure contract; it must not be bolted onto an
unpartitioned global cache.
