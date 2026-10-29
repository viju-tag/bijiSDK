# Shared contracts

The schemas and examples define the proposed bijiSDK v1 wire contract.
The example game schemas and values are illustrative; they are not production game settings.

- schemas/config-response.schema.json: common response envelope, JSON Schema 2020-12.
- schemas/example-game-values.schema.json: example registered feature validators.
- examples/: complete response fixtures and bundled defaults.
- compatibility/cases.json: expected outcomes, including intentionally invalid examples.

Validate a response envelope first, then values against the game's feature schema, then
perform context/compatibility and semantic checks. Enable date-time format checking.
A schema-valid response can still be invalid for a particular request context.

All paths in cases.json are relative to that file. Negative cases are intentional and
must not be published. Test consumers should preserve the distinction between transport
schema validity, feature validity, and request compatibility.
