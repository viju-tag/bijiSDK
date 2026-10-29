using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BijiSDK;
using Newtonsoft.Json.Linq;

namespace BijiSDK.OfflineChecks
{
    internal sealed class Energy
    {
        public int Maximum { get; set; }
        public int RefillSeconds { get; set; }
    }

    internal sealed class AdSettings
    {
        public bool Enabled { get; set; }
        public int CooldownSeconds { get; set; }
    }

    internal sealed class ImmediateDispatcher : IMainThreadDispatcher
    {
        public void Post(Action action) => action();
    }

    internal sealed class MemoryStorage : IConfigStorage
    {
        private readonly Dictionary<string, List<string>> records = new Dictionary<string, List<string>>();
        public ConfigSlot? FailWriteSlot { get; set; }
        public bool FailDelete { get; set; }

        private static string Key(string cacheKey, ConfigSlot slot) => cacheKey + "/" + slot;

        public IReadOnlyList<string> ReadCandidates(string cacheKey, ConfigSlot slot)
        {
            return records.TryGetValue(Key(cacheKey, slot), out var values)
                ? values.ToArray() : Array.Empty<string>();
        }

        public void Write(string cacheKey, ConfigSlot slot, string record)
        {
            if (FailWriteSlot == slot) throw new IOException("Injected write failure.");
            var key = Key(cacheKey, slot);
            var old = records.TryGetValue(key, out var values) ? values : new List<string>();
            var updated = new List<string> { record };
            if (old.Count > 0) updated.Add(old[0]);
            records[key] = updated;
        }

        public void Delete(string cacheKey, ConfigSlot slot)
        {
            if (FailDelete) throw new IOException("Injected cleanup failure.");
            records.Remove(Key(cacheKey, slot));
        }

        public void CorruptCurrent(string cacheKey, ConfigSlot slot)
        {
            records[Key(cacheKey, slot)][0] = "{corrupted";
        }
    }

    internal static class TestSupport
    {
        internal static string Root { get; set; }
        internal static ConfigOptions Options(string environment = "production", long build = 120)
            => new ConfigOptions("example-game", environment, "android", build, 1);

        internal static IConfigDefinition[] Definitions()
        {
            return new IConfigDefinition[]
            {
                new ConfigDefinition<Energy>("energy",
                    new Energy { Maximum = 80, RefillSeconds = 180 },
                    x => new JObject { ["maximum"] = x.Maximum, ["refillSeconds"] = x.RefillSeconds },
                    token => new Energy
                    {
                        Maximum = ReadInt(token, "maximum"),
                        RefillSeconds = ReadInt(token, "refillSeconds")
                    },
                    x => x.Maximum > 0 && x.RefillSeconds > 0 ? null : "Energy values must be positive."),
                new ConfigDefinition<AdSettings>("interstitial",
                    new AdSettings { Enabled = false, CooldownSeconds = 120 },
                    x => new JObject { ["enabled"] = x.Enabled, ["cooldownSeconds"] = x.CooldownSeconds },
                    token => new AdSettings
                    {
                        Enabled = ReadBool(token, "enabled"),
                        CooldownSeconds = ReadInt(token, "cooldownSeconds")
                    },
                    x => x.CooldownSeconds >= 0 ? null : "Cooldown cannot be negative.")
            };
        }

        private static int ReadInt(JToken token, string key)
        {
            if (token[key]?.Type != JTokenType.Integer) throw new FormatException("Required integer: " + key);
            return token[key].Value<int>();
        }

        private static bool ReadBool(JToken token, string key)
        {
            if (token[key]?.Type != JTokenType.Boolean) throw new FormatException("Required bool: " + key);
            return token[key].Value<bool>();
        }

        internal static ConfigClient Client(IConfigStorage storage = null, ConfigOptions options = null)
            => new ConfigClient(options ?? Options(), Definitions(), storage ?? new MemoryStorage(), new ImmediateDispatcher());

        internal static string Fixture(string name = "valid-config.json")
            => File.ReadAllText(Path.Combine(Root, "contracts", "examples", name));

        internal static string Changed(string revision, Action<JObject> change = null)
        {
            var response = JObject.Parse(Fixture());
            response["revision"] = revision;
            change?.Invoke(response);
            return response.ToString();
        }

        internal static void Equal<T>(T expected, T actual, string message = "")
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception(message + " Expected: " + expected + "; actual: " + actual);
        }

        internal static void True(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }

        internal static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }

        internal static async Task Activate(ConfigClient client, string json = null)
        {
            Equal(ConfigOutcome.UpdateAvailable, (await client.StageJsonAsync(json ?? Fixture())).Outcome);
            Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
        }
    }
}
