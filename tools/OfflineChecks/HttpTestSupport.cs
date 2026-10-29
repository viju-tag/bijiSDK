using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BijiSDK;
using static BijiSDK.OfflineChecks.TestSupport;

namespace BijiSDK.OfflineChecks
{
    internal sealed class FakeConfigClock : IConfigClock
    {
        private readonly object gate = new object();
        private DateTimeOffset utc = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        private TimeSpan elapsed;
        internal readonly List<TimeSpan> Delays = new List<TimeSpan>();
        public DateTimeOffset UtcNow { get { lock (gate) return utc; } }
        public TimeSpan Elapsed { get { lock (gate) return elapsed; } }

        internal void Advance(TimeSpan duration)
        {
            lock (gate) { utc += duration; elapsed += duration; }
        }

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate) Delays.Add(delay);
            Advance(delay);
            return Task.CompletedTask;
        }
    }

    internal sealed class ScriptedTransport : IConfigTransport
    {
        private readonly object gate = new object();
        private readonly Queue<Func<ConfigHttpRequest, CancellationToken, Task<ConfigHttpResponse>>> steps
            = new Queue<Func<ConfigHttpRequest, CancellationToken, Task<ConfigHttpResponse>>>();
        private readonly List<ConfigHttpRequest> requests = new List<ConfigHttpRequest>();
        internal int Count { get { lock (gate) return requests.Count; } }
        internal ConfigHttpRequest Last { get { lock (gate) return requests[requests.Count - 1]; } }

        internal void Add(int status, string body = "", string etag = null, string retryAfter = null)
            => Add((_, __) => Task.FromResult(new ConfigHttpResponse(status, body, etag, retryAfter)));

        internal void Add(Func<ConfigHttpRequest, CancellationToken, Task<ConfigHttpResponse>> step)
        {
            lock (gate) steps.Enqueue(step);
        }

        public Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken token)
        {
            Func<ConfigHttpRequest, CancellationToken, Task<ConfigHttpResponse>> step;
            lock (gate)
            {
                requests.Add(request);
                if (steps.Count == 0) throw new Exception("Unexpected HTTP request: " + request.Url);
                step = steps.Dequeue();
            }
            return step(request, token);
        }
    }

    internal static class HttpTestSupport
    {
        internal static ConfigRemoteOptions Remote(string url = "https://config.example.test/")
            => new ConfigRemoteOptions(new Uri(url));

        internal static ConfigClient RemoteClient(ScriptedTransport transport,
            MemoryStorage storage = null, FakeConfigClock clock = null, ConfigRemoteOptions remote = null)
            => new ConfigClient(Options(), Definitions(), storage ?? new MemoryStorage(),
                new ImmediateDispatcher(), transport, remote ?? Remote(), clock ?? new FakeConfigClock());

        internal static async Task Deadline(Task task)
        {
            if (await Task.WhenAny(task, Task.Delay(5000)) != task)
                throw new TimeoutException("Test operation did not complete within five seconds.");
            await task;
        }

        internal static async Task<T> Deadline<T>(Task<T> task)
        {
            if (await Task.WhenAny(task, Task.Delay(5000)) != task)
                throw new TimeoutException("Test operation did not complete within five seconds.");
            return await task;
        }
    }
}
