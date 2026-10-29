using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK.Samples;
using BijiSDK.Unity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BijiSDK.MobileVerification
{
    /// <summary>Runs the same real-transport and real-storage checks in Editor and IL2CPP players.</summary>
    public static class MobileConfigChecks
    {
        public const string PagesUrl = "https://viju-tag.github.io/bijiSDK/";

        public static async Task<int> RunAsync(string platform, string storageRoot)
        {
            Directory.CreateDirectory(storageRoot);
            var results = new JArray();
            int failures = 0;
            var context = new ConfigOptions("bijisdk-offline-sample", "development", platform, 120, 1);
            var storage = new RecoverableFileConfigStorage(Path.Combine(storageRoot, "fresh-" + Guid.NewGuid().ToString("N")));
            var dispatcher = new UnityMainThreadDispatcher();
            var observed = new ObservedTransport(new UnityWebRequestConfigTransport(dispatcher));
            var remote = new ConfigRemoteOptions(new Uri(PagesUrl));
            var pages = new PagesConfigTransport(observed, remote.BaseUri, context);
            ConfigClient client = null;
            string hostedRevision = null;
            string hostedBody = null;
            int activationEvents = 0;
            int mainThread = Thread.CurrentThread.ManagedThreadId;
            var activationThread = new TaskCompletionSource<int>();
            try
            {
                client = CreateClient(context, storage, dispatcher, pages, remote);
                var initialized = await client.InitializeAsync();
                Require(initialized.Outcome == ConfigOutcome.ReadyWithDefaults, "Expected a fresh defaults result.");
                Require(observed.RequestCount == 0, "Initialization contacted the network.");
                RequireEnergy(client, 80, 180);
                Record("defaults_before_network", client, initialized);

                var refresh = await client.RefreshAsync();
                Require(refresh.Outcome == ConfigOutcome.UpdateAvailable, Describe(refresh));
                Require(observed.LastStatus == 200, "Initial Pages response was not HTTP 200.");
                RequireEnergy(client, 80, 180);
                hostedRevision = client.GetStatus().PendingRevision;
                hostedBody = observed.LastBody;
                Require(!string.IsNullOrEmpty(hostedRevision) && client.GetStatus().ActiveRevision == null,
                    "Refresh must create pending data without activating it.");
                var hosted = JObject.Parse(hostedBody);
                Require((int)hosted["values"]["energy"]["maximum"] == 100 &&
                    (int)hosted["values"]["energy"]["refillSeconds"] == 120,
                    "Hosted fixture changed; inspect it before updating test expectations.");
                File.WriteAllText(Path.Combine(storageRoot, "hosted-" + platform + ".json"), hostedBody);
                Record("pages_pending_before_activation", client, refresh);

                int requestCount = observed.RequestCount;
                var fresh = await client.RefreshAsync();
                Require(fresh.Outcome == ConfigOutcome.FreshCache && observed.RequestCount == requestCount,
                    "Fresh cache should avoid another request.");
                var repeatedPending = await client.RefreshAsync(force: true);
                Require(repeatedPending.Outcome == ConfigOutcome.Unchanged, Describe(repeatedPending));
                Require(client.GetStatus().PendingRevision == hostedRevision, "Repeated refresh lost pending data.");
                RequireEnergy(client, 80, 180);
                Record("repeated_pending_refresh", client, repeatedPending);

                client.ConfigActivated += _ =>
                {
                    activationEvents++;
                    activationThread.TrySetResult(Thread.CurrentThread.ManagedThreadId);
                };
                var activated = await client.ActivatePendingAsync();
                Require(activated.Outcome == ConfigOutcome.Activated, Describe(activated));
                RequireEnergy(client, 100, 120);
                Require(client.GetSnapshot().GetSource("energy") == ConfigValueSource.Remote &&
                    client.GetStatus().PendingRevision == null, "Activation state/source is inconsistent.");
                Require(await Task.WhenAny(activationThread.Task, Task.Delay(5000)) == activationThread.Task,
                    "Activation callback did not arrive.");
                Require(await activationThread.Task == mainThread, "Activation callback left the Unity main thread.");
                Record("explicit_activation_and_main_thread_callback", client, activated);

                var repeatedActive = await client.RefreshAsync(force: true);
                Require(repeatedActive.Outcome == ConfigOutcome.Unchanged, Describe(repeatedActive));
                Require(activationEvents == 1, "Refresh emitted a duplicate activation event.");
                RequireEnergy(client, 100, 120);
                Record("repeated_active_refresh", client, repeatedActive);

                client.Dispose();
                client = CreateClient(context, storage, dispatcher, pages, remote);
                requestCount = observed.RequestCount;
                var restored = await client.InitializeAsync();
                Require(restored.Outcome == ConfigOutcome.ReadyWithSavedConfig && observed.RequestCount == requestCount,
                    "Saved startup must work before any request.");
                RequireEnergy(client, 100, 120);
                Record("client_recreation_from_disk", client, restored);
                var revalidated = await client.RefreshAsync(force: true);
                Require(revalidated.Outcome == ConfigOutcome.Unchanged, Describe(revalidated));
                Require(!string.IsNullOrEmpty(observed.LastValidator), "Persisted ETag was not sent after recreation.");
                RequireEnergy(client, 100, 120);
                Record("persisted_etag_revalidation", client, revalidated);

                var wrongContext = JObject.Parse(hostedBody);
                wrongContext["platform"] = platform == "ios" ? "android" : "ios";
                var rejectedContext = await client.StageJsonAsync(wrongContext.ToString(Formatting.None));
                RequireRejected(rejectedContext, client, hostedRevision);
                Record("wrong_context_rejected", client, rejectedContext);

                var invalid = JObject.Parse(hostedBody);
                invalid["revision"] = "invalid-" + Guid.NewGuid().ToString("N");
                invalid["values"]["energy"]["maximum"] = -1;
                var rejectedValue = await client.StageJsonAsync(invalid.ToString(Formatting.None));
                RequireRejected(rejectedValue, client, hostedRevision);
                Record("invalid_values_rejected", client, rejectedValue);

                var rejectedJson = await client.StageJsonAsync("{invalid-json");
                RequireRejected(rejectedJson, client, hostedRevision);
                Record("malformed_json_rejected", client, rejectedJson);

                // Local candidates exercise durable replacement without changing hosted publications.
                var update = JObject.Parse(hostedBody);
                update["revision"] = "local-update-" + Guid.NewGuid().ToString("N");
                update["values"]["energy"]["maximum"] = 125;
                var stagedUpdate = await client.StageJsonAsync(update.ToString(Formatting.None));
                Require(stagedUpdate.Outcome == ConfigOutcome.UpdateAvailable, Describe(stagedUpdate));
                RequireEnergy(client, 100, 120);
                Record("local_update_stays_pending", client, stagedUpdate);
                var localActivation = await client.ActivatePendingAsync();
                Require(localActivation.Outcome == ConfigOutcome.Activated, Describe(localActivation));
                RequireEnergy(client, 125, 120);
                Record("local_update_file_replacement", client, localActivation);

                var rollback = JObject.Parse(hostedBody);
                rollback["revision"] = "local-rollback-" + Guid.NewGuid().ToString("N");
                var stagedRollback = await client.StageJsonAsync(rollback.ToString(Formatting.None));
                Require(stagedRollback.Outcome == ConfigOutcome.UpdateAvailable, Describe(stagedRollback));
                RequireEnergy(client, 125, 120);
                Record("local_rollback_stays_pending", client, stagedRollback);
                var rollbackActivation = await client.ActivatePendingAsync();
                Require(rollbackActivation.Outcome == ConfigOutcome.Activated, Describe(rollbackActivation));
                RequireEnergy(client, 100, 120);
                Record("local_rollback_activation", client, rollbackActivation);

                client.Dispose();
                client = CreateClient(context, storage, dispatcher);
                var restoredRollback = await client.InitializeAsync();
                Require(restoredRollback.Outcome == ConfigOutcome.ReadyWithSavedConfig, Describe(restoredRollback));
                RequireEnergy(client, 100, 120);
                Record("replacement_survives_recreation_without_transport", client, restoredRollback);
            }
            catch (Exception exception)
            {
                failures++;
                Debug.LogException(exception);
                results.Add(new JObject { ["test"] = "suite_failure", ["status"] = "failed", ["error"] = exception.ToString() });
            }
            finally
            {
                client?.Dispose();
                var summary = new JObject
                {
                    ["utc"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["platform"] = platform,
                    ["unity"] = Application.unityVersion,
                    ["deviceModel"] = SystemInfo.deviceModel,
                    ["operatingSystem"] = SystemInfo.operatingSystem,
                    ["processor"] = SystemInfo.processorType,
                    ["storageRoot"] = storageRoot,
                    ["failures"] = failures,
                    ["results"] = results
                };
                File.WriteAllText(Path.Combine(storageRoot, "results.json"), summary.ToString(Formatting.Indented));
                Debug.Log("BIJISDK_MOBILE_CHECKS_COMPLETE platform=" + platform + " passed=" +
                    (results.Count - failures) + " failures=" + failures + " results=" + storageRoot);
            }
            return failures;

            void Record(string test, ConfigClient current, ConfigResult result)
            {
                JObject record = DescribeState(current);
                record["test"] = test;
                record["status"] = "passed";
                record["outcome"] = result.Outcome.ToString();
                record["error"] = result.Error?.ToString();
                record["httpStatus"] = observed.LastStatus;
                results.Add(record);
                Debug.Log("BIJISDK_MOBILE_CHECK " + record.ToString(Formatting.None));
            }
        }

        public static ConfigClient CreateClient(ConfigOptions context, IConfigStorage storage,
            IMainThreadDispatcher dispatcher, IConfigTransport transport = null, ConfigRemoteOptions remote = null)
        {
            var definition = new ConfigDefinition<Energy>("energy", new Energy(80, 180),
                value => new JObject { ["maximum"] = value.Maximum, ["refillSeconds"] = value.RefillSeconds },
                value => new Energy(ReadInteger(value, "maximum"), ReadInteger(value, "refillSeconds")),
                value => value.Maximum > 0 && value.RefillSeconds > 0 ? null : "Energy values must be positive.");
            return new ConfigClient(context, new IConfigDefinition[] { definition }, storage, dispatcher, transport, remote);
        }

        public static JObject DescribeState(ConfigClient client)
        {
            var status = client.GetStatus();
            var energy = client.Get<Energy>("energy");
            return new JObject
            {
                ["active"] = status.ActiveRevision,
                ["pending"] = status.PendingRevision,
                ["source"] = client.GetSnapshot().GetSource("energy").ToString(),
                ["maximum"] = energy.Maximum,
                ["refillSeconds"] = energy.RefillSeconds
            };
        }

        private static int ReadInteger(JToken value, string key)
        {
            JToken number = value is JObject data ? data[key] : null;
            if (number == null || number.Type != JTokenType.Integer)
                throw new FormatException("Energy field must be an integer: " + key);
            return checked((int)number.Value<long>());
        }

        private static void RequireRejected(ConfigResult result, ConfigClient client, string revision)
        {
            Require(result.Outcome == ConfigOutcome.Failed, "Invalid candidate was not rejected.");
            RequireEnergy(client, 100, 120);
            Require(client.GetStatus().ActiveRevision == revision && client.GetStatus().PendingRevision == null,
                "Rejected candidate changed active/pending state.");
        }

        private static void RequireEnergy(ConfigClient client, int maximum, int refillSeconds)
        {
            Energy value = client.Get<Energy>("energy");
            Require(value.Maximum == maximum && value.RefillSeconds == refillSeconds,
                "Expected energy " + maximum + "/" + refillSeconds + ", got " + value.Maximum + "/" + value.RefillSeconds);
        }

        private static string Describe(ConfigResult result) => result.Outcome + ": " + result.Error;

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public sealed class Energy
        {
            public int Maximum { get; }
            public int RefillSeconds { get; }
            public Energy(int maximum, int refillSeconds)
            {
                Maximum = maximum;
                RefillSeconds = refillSeconds;
            }
        }

        private sealed class ObservedTransport : IConfigTransport
        {
            private readonly IConfigTransport inner;
            public int RequestCount { get; private set; }
            public int LastStatus { get; private set; }
            public string LastBody { get; private set; }
            public string LastValidator { get; private set; }

            public ObservedTransport(IConfigTransport inner) { this.inner = inner; }

            public async Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken token)
            {
                RequestCount++;
                LastValidator = request.IfNoneMatch;
                ConfigHttpResponse response = await inner.GetAsync(request, token);
                LastStatus = response.StatusCode;
                if (response.StatusCode == 200) LastBody = response.Body;
                return response;
            }
        }
    }
}
