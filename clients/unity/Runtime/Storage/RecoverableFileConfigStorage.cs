using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BijiSDK
{
    /// <summary>
    /// Stores complete records with a recoverable previous generation.
    /// Use one storage instance/writer for a given root directory.
    /// </summary>
    public sealed class RecoverableFileConfigStorage : IConfigStorage
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private readonly string rootDirectory;
        private readonly object gate = new object();

        public RecoverableFileConfigStorage(string rootDirectory)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory))
                throw new ArgumentException("A storage root directory is required.", nameof(rootDirectory));

            this.rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public IReadOnlyList<string> ReadCandidates(string cacheKey, ConfigSlot slot)
        {
            lock (gate)
            {
                Paths paths = GetPaths(cacheKey, slot);
                if (!Directory.Exists(paths.Directory) || File.Exists(paths.Deleted))
                    return Array.Empty<string>();

                var candidates = new List<string>();
                Exception firstFailure = null;
                var files = new List<string> { paths.Current };
                // Recovery files are former committed current records. Temporary writes
                // are deliberately excluded, even if they contain parseable JSON.
                files.AddRange(GetRecoveryFiles(paths));
                files.Add(paths.Backup);

                foreach (string file in files)
                {
                    if (!File.Exists(file))
                        continue;

                    try
                    {
                        candidates.Add(File.ReadAllText(file, Utf8));
                    }
                    catch (Exception exception) when (IsFileFailure(exception))
                    {
                        firstFailure = firstFailure ?? exception;
                    }
                }

                if (candidates.Count == 0 && firstFailure != null)
                    throw new IOException("No readable config storage candidate was available.", firstFailure);

                return candidates;
            }
        }

        public void Write(string cacheKey, ConfigSlot slot, string record)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            lock (gate)
            {
                Paths paths = GetPaths(cacheKey, slot);
                Directory.CreateDirectory(paths.Directory);

                // Keep a tombstone until the replacement record has been committed.
                // An interrupted write after Delete therefore cannot revive old data.
                bool wasDeleted = File.Exists(paths.Deleted);
                if (wasDeleted)
                    RemoveRecords(paths);

                WriteAndFlush(paths.Temporary, record);

                if (File.Exists(paths.Current))
                {
                    string recovery = paths.RecoveryPrefix + Guid.NewGuid().ToString("N") + ".json";
                    // A unique backup destination leaves the existing validated backup
                    // untouched until the new current record has committed.
                    File.Replace(paths.Temporary, paths.Current, recovery);
                    MaintainBackupAfterCommit(paths, recovery);
                }
                else
                {
                    File.Move(paths.Temporary, paths.Current);
                }

                if (wasDeleted)
                    File.Delete(paths.Deleted);
            }
        }

        public void Delete(string cacheKey, ConfigSlot slot)
        {
            lock (gate)
            {
                Paths paths = GetPaths(cacheKey, slot);
                Directory.CreateDirectory(paths.Directory);

                if (!File.Exists(paths.Deleted))
                {
                    WriteAndFlush(paths.DeleteTemporary, "deleted\n");
                    File.Move(paths.DeleteTemporary, paths.Deleted);
                }

                // Deletion is logically committed once the marker exists. Cleanup is
                // best effort; readers honor the marker even after interrupted cleanup.
                TryCleanup(() => RemoveRecords(paths));
            }
        }

        private static void WriteAndFlush(string path, string record)
        {
            byte[] bytes = Utf8.GetBytes(record);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void MaintainBackupAfterCommit(Paths paths, string recovery)
        {
            // Failure here must not report a failed write: current is already committed.
            // The uniquely named recovery remains available if rotation cannot finish.
            TryCleanup(() =>
            {
                if (File.Exists(paths.Backup))
                    File.Replace(recovery, paths.Backup, null);
                else
                    File.Move(recovery, paths.Backup);

                foreach (string olderRecovery in GetRecoveryFiles(paths))
                    File.Delete(olderRecovery);
            });
        }

        private static string[] GetRecoveryFiles(Paths paths)
        {
            return Directory.GetFiles(paths.Directory, paths.Name + ".recovery.*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        private static void RemoveRecords(Paths paths)
        {
            File.Delete(paths.Current);
            File.Delete(paths.Backup);
            File.Delete(paths.Temporary);
            File.Delete(paths.DeleteTemporary);
            foreach (string recovery in GetRecoveryFiles(paths))
                File.Delete(recovery);
        }

        private static void TryCleanup(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception exception) when (IsFileFailure(exception) ||
                                               exception is PlatformNotSupportedException)
            {
                // Durable current data or a durable deletion marker already determines
                // the visible result. A later operation can retry leftover cleanup.
            }
        }

        private static bool IsFileFailure(Exception exception)
        {
            return exception is IOException ||
                   exception is UnauthorizedAccessException ||
                   exception is DecoderFallbackException;
        }

        private Paths GetPaths(string cacheKey, ConfigSlot slot)
        {
            if (cacheKey == null || cacheKey.Length != 64 ||
                cacheKey.Any(character => !((character >= '0' && character <= '9') ||
                                             (character >= 'a' && character <= 'f'))))
            {
                throw new ArgumentException("Cache keys must be lowercase SHA-256 hex strings.", nameof(cacheKey));
            }

            string name;
            switch (slot)
            {
                case ConfigSlot.Active:
                    name = "active";
                    break;
                case ConfigSlot.Pending:
                    name = "pending";
                    break;
                case ConfigSlot.Fetch:
                    name = "fetch";
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(slot), slot, "Unknown config slot.");
            }

            return new Paths(Path.Combine(rootDirectory, cacheKey), name);
        }

        private sealed class Paths
        {
            internal Paths(string directory, string name)
            {
                Directory = directory;
                Name = name;
                string prefix = Path.Combine(directory, name);
                Current = prefix + ".json";
                Backup = prefix + ".backup.json";
                Temporary = prefix + ".tmp";
                Deleted = prefix + ".deleted";
                DeleteTemporary = prefix + ".delete.tmp";
                RecoveryPrefix = prefix + ".recovery.";
            }

            internal string Directory { get; }
            internal string Name { get; }
            internal string Current { get; }
            internal string Backup { get; }
            internal string Temporary { get; }
            internal string Deleted { get; }
            internal string DeleteTemporary { get; }
            internal string RecoveryPrefix { get; }
        }
    }
}
