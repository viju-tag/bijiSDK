using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BijiSDK;

namespace BijiSDK
{
    public enum ConfigSlot { Active, Pending, Fetch }
    public interface IConfigStorage
    {
        IReadOnlyList<string> ReadCandidates(string cacheKey, ConfigSlot slot);
        void Write(string cacheKey, ConfigSlot slot, string record);
        void Delete(string cacheKey, ConfigSlot slot);
    }
}

public static class Program
{
    public static void Main()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "runs", Guid.NewGuid().ToString("N")));
        string key = new string('a', 64);
        var storage = new RecoverableFileConfigStorage(root);
        void Check(bool condition, string name)
        {
            if (!condition) throw new Exception(name);
            Console.WriteLine("PASS: " + name);
        }
        Check(storage.ReadCandidates(key, ConfigSlot.Active).Count == 0, "fresh storage");
        storage.Write(key, ConfigSlot.Active, "first");
        storage.Write(key, ConfigSlot.Active, "second");
        Check(storage.ReadCandidates(key, ConfigSlot.Active).SequenceEqual(new[] { "second", "first" }), "replacement retains backup");
        string directory = Path.Combine(root, key);
        File.WriteAllText(Path.Combine(directory, "active.tmp"), "uncommitted");
        Check(!storage.ReadCandidates(key, ConfigSlot.Active).Contains("uncommitted"), "temporary record excluded");
        File.WriteAllText(Path.Combine(directory, "active.json"), "corrupt");
        Check(storage.ReadCandidates(key, ConfigSlot.Active).SequenceEqual(new[] { "corrupt", "first" }), "backup remains available after corruption");
        storage.Write(key, ConfigSlot.Active, "third");
        Check(storage.ReadCandidates(key, ConfigSlot.Active)[0] == "third", "write replaces corrupted current");
        using (var held = new FileStream(Path.Combine(directory, "active.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            bool failed = false;
            try { storage.Write(key, ConfigSlot.Active, "blocked"); }
            catch (IOException) { failed = true; }
            Check(failed, "locked replacement reports failure");
        }
        Check(storage.ReadCandidates(key, ConfigSlot.Active)[0] == "third", "failed replacement preserves current");
        storage.Write(key, ConfigSlot.Pending, "pending");
        storage.Delete(key, ConfigSlot.Pending);
        File.WriteAllText(Path.Combine(directory, "pending.backup.json"), "stale");
        Check(storage.ReadCandidates(key, ConfigSlot.Pending).Count == 0, "deletion marker prevents resurrection");
        storage.Write(key, ConfigSlot.Pending, "new pending");
        Check(storage.ReadCandidates(key, ConfigSlot.Pending).SequenceEqual(new[] { "new pending" }), "write after deletion clears stale records");
        bool rejected = false;
        try { storage.ReadCandidates("../escape", ConfigSlot.Active); }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unsafe cache key rejected");
        rejected = false;
        try { storage.ReadCandidates(key, (ConfigSlot)99); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "unknown slot rejected");
        Check(storage.ReadCandidates(key, ConfigSlot.Active)[0] == "third", "slots stay isolated");
        Console.WriteLine("Storage test data: " + root);
    }
}
