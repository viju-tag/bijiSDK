# Development backend checks

    python backend/tests/test_development_server.py

Fifteen stdlib unittest cases cover 200/304 and ETags, context/build selection,
publication/rollback, preconditions, invalid publications, atomic pointer failure,
read-only HTTP, corrupt published records, strict JSON validation, and exclusive locks.

Servers use ephemeral loopback ports. Test directories stay here and are cleaned after
the servers stop. No external service is accessed.
