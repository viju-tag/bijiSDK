using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK.Unity;
using Newtonsoft.Json.Linq;
using static BijiSDK.OfflineChecks.TestSupport;

namespace BijiSDK.OfflineChecks
{
    internal static class UnityHttpIntegration
    {
        internal static async Task RunAsync()
        {
            string url = Environment.GetEnvironmentVariable("BIJI_HTTP_TEST_URL");
            if (string.IsNullOrEmpty(url)) return;
            string dataRoot = Environment.GetEnvironmentVariable("BIJI_HTTP_TEST_DATA");
            string python = Environment.GetEnvironmentVariable("BIJI_HTTP_TEST_PYTHON");
            var options = new ConfigOptions("bijisdk-offline-sample", "development", "editor", 120, 1);
            var storage = new RecoverableFileConfigStorage(Path.Combine(dataRoot, "client-cache"));
            var dispatcher = new UnityMainThreadDispatcher();
            var observed = new ObservedTransport(new UnityWebRequestConfigTransport(dispatcher, true));
            var remote = new ConfigRemoteOptions(new Uri(url), allowInsecureHttp: true);
            string originalRevision;
            using (var client = new ConfigClient(options, Definitions(), storage, dispatcher, observed, remote))
            {
                await client.InitializeAsync();
                Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
                Equal(200, observed.LastStatus);
                Equal(80, client.Get<Energy>("energy").Maximum);
                originalRevision = client.GetStatus().PendingRevision;
                await client.ActivatePendingAsync();
                Equal(100, client.Get<Energy>("energy").Maximum);
                Equal(ConfigOutcome.Unchanged, (await client.RefreshAsync(force: true)).Outcome);
                Equal(304, observed.LastStatus);
                True(!string.IsNullOrEmpty(observed.LastValidator), "Conditional request omitted ETag.");

                var updated = JObject.Parse(File.ReadAllText(Path.Combine(TestSupport.Root,
                    "clients/unity/Samples~/OfflineConfig/offline-config.json")));
                updated["values"]["energy"]["maximum"] = 175;
                string input = Path.Combine(dataRoot, "updated-input.json");
                File.WriteAllText(input, updated.ToString());
                await PublishAsync(python, dataRoot, "publish", "--file", input,
                    "--reason", "HTTP integration update", "--validate-example-features");
                Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync(force: true)).Outcome);
                Equal(100, client.Get<Energy>("energy").Maximum);
                await client.ActivatePendingAsync();
                Equal(175, client.Get<Energy>("energy").Maximum);

                await PublishAsync(python, dataRoot, "rollback", "--project", options.ProjectId,
                    "--environment", options.Environment, "--platform", options.Platform,
                    "--revision", originalRevision, "--reason", "HTTP integration rollback");
                Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync(force: true)).Outcome);
                True(client.GetStatus().PendingRevision != originalRevision, "Rollback reused old publication ID.");
                Equal(175, client.Get<Energy>("energy").Maximum);
                await client.ActivatePendingAsync();
                Equal(100, client.Get<Energy>("energy").Maximum);
            }

            // A client constructed without transport proves startup/reads need no network.
            using (var offline = new ConfigClient(options, Definitions(), storage, dispatcher))
            {
                Equal(ConfigOutcome.ReadyWithSavedConfig, (await offline.InitializeAsync()).Outcome);
                Equal(100, offline.Get<Energy>("energy").Maximum);
            }
        }

        internal static async Task CheckFaultTransportAsync()
        {
            string url = Environment.GetEnvironmentVariable("BIJI_HTTP_FAULT_URL");
            if (string.IsNullOrEmpty(url)) return;
            var transport = new UnityWebRequestConfigTransport(new UnityMainThreadDispatcher(), true);
            foreach (string route in new[] { "oversized", "invalid-utf8" })
            {
                try
                {
                    await transport.GetAsync(new ConfigHttpRequest(new Uri(url + route), null,
                        TimeSpan.FromSeconds(2), 128), CancellationToken.None);
                    throw new Exception("Expected permanent transport failure: " + route);
                }
                catch (ConfigTransportException error)
                {
                    True(!error.IsRetryable, route + " must be a permanent failure.");
                }
            }
            try
            {
                await transport.GetAsync(new ConfigHttpRequest(new Uri(url + "slow"), null,
                    TimeSpan.FromMilliseconds(100), 1024), CancellationToken.None);
                throw new Exception("Expected HTTP transport timeout.");
            }
            catch (ConfigTransportException error) { True(error.IsTimeout, "Timeout classification lost."); }

            using (var cancel = new CancellationTokenSource(100))
            {
                try
                {
                    await transport.GetAsync(new ConfigHttpRequest(new Uri(url + "slow"), null,
                        TimeSpan.FromSeconds(2), 1024), cancel.Token);
                    throw new Exception("Expected HTTP request cancellation.");
                }
                catch (OperationCanceledException) { }
            }
            var redirect = await transport.GetAsync(new ConfigHttpRequest(new Uri(url + "redirect"), null,
                TimeSpan.FromSeconds(2), 1024), CancellationToken.None);
            Equal(302, redirect.StatusCode);
            var throttled = await transport.GetAsync(new ConfigHttpRequest(new Uri(url + "throttled"), null,
                TimeSpan.FromSeconds(2), 1024), CancellationToken.None);
            Equal(429, throttled.StatusCode);
            Equal("120", throttled.RetryAfter);
        }

        private static Task PublishAsync(string python, string dataRoot, params string[] arguments)
        {
            return Task.Run(() =>
            {
                string command = Quote(Path.Combine(TestSupport.Root, "backend/publishing/publish.py"))
                    + " --data-dir " + Quote(dataRoot);
                foreach (string argument in arguments) command += " " + Quote(argument);
                var start = new ProcessStartInfo(python, command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException("Could not start local publisher.");
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000))
                    {
                        process.Kill();
                        throw new TimeoutException("Local publisher did not exit.");
                    }
                    if (process.ExitCode != 0) throw new Exception("Local publisher failed: " + errorTask.GetAwaiter().GetResult() + outputTask.GetAwaiter().GetResult());
                }
            });
        }

        private static string Quote(string value)
        {
            if (value == null || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("Unexpected process argument.");
            return "\"" + value + "\"";
        }

        private sealed class ObservedTransport : IConfigTransport
        {
            private readonly IConfigTransport inner;
            internal int LastStatus { get; private set; }
            internal string LastValidator { get; private set; }
            internal ObservedTransport(IConfigTransport inner) { this.inner = inner; }
            public async Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken token)
            {
                LastValidator = request.IfNoneMatch;
                var response = await inner.GetAsync(request, token);
                LastStatus = response.StatusCode;
                return response;
            }
        }
    }
}
