# Local publication CLI

Run python backend/publishing/publish.py --help from the project root.

Commands: init-demo, publish, rollback. Global --data-dir must precede the command.
init-demo accepts --project and --environment overrides.

store.py checks duplicate JSON keys, finite/interoperable numbers, depth, size,
identifiers, UTC timestamps, and compatibility bounds. Generic publication does not
assume game schemas; --validate-example-features checks the supplied demonstration.

Each publication writes and flushes an immutable response/audit file, then atomically
replaces the index. Failed index replacement leaves the previous index active;
orphaned files are not served.

An exclusive lock rejects concurrent publishers for a context. --expected-revision
checks the indexed revision. Crash leftovers require manual inspection.
Full-machine power-loss durability is platform-dependent; this is a local tool.

Audit metadata is capped at 4 KiB and index history at 16 MiB.
Production permissions, retention, and disaster recovery remain future decisions.
