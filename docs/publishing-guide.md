# Publishing and rollback

1. Select project, environment, platform, app compatibility, and game contract version.
2. Validate the transport envelope and the registered game schemas.
3. Validate range and compatibility rules not expressible by the schema alone.
4. Preview the full change from the active publication.
5. Create an immutable revision containing the complete config.
6. Upload and verify the revision before switching the active publication pointer.
7. Record author, UTC publication time, reason, and the revision it replaces.
8. Verify delivery through the public retrieval path in the intended environment.

Development, QA, and production have distinct publication state and access controls.
Promotion preserves reviewed feature values while creating an environment-appropriate
envelope, revision, timestamp, and ETag. Do not copy a QA envelope directly into production.

Rollback publishes a new revision with previously approved values and compatibility
metadata. The Unity client handles it as any other validated update, subject to explicit
activation. Rollback is not guaranteed to apply instantly to offline or active gameplay.

Concurrency control must prevent one publisher from accidentally overwriting another's
activation pointer. Use a provider-supported compare-and-set/precondition mechanism.

Validate manifest/pointer and CDN cache behavior so a published pointer cannot refer to
an unavailable payload. Do not allow long-lived stale pointers to make rollback ineffective.

A command-line publisher is sufficient for the first milestone. A dashboard is deferred.
Backend provider, access management, retention period, and operational ownership are open.
