using System;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK;
using Newtonsoft.Json.Linq;
using static BijiSDK.OfflineChecks.TestSupport;

namespace BijiSDK.OfflineChecks
{
    internal static class CoreChecks
    {
        internal static async Task DefaultsAndIsolation()
        {
            using var client = Client();
            Equal(ConfigOutcome.ReadyWithDefaults, (await client.InitializeAsync()).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal(ConfigValueSource.Default, client.GetSnapshot().GetSource("energy"));
            var copy = client.Get<Energy>("energy");
            copy.Maximum = 999;
            Equal(80, client.Get<Energy>("energy").Maximum, "Read objects must not mutate state.");
            Throws<InvalidOperationException>(() => client.Get<AdSettings>("energy"));
            Throws<System.Collections.Generic.KeyNotFoundException>(() => client.Get<Energy>("missing"));
        }

        internal static async Task StageActivateAndSnapshots()
        {
            using var client = Client();
            await client.InitializeAsync();
            var initial = client.GetSnapshot();
            var updates = 0;
            var activations = 0;
            client.UpdateAvailable += _ => updates++;
            client.ConfigActivated += _ => activations++;
            Equal(ConfigOutcome.UpdateAvailable, (await client.StageJsonAsync(Fixture())).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal(0, activations);
            Equal("release-000042", client.GetStatus().PendingRevision);
            Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
            Equal(100, client.Get<Energy>("energy").Maximum);
            Equal(80, initial.Get<Energy>("energy").Maximum, "Captured snapshot must remain stable.");
            Equal(ConfigValueSource.Remote, client.GetSnapshot().GetSource("energy"));
            Equal(1, updates);
            Equal(1, activations);
            Equal(ConfigOutcome.NoPendingUpdate, (await client.ActivatePendingAsync()).Outcome);
        }

        internal static async Task RestoreActiveAndPending()
        {
            var storage = new MemoryStorage();
            using (var first = Client(storage))
            {
                await first.InitializeAsync();
                await Activate(first);
                await first.StageJsonAsync(Changed("next", x => x["values"]["energy"]["maximum"] = 150));
            }
            using var restored = Client(storage);
            Equal(ConfigOutcome.ReadyWithSavedConfig, (await restored.InitializeAsync()).Outcome);
            Equal(100, restored.Get<Energy>("energy").Maximum);
            Equal("next", restored.GetStatus().PendingRevision);
            await restored.ActivatePendingAsync();
            Equal(150, restored.Get<Energy>("energy").Maximum);
        }

        internal static async Task PendingOnlySurvivesRestart()
        {
            var storage = new MemoryStorage();
            using (var first = Client(storage))
            {
                await first.InitializeAsync();
                await first.StageJsonAsync(Fixture());
            }
            using var restored = Client(storage);
            await restored.InitializeAsync();
            Equal(80, restored.Get<Energy>("energy").Maximum);
            Equal("release-000042", restored.GetStatus().PendingRevision);
            await restored.ActivatePendingAsync();
            Equal(100, restored.Get<Energy>("energy").Maximum);
        }

        internal static async Task MissingAndEmptyUseDefaults()
        {
            using var client = Client();
            await client.InitializeAsync();
            await Activate(client);
            True(client.Get<AdSettings>("interstitial").Enabled, "Fixture should enable ads.");
            await Activate(client, Fixture("missing-key-config.json"));
            Equal(false, client.Get<AdSettings>("interstitial").Enabled);
            Equal(ConfigValueSource.Default, client.GetSnapshot().GetSource("interstitial"));
            await Activate(client, Fixture("empty-values-config.json"));
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal(ConfigValueSource.Default, client.GetSnapshot().GetSource("energy"));
        }

        internal static async Task InvalidResponsesPreserveState()
        {
            using var client = Client();
            await client.InitializeAsync();
            await client.StageJsonAsync(Fixture());
            foreach (var name in new[] { "invalid-feature-config.json", "unsupported-schema-config.json", "wrong-context-config.json" })
                Equal(ConfigOutcome.Failed, (await client.StageJsonAsync(Fixture(name))).Outcome, name);
            Equal("release-000042", client.GetStatus().PendingRevision);
            await client.ActivatePendingAsync();
            Equal(100, client.Get<Energy>("energy").Maximum);
        }

        internal static async Task StrictEnvelopeValidation()
        {
            using var client = Client();
            await client.InitializeAsync();
            var invalid = new[]
            {
                "{",
                Fixture() + "{}",
                Fixture().Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"),
                Changed("bad-build", x => x["compatibility"]["maxAppBuild"] = 119),
                Changed("bad-range", x => { x["compatibility"]["minAppBuild"] = 150; x["compatibility"]["maxAppBuild"] = 100; }),
                Changed("wrong-platform", x => x["platform"] = "ios"),
                Changed("wrong-project", x => x["projectId"] = "other-game"),
                Changed("wrong-contract", x => x["configContractVersion"] = 2),
                Changed("bad-date", x => x["publishedAt"] = "not-a-date"),
                Changed("missing-nested", x => ((JObject)x["values"]["energy"]).Remove("maximum")),
                Changed("null-feature", x => x["values"]["energy"] = JValue.CreateNull()),
                Changed("nonfinite", x => x["values"]["unknown"] = double.NaN).Replace("\"NaN\"", "NaN")
            };
            foreach (var json in invalid)
                Equal(ConfigOutcome.Failed, (await client.StageJsonAsync(json)).Outcome, json.Substring(0, Math.Min(80, json.Length)));
            Equal(80, client.Get<Energy>("energy").Maximum);
        }

        internal static async Task RevisionsAndRollback()
        {
            using var client = Client();
            await client.InitializeAsync();
            var events = 0;
            client.UpdateAvailable += _ => events++;
            await client.StageJsonAsync(Fixture());
            Equal(ConfigOutcome.Unchanged, (await client.StageJsonAsync(Fixture())).Outcome);
            Equal(1, events);
            Equal(ConfigOutcome.Failed, (await client.StageJsonAsync(Changed("release-000042", x => x["values"]["energy"]["maximum"] = 999))).Outcome);
            await client.ActivatePendingAsync();
            Equal(ConfigOutcome.Unchanged, (await client.StageJsonAsync(Fixture())).Outcome);
            await Activate(client, Fixture("rollback-config.json"));
            Equal(90, client.Get<Energy>("energy").Maximum);
        }

        internal static async Task FailedWritesDoNotApply()
        {
            var storage = new MemoryStorage();
            using var client = Client(storage);
            await client.InitializeAsync();
            await client.StageJsonAsync(Fixture());
            storage.FailWriteSlot = ConfigSlot.Pending;
            Equal(ConfigOutcome.Failed, (await client.StageJsonAsync(Changed("new"))).Outcome);
            Equal("release-000042", client.GetStatus().PendingRevision);
            storage.FailWriteSlot = ConfigSlot.Active;
            Equal(ConfigOutcome.Failed, (await client.ActivatePendingAsync()).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal("release-000042", client.GetStatus().PendingRevision);
            storage.FailWriteSlot = null;
            await client.ActivatePendingAsync();
            Equal(100, client.Get<Energy>("energy").Maximum);
        }

        internal static async Task CleanupFailureCannotReactivate()
        {
            var storage = new MemoryStorage();
            using (var client = Client(storage))
            {
                await client.InitializeAsync();
                await client.StageJsonAsync(Fixture());
                storage.FailDelete = true;
                Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
            }
            using var restored = Client(storage);
            await restored.InitializeAsync();
            Equal(100, restored.Get<Energy>("energy").Maximum);
            Equal(ConfigOutcome.NoPendingUpdate, (await restored.ActivatePendingAsync()).Outcome);
        }

        internal static async Task CorruptCurrentRestoresBackup()
        {
            var storage = new MemoryStorage();
            using (var first = Client(storage))
            {
                await first.InitializeAsync();
                await Activate(first);
                await Activate(first, Changed("second", x => x["values"]["energy"]["maximum"] = 150));
            }
            storage.CorruptCurrent(Options().CacheKey, ConfigSlot.Active);
            using var restored = Client(storage);
            await restored.InitializeAsync();
            Equal(100, restored.Get<Energy>("energy").Maximum);
        }

        internal static async Task ContextIsolation()
        {
            var storage = new MemoryStorage();
            using (var first = Client(storage))
            {
                await first.InitializeAsync();
                await Activate(first);
            }
            using var qa = Client(storage, Options("qa"));
            using var newer = Client(storage, Options(build: 121));
            await qa.InitializeAsync();
            await newer.InitializeAsync();
            Equal(80, qa.Get<Energy>("energy").Maximum);
            Equal(80, newer.Get<Energy>("energy").Maximum);
        }

        internal static async Task SubscriberFailureIsIsolated()
        {
            using var client = Client();
            await client.InitializeAsync();
            var delivered = false;
            client.ConfigActivated += _ => throw new InvalidOperationException("Bad subscriber.");
            client.ConfigActivated += _ => delivered = true;
            client.ErrorOccurred += _ => throw new Exception("Bad diagnostic subscriber.");
            await Activate(client);
            True(delivered, "One throwing subscriber blocked another.");
            Equal(100, client.Get<Energy>("energy").Maximum);
        }

        internal static async Task CancellationAndPayloadBound()
        {
            using var client = Client();
            await client.InitializeAsync();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            Equal(ConfigOutcome.Cancelled, (await client.StageJsonAsync(Fixture(), cancelled.Token)).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            using var small = Client(options: new ConfigOptions("example-game", "production", "android", 120, 1, 128));
            await small.InitializeAsync();
            Equal(ConfigOutcome.Failed, (await small.StageJsonAsync(Fixture())).Outcome);
        }

        internal static async Task ConcurrentStagesRemainConsistent()
        {
            using var client = Client();
            await client.InitializeAsync();
            var results = await Task.WhenAll(
                client.StageJsonAsync(Changed("a", x => x["values"]["energy"]["maximum"] = 101)),
                client.StageJsonAsync(Changed("b", x => x["values"]["energy"]["maximum"] = 102)));
            foreach (var result in results) Equal(ConfigOutcome.UpdateAvailable, result.Outcome);
            await client.ActivatePendingAsync();
            var snapshot = client.GetSnapshot();
            Equal(snapshot.Revision == "a" ? 101 : 102, snapshot.Get<Energy>("energy").Maximum);
        }
    }
}
