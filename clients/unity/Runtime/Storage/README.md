# Recoverable file storage

`RecoverableFileConfigStorage` implements the offline client's synchronous storage
interface without depending on Unity. The core calls it from its serialized async
mutation path.

## Record layout

Each lowercase SHA-256 cache key receives its own directory. Active and pending slots
have separate current, backup, temporary, deletion-marker, and recovery files.

Reads return current, interrupted-rotation recovery records (newest filesystem time
first), then backup. The client validates each candidate and uses the first valid one.
Uncommitted temporary files are never returned. Filesystem timestamps only prioritize
fallbacks; they do not determine publication revision order.

Writes flush a temporary file to disk, then move it for the first record or use
`File.Replace` for replacement. Replacement uses a unique recovery path, preserving the
existing backup until current has committed. Backup rotation and stale recovery cleanup
are best effort after the commit.

Deletion first commits a flushed marker. Readers ignore all old slot records while
the marker exists, so an interrupted cleanup cannot resurrect an older pending record.
A later successful write clears the marker after committing its replacement.

## Constraints and verification

- One writer/storage instance per root directory; the instance serializes its own calls.
  Cross-process and independent-instance writers are not supported.
- The root is trusted application storage. Cache keys and slot values are validated.
- The implementation requires filesystem move/replacement semantics. If `File.Replace`
  is unsupported, the write fails instead of deleting the old current record.
- Flushes and same-directory replacement reduce interruption windows. Sudden-power-loss
  durability depends on the platform/filesystem; directory fsync is not provided by
  this portable implementation.
- No Android/iOS device behavior is claimed until device verification is performed.

Planned checks include corrupt-current fallback, temp exclusion, failed replacement,
interrupted deletion, context isolation, invalid cache keys, and active/pending separation.
