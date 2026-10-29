using System;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK.Samples;
using static BijiSDK.OfflineChecks.TestSupport;

namespace BijiSDK.OfflineChecks
{
    internal static class PagesChecks
    {
        private static readonly Uri BaseUri = new Uri("https://viju-tag.github.io/bijiSDK/");

        internal static async Task<int> RunAllAsync(Action<string> report)
        {
            var cases = new (string Name, Func<Task> Run)[]
            {
                ("Pages maps platforms and forwards request/response metadata", Mapping),
                ("Pages rejects another endpoint/context and insecure base", Isolation),
                ("Pages preserves cancellation", Cancellation),
                ("Pages keeps core validation and explicit activation", CoreBehavior)
            };
            int failures = 0;
            foreach (var test in cases)
            {
                try { await HttpTestSupport.Deadline(test.Run()); report("PASS: " + test.Name); }
                catch (Exception error) { failures++; report("FAIL: " + test.Name + Environment.NewLine + error); }
            }
            report("Pages checks: " + (cases.Length - failures) + "/" + cases.Length + " passed.");
            return failures;
        }

        private static ConfigHttpRequest Request(string platform = "android")
            => new ConfigHttpRequest(new Uri(BaseUri,
                "v1/projects/example-game/environments/production/config?platform=" + platform +
                "&appBuild=120&configContractVersion=1"), "\"old\"", TimeSpan.FromSeconds(3), 12345);

        private static async Task Mapping()
        {
            foreach (string platform in new[] { "editor", "android", "ios" })
            {
                var context = new ConfigOptions("example-game", "production", platform, 120, 1);
                var inner = new ScriptedTransport();
                var response = new ConfigHttpResponse(304, "", "\"new\"", "120");
                using (var source = new CancellationTokenSource())
                {
                    inner.Add((request, token) =>
                    {
                        Equal(source.Token, token);
                        Equal("\"old\"", request.IfNoneMatch);
                        Equal(TimeSpan.FromSeconds(3), request.Timeout);
                        Equal(12345, request.MaxResponseBytes);
                        Equal("https://viju-tag.github.io/bijiSDK/configs/example-game/production/" +
                            platform + "/contract-1/config.json", request.Url.AbsoluteUri);
                        return Task.FromResult(response);
                    });
                    var transport = new PagesConfigTransport(inner, BaseUri, context);
                    True(ReferenceEquals(response, await transport.GetAsync(Request(platform), source.Token)),
                        "Response status, body, validator and throttle headers must remain unchanged.");
                }
            }
        }

        private static async Task Isolation()
        {
            var inner = new ScriptedTransport();
            var transport = new PagesConfigTransport(inner, BaseUri, Options());
            foreach (string url in new[]
            {
                Request().Url.AbsoluteUri.Replace("viju-tag.github.io", "another.example"),
                Request().Url.AbsoluteUri.Replace("platform=android", "platform=ios"),
                Request().Url.AbsoluteUri.Replace("appBuild=120", "appBuild=121"),
                Request().Url.AbsoluteUri.Replace("/production/", "/qa/"),
                Request().Url.AbsoluteUri + "#fragment"
            })
            {
                try
                {
                    await transport.GetAsync(new ConfigHttpRequest(new Uri(url), null,
                        TimeSpan.FromSeconds(3), 1024), CancellationToken.None);
                    throw new Exception("Different context was accepted.");
                }
                catch (ConfigTransportException error) { True(!error.IsRetryable, "Context mismatch must not retry."); }
            }
            Equal(0, inner.Count);
            try
            {
                new PagesConfigTransport(inner, new Uri("http://viju-tag.github.io/bijiSDK/"), Options());
                throw new Exception("Plain HTTP Pages base was accepted.");
            }
            catch (ArgumentException) { }
        }

        private static async Task Cancellation()
        {
            var inner = new ScriptedTransport();
            var transport = new PagesConfigTransport(inner, BaseUri, Options());
            using (var source = new CancellationTokenSource())
            {
                source.Cancel();
                try
                {
                    await transport.GetAsync(Request(), source.Token);
                    throw new Exception("Cancelled Pages request reached transport.");
                }
                catch (OperationCanceledException) { }
            }
            Equal(0, inner.Count);
        }

        private static async Task CoreBehavior()
        {
            var inner = new ScriptedTransport();
            inner.Add(200, Fixture(), "\"valid\"");
            inner.Add(200, Fixture("wrong-context-config.json"));
            var transport = new PagesConfigTransport(inner, BaseUri, Options());
            using (var client = new ConfigClient(Options(), Definitions(), new MemoryStorage(),
                new ImmediateDispatcher(), transport, new ConfigRemoteOptions(BaseUri), new FakeConfigClock()))
            {
                await client.InitializeAsync();
                Equal(ConfigOutcome.UpdateAvailable, (await client.RefreshAsync()).Outcome);
                Equal(80, client.Get<Energy>("energy").Maximum);
                Equal(ConfigOutcome.Activated, (await client.ActivatePendingAsync()).Outcome);
                string active = client.GetStatus().ActiveRevision;
                Equal(ConfigOutcome.Failed, (await client.RefreshAsync(force: true)).Outcome);
                Equal(active, client.GetStatus().ActiveRevision);
                Equal(2, inner.Count);
            }
        }
    }
}
