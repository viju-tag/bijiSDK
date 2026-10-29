# Local HTTP delivery

    python backend/delivery/server.py --host 127.0.0.1 --port 8765

Optional --data-dir selects storage. --port 0 selects an ephemeral port; startup prints
the actual listening URL as JSON. GET /health is a readiness check.

Config route:

    GET /v1/projects/{projectId}/environments/{environment}/config?platform=editor&appBuild=120&configContractVersion=1

Responses have strong SHA-256 ETags over the exact bytes. If-None-Match supports
weak/list validators and wildcard. A 304 has no body and preserves the ETag.
Cache-Control: no-cache keeps freshness under the SDK's policy.

Only indexed publications are served. The newest compatible revision wins.
There is no HTTP publishing endpoint. Nonloopback access requires explicit --host.
TLS, authentication, rate limiting, and fault-injection endpoints are not implemented.
Client retry/throttle behavior is checked through injected transports.
