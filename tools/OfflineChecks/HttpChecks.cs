using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK;
using static BijiSDK.OfflineChecks.TestSupport;
using static BijiSDK.OfflineChecks.HttpTestSupport;

namespace BijiSDK.OfflineChecks
{
    internal static class HttpChecks
    {
        internal static async Task<int> RunAllAsync(Action<string> report)
        {
            var cases = new (string Name, Func<Task> Run)[]
            {
                ("HTTP stages without activation and builds context URI", FetchStages),
                ("Freshness and explicit force refresh", Freshness),
                ("ETag and 304 preserve pending activation", ConditionalResponse),
                ("Persisted ETag/freshness survive restart", PersistedFetch),
                ("ETag is isolated by backend endpoint", EndpointIsolation),
                ("Transient retries and failure cooldown", RetriesAndCooldown),
                ("Permanent/invalid responses are not retried", PermanentFailures),
                ("429 Retry-After cannot be bypassed by force", Throttling),
                ("Concurrent refreshes share one request", SingleFlight),
                ("Joining caller cancellation preserves shared request", JoinerCancellation),
                ("Newer local staging supersedes in-flight HTTP", StaleResponse),
                ("304 without representation recovers unconditionally", MissingRepresentation),
                ("Cancelled/disposed request cannot apply late data", Cancellation),
                ("Request timeout remains bounded", Timeout),
                ("Failed HTTP staging persistence preserves active", PersistenceFailure),
                ("HTTP options reject insecure defaults", OptionsValidation)
            };
            int failures = 0;
            foreach (var test in cases)
            {
                try { await Deadline(test.Run()); report("PASS: " + test.Name); }
                catch (Exception exception) { failures++; report("FAIL: " + test.Name + Environment.NewLine + exception); }
            }
            report("HTTP core checks: " + (cases.Length - failures) + "/" + cases.Length + " passed.");
            return failures;
        }

        private static async Task FetchStages()
        {
            var transport = new ScriptedTransport();
            transport.Add(200, Fixture(), "\"one\"");
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal("release-000042", client.GetStatus().PendingRevision);
            Equal("https://config.example.test/v1/projects/example-game/environments/production/config?platform=android&appBuild=120&configContractVersion=1",
                transport.Last.Url.AbsoluteUri);
            Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
            Equal(100, client.Get<Energy>("energy").Maximum);
        }

        private static async Task Freshness()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeConfigClock();
            transport.Add(200, Fixture(), "\"one\"");
            transport.Add(304);
            transport.Add(304);
            using var client = RemoteClient(transport, clock: clock);
            await client.InitializeAsync();
            await client.RefreshAsync();
            Equal("FreshCache", (await client.RefreshAsync()).Outcome.ToString());
            Equal(1, transport.Count);
            Equal(ConfigOutcome.Unchanged, (await client.RefreshAsync(force: true)).Outcome);
            clock.Advance(TimeSpan.FromMinutes(16));
            Equal(ConfigOutcome.Unchanged, (await client.RefreshAsync()).Outcome);
            Equal(3, transport.Count);
        }

        private static async Task ConditionalResponse()
        {
            var transport = new ScriptedTransport();
            transport.Add(200, Fixture(), "\"one\"");
            transport.Add(304);
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            int updates = 0;
            client.UpdateAvailable += _ => updates++;
            await client.RefreshAsync();
            Equal(ConfigOutcome.Unchanged, (await client.RefreshAsync(force: true)).Outcome);
            Equal("\"one\"", transport.Last.IfNoneMatch);
            Equal(1, updates);
            Equal(80, client.Get<Energy>("energy").Maximum);
        }

        private static async Task PersistedFetch()
        {
            var storage = new MemoryStorage();
            var firstTransport = new ScriptedTransport();
            firstTransport.Add(200, Fixture(), "\"one\"");
            using (var first = RemoteClient(firstTransport, storage))
            {
                await first.InitializeAsync();
                await first.RefreshAsync();
            }
            var nextTransport = new ScriptedTransport();
            nextTransport.Add(304);
            using var restored = RemoteClient(nextTransport, storage);
            await restored.InitializeAsync();
            Equal("FreshCache", (await restored.RefreshAsync()).Outcome.ToString());
            Equal(0, nextTransport.Count);
            Equal(ConfigOutcome.Unchanged, (await restored.RefreshAsync(force: true)).Outcome);
            Equal("\"one\"", nextTransport.Last.IfNoneMatch);
            Equal(80, restored.Get<Energy>("energy").Maximum);
        }

        private static async Task EndpointIsolation()
        {
            var storage = new MemoryStorage();
            var initial = new ScriptedTransport();
            initial.Add(200, Fixture(), "\"one\"");
            using (var first = RemoteClient(initial, storage))
            {
                await first.InitializeAsync();
                await first.RefreshAsync();
                await first.ActivatePendingAsync();
            }
            var other = new ScriptedTransport();
            other.Add(200, Fixture(), "\"two\"");
            using var client = RemoteClient(other, storage, remote: Remote("https://other.example.test/"));
            await client.InitializeAsync();
            await client.RefreshAsync();
            Equal(null, other.Last.IfNoneMatch);
            Equal(1, other.Count);
        }

        private static async Task RetriesAndCooldown()
        {
            var transport = new ScriptedTransport();
            var clock = new FakeConfigClock();
            transport.Add(500); transport.Add(503); transport.Add(200, Fixture());
            using var client = RemoteClient(transport, clock: clock);
            await client.InitializeAsync();
            Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
            Equal(3, transport.Count);
            Equal(2, clock.Delays.Count);
            True(clock.Delays[0].TotalSeconds >= 1.6 && clock.Delays[0].TotalSeconds <= 2.4, "First backoff outside jitter bounds.");
            True(clock.Delays[1].TotalSeconds >= 4 && clock.Delays[1].TotalSeconds <= 6, "Second backoff outside jitter bounds.");

            var failing = new ScriptedTransport();
            failing.Add(500); failing.Add(500); failing.Add(500);
            using var failed = RemoteClient(failing);
            await failed.InitializeAsync();
            Equal(ConfigOutcome.Failed, (await failed.RefreshAsync()).Outcome);
            Equal(3, failing.Count);
            await failed.RefreshAsync(force: true);
            Equal(3, failing.Count, "Force must not bypass failure cooldown.");
        }

        private static async Task PermanentFailures()
        {
            foreach (var reply in new[] { new ConfigHttpResponse(404, ""), new ConfigHttpResponse(200, Fixture("invalid-feature-config.json")) })
            {
                var transport = new ScriptedTransport();
                transport.Add((_, __) => Task.FromResult(reply));
                using var client = RemoteClient(transport);
                await client.InitializeAsync();
                await client.StageJsonAsync(Fixture());
                Equal(ConfigOutcome.Failed, (await client.RefreshAsync(force: true)).Outcome);
                Equal(1, transport.Count);
                Equal("release-000042", client.GetStatus().PendingRevision);
                Equal(80, client.Get<Energy>("energy").Maximum);
            }
        }

        private static async Task Throttling()
        {
            var clock = new FakeConfigClock();
            var transport = new ScriptedTransport();
            transport.Add(429, retryAfter: " 120 ");
            transport.Add(200, Fixture());
            using var client = RemoteClient(transport, clock: clock);
            await client.InitializeAsync();
            Equal("Throttled", (await client.RefreshAsync()).Outcome.ToString());
            Equal("Throttled", (await client.RefreshAsync(force: true)).Outcome.ToString());
            Equal(1, transport.Count);
            clock.Advance(TimeSpan.FromSeconds(121));
            Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
        }

        private static async Task SingleFlight()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<ConfigHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ScriptedTransport();
            transport.Add((_, __) => { started.TrySetResult(true); return response.Task; });
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            var first = client.RefreshAsync();
            await started.Task;
            var second = client.RefreshAsync(force: true);
            response.SetResult(new ConfigHttpResponse(200, Fixture()));
            Equal(ConfigOutcome.UpdateAvailable, (await first).Outcome);
            Equal(ConfigOutcome.UpdateAvailable, (await second).Outcome);
            Equal(1, transport.Count);
        }

        private static async Task JoinerCancellation()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<ConfigHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ScriptedTransport();
            transport.Add((_, __) => { started.TrySetResult(true); return response.Task; });
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            var first = client.RefreshAsync();
            await started.Task;
            using var joiner = new CancellationTokenSource();
            var second = client.RefreshAsync(joiner.Token);
            joiner.Cancel();
            Equal(ConfigOutcome.Cancelled, (await second).Outcome);
            response.SetResult(new ConfigHttpResponse(200, Fixture()));
            Equal(ConfigOutcome.UpdateAvailable, (await first).Outcome);
            Equal(1, transport.Count);
        }

        private static async Task StaleResponse()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<ConfigHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ScriptedTransport();
            transport.Add((_, __) => { started.TrySetResult(true); return response.Task; });
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            var refresh = client.RefreshAsync();
            await started.Task;
            await client.StageJsonAsync(Changed("local-new", x => x["values"]["energy"]["maximum"] = 155));
            response.SetResult(new ConfigHttpResponse(200, Fixture()));
            Equal("Superseded", (await refresh).Outcome.ToString());
            Equal("local-new", client.GetStatus().PendingRevision);
            await client.ActivatePendingAsync();
            Equal(155, client.Get<Energy>("energy").Maximum);
        }

        private static async Task MissingRepresentation()
        {
            var transport = new ScriptedTransport();
            transport.Add(304); transport.Add(200, Fixture(), "\"one\"");
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
            Equal(2, transport.Count);
            Equal(null, transport.Last.IfNoneMatch);
        }

        private static async Task Cancellation()
        {
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var response = new TaskCompletionSource<ConfigHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transport = new ScriptedTransport();
            transport.Add((_, __) => { started.TrySetResult(true); return response.Task; });
            using var client = RemoteClient(transport);
            await client.InitializeAsync();
            using var owner = new CancellationTokenSource();
            var operation = client.RefreshAsync(owner.Token);
            await started.Task;
            owner.Cancel();
            Equal(ConfigOutcome.Cancelled, (await operation).Outcome);
            response.SetResult(new ConfigHttpResponse(200, Fixture()));
            await Task.Delay(25);
            Equal(null, client.GetStatus().PendingRevision);
        }

        private static async Task Timeout()
        {
            var transport = new ScriptedTransport();
            transport.Add((_, __) => new TaskCompletionSource<ConfigHttpResponse>().Task);
            var remote = new ConfigRemoteOptions(new Uri("https://config.example.test/"),
                requestTimeout: TimeSpan.FromMilliseconds(30), maxAttempts: 1,
                operationTimeout: TimeSpan.FromMilliseconds(200));
            using var client = RemoteClient(transport, remote: remote);
            await client.InitializeAsync();
            var result = await client.RefreshAsync();
            Equal(ConfigOutcome.Failed, result.Outcome);
            Equal("Timeout", result.Error.Category.ToString());
        }

        private static async Task PersistenceFailure()
        {
            var storage = new MemoryStorage();
            var transport = new ScriptedTransport();
            transport.Add(200, Fixture(), "\"one\"");
            using var client = RemoteClient(transport, storage);
            await client.InitializeAsync();
            storage.FailWriteSlot = ConfigSlot.Pending;
            Equal(ConfigOutcome.Failed, (await client.RefreshAsync()).Outcome);
            Equal(80, client.Get<Energy>("energy").Maximum);
            Equal(null, client.GetStatus().PendingRevision);
        }

        private static Task OptionsValidation()
        {
            Throws<ArgumentException>(() => Remote("http://127.0.0.1:8765/"));
            Throws<ArgumentException>(() => Remote("https://user:password@config.example.test/"));
            Throws<ArgumentException>(() => Remote("https://config.example.test/?token=x"));
            var allowed = new ConfigRemoteOptions(new Uri("http://127.0.0.1:8765/"), allowInsecureHttp: true);
            True(allowed.AllowInsecureHttp, "Development HTTP opt-in not retained.");
            return Task.CompletedTask;
        }
    }
}
