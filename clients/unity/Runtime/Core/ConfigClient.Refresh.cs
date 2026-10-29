using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    public sealed partial class ConfigClient
    {
        private sealed class FetchRepresentation
        {
            internal readonly ConfigCandidate Candidate;
            internal readonly string ETag;
            internal readonly DateTimeOffset? SuccessUtc;

            internal FetchRepresentation(ConfigCandidate candidate, string eTag, DateTimeOffset? successUtc)
            {
                Candidate = candidate;
                ETag = eTag;
                SuccessUtc = successUtc;
            }
        }

        private sealed class RefreshTiming
        {
            internal readonly DateTimeOffset? AttemptUtc;
            internal readonly DateTimeOffset? SuccessUtc;
            internal readonly TimeSpan? SuccessElapsed;
            internal readonly DateTimeOffset? BlockedUntilUtc;
            internal readonly TimeSpan? BlockedUntilElapsed;

            internal RefreshTiming(DateTimeOffset? attemptUtc = null, DateTimeOffset? successUtc = null,
                TimeSpan? successElapsed = null, DateTimeOffset? blockedUntilUtc = null,
                TimeSpan? blockedUntilElapsed = null)
            {
                AttemptUtc = attemptUtc;
                SuccessUtc = successUtc;
                SuccessElapsed = successElapsed;
                BlockedUntilUtc = blockedUntilUtc;
                BlockedUntilElapsed = blockedUntilElapsed;
            }
        }

        private readonly object refreshGate = new object();
        private readonly Random retryRandom = new Random();
        private IConfigTransport transport;
        private ConfigRemoteOptions remoteOptions;
        private IConfigClock clock;
        private Uri requestUri;
        private Task<ConfigResult> sharedRefresh;
        private FetchRepresentation fetched;
        private RefreshTiming refreshTiming = new RefreshTiming();
        private long contentGeneration;

        public event Action<ConfigError> RefreshFailed;

        private void ConfigureRemote(IConfigTransport configuredTransport, ConfigRemoteOptions configuredOptions,
            IConfigClock configuredClock)
        {
            if ((configuredTransport == null) != (configuredOptions == null))
                throw new ArgumentException("Supply both a transport and remote options, or neither.");
            transport = configuredTransport;
            remoteOptions = configuredOptions;
            clock = configuredClock ?? new SystemConfigClock();
            if (remoteOptions != null)
            {
                string path = "v1/projects/" + Uri.EscapeDataString(options.ProjectId) +
                    "/environments/" + Uri.EscapeDataString(options.Environment) +
                    "/config?platform=" + Uri.EscapeDataString(options.Platform) +
                    "&appBuild=" + options.AppBuild.ToString(CultureInfo.InvariantCulture) +
                    "&configContractVersion=" + options.ConfigContractVersion.ToString(CultureInfo.InvariantCulture);
                requestUri = new Uri(remoteOptions.BaseUri, path);
                if (requestUri.AbsoluteUri.Length > 4096)
                    throw new ArgumentException("The complete config request URI exceeds 4096 characters.");
            }
        }

        /// <summary>
        /// Fetch without activating. Concurrent callers share the first caller's operation.
        /// The first caller owns cancellation; a later caller can cancel only its own wait.
        /// Force bypasses freshness, but never throttling, backoff, or request budgets.
        /// </summary>
        public Task<ConfigResult> RefreshAsync(CancellationToken cancellationToken = default, bool force = false)
        {
            if (cancellationToken.IsCancellationRequested || Volatile.Read(ref disposed) != 0)
                return Task.FromResult(CancelledResult());
            lock (refreshGate)
            {
                if (sharedRefresh == null || sharedRefresh.IsCompleted)
                {
                    sharedRefresh = RefreshCoreAsync(force, cancellationToken);
                    return sharedRefresh;
                }
                return cancellationToken.CanBeCanceled
                    ? WaitForSharedAsync(sharedRefresh, cancellationToken)
                    : sharedRefresh;
            }
        }

        private async Task<ConfigResult> RefreshCoreAsync(bool force, CancellationToken callerToken)
        {
            // Ensure construction of the shared task completes before any callback can reenter.
            long generation = Interlocked.Read(ref contentGeneration);
            await Task.Yield();
            if (transport == null)
                return RefreshFailure(ConfigErrorCategory.Initialization, "This client has no HTTP transport configured.");
            if (!Volatile.Read(ref state).Ready)
                return RefreshFailure(ConfigErrorCategory.Initialization, "Initialize the client before refreshing.");

            using (var operation = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token))
            {
                operation.CancelAfter(remoteOptions.OperationTimeout);
                TimeSpan started = clock.Elapsed;
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    RefreshTiming timing = Volatile.Read(ref refreshTiming);
                    if (timing.BlockedUntilElapsed.HasValue && clock.Elapsed < timing.BlockedUntilElapsed.Value)
                        return ThrottledResult("Refresh is waiting for the current cooldown.");
                    if (!force && IsFresh(timing))
                        return new ConfigResult(ConfigOutcome.FreshCache);

                    bool recovered304 = false;
                    bool unconditional = false;
                    ConfigError lastTransient = null;
                    for (int attempt = 0; attempt < remoteOptions.MaxAttempts; attempt++)
                    {
                        operation.Token.ThrowIfCancellationRequested();
                        if (generation != Interlocked.Read(ref contentGeneration))
                            return new ConfigResult(ConfigOutcome.Superseded);
                        TimeSpan remaining = remoteOptions.OperationTimeout - (clock.Elapsed - started);
                        if (remaining <= TimeSpan.Zero)
                            return TransientFailure(ConfigErrorCategory.Timeout, "Refresh operation exceeded its time budget.");

                        FetchRepresentation representation = Volatile.Read(ref fetched);
                        string etag = !unconditional && RepresentationMatchesCurrent(representation)
                            ? representation.ETag : null;
                        TimeSpan timeout = remaining < remoteOptions.RequestTimeout ? remaining : remoteOptions.RequestTimeout;
                        RecordAttempt();
                        ConfigHttpResponse response;
                        try
                        {
                            response = await SendBoundedAsync(
                                new ConfigHttpRequest(requestUri, etag, timeout, options.MaxPayloadBytes),
                                operation.Token).ConfigureAwait(false);
                        }
                        catch (ConfigTransportException exception)
                        {
                            if (generation != Interlocked.Read(ref contentGeneration))
                                return new ConfigResult(ConfigOutcome.Superseded);
                            if (!exception.IsRetryable)
                                return RefreshFailure(ConfigErrorCategory.Protocol, exception.Message);
                            lastTransient = new ConfigError(exception.IsTimeout
                                ? ConfigErrorCategory.Timeout : ConfigErrorCategory.Transport, exception.Message);
                            if (!await DelayRetryAsync(attempt, started, operation.Token).ConfigureAwait(false))
                                return TransientFailure(lastTransient.Category, lastTransient.Message);
                            continue;
                        }

                        if (generation != Interlocked.Read(ref contentGeneration))
                            return new ConfigResult(ConfigOutcome.Superseded);
                        operation.Token.ThrowIfCancellationRequested();
                        if (response == null)
                            return RefreshFailure(ConfigErrorCategory.Protocol, "The transport returned no response.");

                        if (response.StatusCode == 200)
                        {
                            ConfigResult result = await AcceptResponseAsync(response.Body, response.ETag,
                                generation, operation.Token).ConfigureAwait(false);
                            return ReportRefreshResult(result);
                        }
                        if (response.StatusCode == 304)
                        {
                            if (etag != null && representation != null && RepresentationMatchesCurrent(representation))
                            {
                                ConfigResult result = await AcceptNotModifiedAsync(representation,
                                    response.ETag ?? etag, generation, operation.Token).ConfigureAwait(false);
                                return ReportRefreshResult(result);
                            }
                            if (recovered304 || attempt + 1 >= remoteOptions.MaxAttempts)
                                return RefreshFailure(ConfigErrorCategory.Protocol,
                                    "Received 304 without a matching cached representation.");
                            recovered304 = true;
                            unconditional = true;
                            continue;
                        }
                        if (response.StatusCode == 429)
                        {
                            TimeSpan wait = ParseRetryAfter(response.RetryAfter);
                            SetBlocked(wait);
                            remaining = remoteOptions.OperationTimeout - (clock.Elapsed - started);
                            if (attempt + 1 >= remoteOptions.MaxAttempts || wait >= remaining)
                                return ThrottledResult("The server requested a later refresh.");
                            await clock.DelayAsync(wait, operation.Token).ConfigureAwait(false);
                            continue;
                        }
                        if (response.StatusCode >= 500 && response.StatusCode <= 599)
                        {
                            lastTransient = new ConfigError(ConfigErrorCategory.Transport,
                                "Config service returned HTTP " + response.StatusCode + ".");
                            if (!await DelayRetryAsync(attempt, started, operation.Token).ConfigureAwait(false))
                                return TransientFailure(lastTransient.Category, lastTransient.Message);
                            continue;
                        }
                        return RefreshFailure(ConfigErrorCategory.Protocol,
                            "Config request failed with HTTP " + response.StatusCode + ".");
                    }
                    return TransientFailure(lastTransient?.Category ?? ConfigErrorCategory.Transport,
                        lastTransient?.Message ?? "Refresh exhausted its request budget.");
                }
                catch (OperationCanceledException)
                {
                    if (callerToken.IsCancellationRequested || lifetime.IsCancellationRequested)
                        return CancelledResult();
                    if (generation != Interlocked.Read(ref contentGeneration))
                        return new ConfigResult(ConfigOutcome.Superseded);
                    return TransientFailure(ConfigErrorCategory.Timeout, "Refresh operation exceeded its time budget.");
                }
                catch (Exception exception)
                {
                    if (generation != Interlocked.Read(ref contentGeneration))
                        return new ConfigResult(ConfigOutcome.Superseded);
                    return RefreshFailure(ConfigErrorCategory.Transport, exception.Message);
                }
            }
        }

        private async Task<ConfigHttpResponse> SendBoundedAsync(ConfigHttpRequest request, CancellationToken token)
        {
            using (var attempt = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                attempt.CancelAfter(request.Timeout);
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (attempt.Token.Register(() => cancelled.TrySetResult(true)))
                {
                    Task<ConfigHttpResponse> send;
                    try { send = transport.GetAsync(request, attempt.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (ConfigTransportException) { throw; }
                    catch (Exception exception) { throw new ConfigTransportException(exception.Message); }
                    if (send == null)
                        throw new ConfigTransportException("Transport returned no task.", isRetryable: false);

                    Task winner = await Task.WhenAny(send, cancelled.Task).ConfigureAwait(false);
                    if (winner != send)
                    {
                        ObserveLateFailure(send);
                        token.ThrowIfCancellationRequested();
                        throw new ConfigTransportException("Config request timed out.", isTimeout: true);
                    }
                    try { return await send.ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        token.ThrowIfCancellationRequested();
                        if (attempt.IsCancellationRequested)
                            throw new ConfigTransportException("Config request timed out.", isTimeout: true);
                        throw new ConfigTransportException("Transport cancelled the request.");
                    }
                    catch (ConfigTransportException) { throw; }
                    catch (Exception exception) { throw new ConfigTransportException(exception.Message); }
                }
            }
        }

        private static void ObserveLateFailure(Task task)
        {
            task.ContinueWith(completed => { var observed = completed.Exception; },
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private async Task<bool> DelayRetryAsync(int attempt, TimeSpan started, CancellationToken token)
        {
            if (attempt + 1 >= remoteOptions.MaxAttempts) return false;
            double jitter = 0.8 + retryRandom.NextDouble() * 0.4;
            TimeSpan delay = TimeSpan.FromSeconds((attempt == 0 ? 2 : 5) * jitter);
            TimeSpan remaining = remoteOptions.OperationTimeout - (clock.Elapsed - started);
            if (delay >= remaining) return false;
            await clock.DelayAsync(delay, token).ConfigureAwait(false);
            return true;
        }

        private static async Task<ConfigResult> WaitForSharedAsync(Task<ConfigResult> shared, CancellationToken token)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(shared, cancelled.Task).ConfigureAwait(false) != shared)
                    return CancelledResult();
                return await shared.ConfigureAwait(false);
            }
        }

        private static ConfigResult CancelledResult() => new ConfigResult(ConfigOutcome.Cancelled,
            new ConfigError(ConfigErrorCategory.Cancelled, "Refresh was cancelled."));

        private ConfigResult ReportRefreshResult(ConfigResult result)
        {
            if (result.Outcome == ConfigOutcome.Failed && result.Error != null)
                Dispatch(RefreshFailed, result.Error);
            return result;
        }

        private ConfigResult RefreshFailure(ConfigErrorCategory category, string message)
        {
            var error = new ConfigError(category, message);
            Report(error);
            Dispatch(RefreshFailed, error);
            return new ConfigResult(ConfigOutcome.Failed, error);
        }

        private ConfigResult TransientFailure(ConfigErrorCategory category, string message)
        {
            SetBlocked(remoteOptions.FailureCooldown);
            return RefreshFailure(category, message);
        }

        private ConfigResult ThrottledResult(string message)
        {
            var error = new ConfigError(ConfigErrorCategory.Throttled, message);
            Report(error);
            Dispatch(RefreshFailed, error);
            return new ConfigResult(ConfigOutcome.Throttled, error);
        }

        private bool IsFresh(RefreshTiming timing)
        {
            return RepresentationMatchesCurrent(Volatile.Read(ref fetched)) &&
                timing.SuccessElapsed.HasValue &&
                clock.Elapsed >= timing.SuccessElapsed.Value &&
                clock.Elapsed - timing.SuccessElapsed.Value < remoteOptions.MinimumRefreshInterval;
        }

        private void RecordAttempt()
        {
            DateTimeOffset now = clock.UtcNow;
            UpdateTiming(previous => new RefreshTiming(now, previous.SuccessUtc,
                previous.SuccessElapsed, previous.BlockedUntilUtc, previous.BlockedUntilElapsed));
        }

        private void SetBlocked(TimeSpan delay)
        {
            TimeSpan untilElapsed = SafeAdd(clock.Elapsed, delay);
            DateTimeOffset untilUtc = SafeAdd(clock.UtcNow, delay);
            UpdateTiming(previous =>
            {
                if (previous.BlockedUntilElapsed.HasValue && previous.BlockedUntilElapsed.Value > untilElapsed)
                    return previous;
                return new RefreshTiming(previous.AttemptUtc, previous.SuccessUtc,
                    previous.SuccessElapsed, untilUtc, untilElapsed);
            });
        }

        // Timing fields are updated from both local mutations and network continuations.
        // CAS preserves independent fields when, for example, a local stage clears freshness
        // at the same time a response establishes a server throttle deadline.
        private void UpdateTiming(Func<RefreshTiming, RefreshTiming> update)
        {
            RefreshTiming previous;
            RefreshTiming replacement;
            do
            {
                previous = Volatile.Read(ref refreshTiming);
                replacement = update(previous);
            } while (!ReferenceEquals(Interlocked.CompareExchange(ref refreshTiming, replacement, previous), previous));
        }

        private TimeSpan ParseRetryAfter(string value)
        {
            value = value?.Trim();
            if (long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long seconds))
            {
                if (seconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond) return TimeSpan.MaxValue;
                return TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond);
            }
            if (!string.IsNullOrWhiteSpace(value) && IsDigits(value))
                return TimeSpan.MaxValue;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset date))
                return date > clock.UtcNow ? date - clock.UtcNow : TimeSpan.Zero;
            return remoteOptions.FailureCooldown;
        }

        private static bool IsDigits(string value)
        {
            foreach (char character in value)
                if (character < '0' || character > '9') return false;
            return true;
        }

        private static TimeSpan SafeAdd(TimeSpan first, TimeSpan second)
            => second.Ticks > TimeSpan.MaxValue.Ticks - first.Ticks ? TimeSpan.MaxValue : first + second;

        private static DateTimeOffset SafeAdd(DateTimeOffset first, TimeSpan second)
            => second.Ticks > DateTimeOffset.MaxValue.UtcDateTime.Ticks - first.UtcDateTime.Ticks ? DateTimeOffset.MaxValue : first + second;

        private ConfigStatus CreateStatus(State current)
        {
            RefreshTiming timing = Volatile.Read(ref refreshTiming);
            DateTimeOffset? next = timing.BlockedUntilElapsed.HasValue &&
                clock.Elapsed < timing.BlockedUntilElapsed.Value ? timing.BlockedUntilUtc : null;
            if (remoteOptions != null && IsFresh(timing) && timing.SuccessUtc.HasValue)
            {
                DateTimeOffset freshUntil = SafeAdd(timing.SuccessUtc.Value, remoteOptions.MinimumRefreshInterval);
                if (!next.HasValue || freshUntil > next.Value) next = freshUntil;
            }
            bool refreshing;
            lock (refreshGate) refreshing = sharedRefresh != null && !sharedRefresh.IsCompleted;
            return new ConfigStatus(current.Ready, current.Active?.Revision, current.Pending?.Revision,
                current.Error, timing.AttemptUtc, timing.SuccessUtc, next, refreshing);
        }
    }
}
