using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    /// <summary>
    /// Offline configuration core. Operations run on a worker; all definition delegates
    /// and storage implementations must be thread-safe. Events use the injected dispatcher.
    /// </summary>
    public sealed partial class ConfigClient : IDisposable
    {
        private sealed class State
        {
            internal readonly bool Ready;
            internal readonly ConfigSnapshot Snapshot;
            internal readonly ConfigCandidate Active;
            internal readonly ConfigCandidate Pending;
            internal readonly ConfigError Error;

            internal State(bool ready, ConfigSnapshot snapshot, ConfigCandidate active,
                ConfigCandidate pending, ConfigError error)
            {
                Ready = ready;
                Snapshot = snapshot;
                Active = active;
                Pending = pending;
                Error = error;
            }
        }

        private readonly ConfigOptions options;
        private readonly IConfigDefinition[] registrations;
        private readonly IConfigStorage storage;
        private readonly IMainThreadDispatcher dispatcher;
        private readonly SemaphoreSlim mutation = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private Dictionary<string, IConfigDefinition> definitions;
        private Dictionary<string, JToken> defaults;
        private State state = new State(false, null, null, null, null);
        private int disposed;

        public event Action<string> UpdateAvailable;
        public event Action<string> ConfigActivated;
        public event Action<ConfigError> ErrorOccurred;

        public ConfigClient(ConfigOptions options, IEnumerable<IConfigDefinition> definitions,
            IConfigStorage storage, IMainThreadDispatcher dispatcher, IConfigTransport transport = null,
            ConfigRemoteOptions remoteOptions = null, IConfigClock clock = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            registrations = (definitions ?? throw new ArgumentNullException(nameof(definitions))).ToArray();
            this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            ConfigureRemote(transport, remoteOptions, clock);
        }

        public Task<ConfigResult> InitializeAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(token =>
            {
                State current = Volatile.Read(ref state);
                if (current.Ready)
                    return new ConfigResult(current.Active == null
                        ? ConfigOutcome.ReadyWithDefaults : ConfigOutcome.ReadyWithSavedConfig);

                var registered = new Dictionary<string, IConfigDefinition>(StringComparer.Ordinal);
                var defaultValues = new Dictionary<string, JToken>(StringComparer.Ordinal);
                try
                {
                    foreach (IConfigDefinition definition in registrations)
                    {
                        token.ThrowIfCancellationRequested();
                        if (definition == null || string.IsNullOrWhiteSpace(definition.Key))
                            throw new ArgumentException("Definitions must be non-null and have nonblank keys.");
                        registered.Add(definition.Key, definition);
                        defaultValues.Add(definition.Key, definition.CreateDefaultValue().DeepClone());
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception)
                {
                    throw new ConfigException(ConfigErrorCategory.Initialization,
                        "Invalid config definitions: " + exception.Message);
                }

                definitions = registered;
                defaults = defaultValues;
                ConfigCandidate active = Restore(ConfigSlot.Active, token);
                ConfigCandidate pending = Restore(ConfigSlot.Pending, token);
                if (active != null && pending != null && active.Revision == pending.Revision)
                {
                    if (active.Hash != pending.Hash)
                        Report(new ConfigError(ConfigErrorCategory.Protocol,
                            "Discarded pending data that reuses the active revision with different content."));
                    pending = null;
                    TryDeletePending();
                }

                var sources = defaultValues.ToDictionary(pair => pair.Key,
                    pair => ConfigValueSource.Default, StringComparer.Ordinal);
                ConfigSnapshot snapshot = active?.Snapshot ??
                    new ConfigSnapshot(null, registered, defaultValues, sources);
                token.ThrowIfCancellationRequested();
                Volatile.Write(ref state, new State(true, snapshot, active, pending,
                    Volatile.Read(ref state).Error));
                RestoreFetchMetadata();
                return new ConfigResult(active == null
                    ? ConfigOutcome.ReadyWithDefaults : ConfigOutcome.ReadyWithSavedConfig);
            }, cancellationToken);
        }

        /// <summary>Validate a local or downloaded JSON response and persist it as pending.</summary>
        public Task<ConfigResult> StageJsonAsync(string json, CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(token =>
            {
                State current = RequireReady();
                JObject envelope = JsonContract.ParseObject(json, options.MaxPayloadBytes);
                ConfigCandidate candidate = ConfigCandidate.Create(envelope, options, definitions, defaults);
                ValidateRevision(candidate, current);
                token.ThrowIfCancellationRequested();
                InvalidateFetchForLocalStage();
                Interlocked.Increment(ref contentGeneration);
                return StageCandidate(candidate, token);
            }, cancellationToken);
        }

        private static void ValidateRevision(ConfigCandidate candidate, State current)
        {
            foreach (ConfigCandidate existing in new[] { current.Active, current.Pending })
            {
                if (existing != null && existing.Revision == candidate.Revision && existing.Hash != candidate.Hash)
                    throw new ConfigException(ConfigErrorCategory.Protocol,
                        "A revision cannot be reused with different content.");
            }
        }

        // The caller holds the mutation gate and has checked any HTTP generation guard.
        private ConfigResult StageCandidate(ConfigCandidate candidate, CancellationToken token)
        {
            State current = RequireReady();
            ValidateRevision(candidate, current);
            token.ThrowIfCancellationRequested();
            if (current.Pending != null && current.Pending.Revision == candidate.Revision)
                return new ConfigResult(ConfigOutcome.Unchanged);

            if (current.Active != null && current.Active.Revision == candidate.Revision)
            {
                if (current.Pending != null)
                {
                    StorageAction(() => storage.Delete(options.CacheKey, ConfigSlot.Pending));
                    Volatile.Write(ref state, new State(true, current.Snapshot, current.Active, null, null));
                }
                return new ConfigResult(ConfigOutcome.Unchanged);
            }

            StorageAction(() => storage.Write(options.CacheKey, ConfigSlot.Pending, candidate.ToRecord(options)));
            // Cancellation cannot undo a completed durable commit.
            Volatile.Write(ref state, new State(true, current.Snapshot, current.Active, candidate, null));
            Dispatch(UpdateAvailable, candidate.Revision);
            return new ConfigResult(ConfigOutcome.UpdateAvailable);
        }

        public Task<ConfigResult> ActivatePendingAsync(CancellationToken cancellationToken = default)
        {
            return ExecuteAsync(token =>
            {
                State current = RequireReady();
                if (current.Pending == null)
                    return new ConfigResult(ConfigOutcome.NoPendingUpdate);
                ConfigCandidate candidate = current.Pending;
                string record = candidate.ToRecord(options);
                token.ThrowIfCancellationRequested();
                StorageAction(() => storage.Write(options.CacheKey, ConfigSlot.Active, record));
                Volatile.Write(ref state, new State(true, candidate.Snapshot, candidate, null, null));
                // Active commit is the success boundary. Cleanup failure cannot roll it back.
                TryDeletePending();
                Dispatch(ConfigActivated, candidate.Revision);
                return new ConfigResult(ConfigOutcome.Activated);
            }, cancellationToken);
        }

        public T Get<T>(string key) => GetSnapshot().Get<T>(key);

        public ConfigSnapshot GetSnapshot()
        {
            ThrowIfDisposed();
            return RequireReady().Snapshot;
        }

        public ConfigStatus GetStatus()
        {
            State current = Volatile.Read(ref state);
            return CreateStatus(current);
        }

        private ConfigCandidate Restore(ConfigSlot slot, CancellationToken token)
        {
            IReadOnlyList<string> records;
            try
            {
                records = storage.ReadCandidates(options.CacheKey, slot) ?? Array.Empty<string>();
            }
            catch (Exception exception)
            {
                Report(new ConfigError(ConfigErrorCategory.Storage,
                    "Could not read saved " + slot + " configuration: " + exception.Message));
                return null;
            }

            foreach (string record in records)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    return ConfigCandidate.FromRecord(record, options, definitions, defaults);
                }
                catch (Exception exception)
                {
                    Report(new ConfigError(ConfigErrorCategory.Storage,
                        "Skipped invalid saved " + slot + " configuration: " + exception.Message));
                }
            }
            return null;
        }

        private void TryDeletePending()
        {
            try { storage.Delete(options.CacheKey, ConfigSlot.Pending); }
            catch (Exception exception)
            {
                Report(new ConfigError(ConfigErrorCategory.Storage,
                    "Active state is usable, but pending cleanup failed: " + exception.Message));
            }
        }

        private static void StorageAction(Action action)
        {
            try { action(); }
            catch (Exception exception)
            {
                throw new ConfigException(ConfigErrorCategory.Storage, exception.Message);
            }
        }

        private async Task<ConfigResult> ExecuteAsync(Func<CancellationToken, ConfigResult> operation,
            CancellationToken callerToken)
        {
            if (Volatile.Read(ref disposed) != 0)
                return new ConfigResult(ConfigOutcome.Cancelled,
                    new ConfigError(ConfigErrorCategory.Cancelled, "The client is disposed."));
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, lifetime.Token))
            {
                bool entered = false;
                try
                {
                    await mutation.WaitAsync(linked.Token).ConfigureAwait(false);
                    entered = true;
                    linked.Token.ThrowIfCancellationRequested();
                    return await Task.Run(() => operation(linked.Token), linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    var error = new ConfigError(ConfigErrorCategory.Cancelled, "Operation cancelled before commit.");
                    return new ConfigResult(ConfigOutcome.Cancelled, error);
                }
                catch (ConfigException exception)
                {
                    var error = new ConfigError(exception.Category, exception.Message);
                    Report(error);
                    return new ConfigResult(ConfigOutcome.Failed, error);
                }
                catch (Exception exception)
                {
                    var error = new ConfigError(ConfigErrorCategory.Validation, exception.Message);
                    Report(error);
                    return new ConfigResult(ConfigOutcome.Failed, error);
                }
                finally
                {
                    if (entered) mutation.Release();
                }
            }
        }

        private State RequireReady()
        {
            State current = Volatile.Read(ref state);
            if (!current.Ready)
                throw new ConfigException(ConfigErrorCategory.Initialization, "Initialize the client before using it.");
            return current;
        }

        private void SetLastError(ConfigError error)
        {
            State previous;
            State replacement;
            do
            {
                previous = Volatile.Read(ref state);
                replacement = new State(previous.Ready, previous.Snapshot, previous.Active, previous.Pending, error);
            } while (!ReferenceEquals(Interlocked.CompareExchange(ref state, replacement, previous), previous));
        }

        private void Report(ConfigError error)
        {
            SetLastError(error);
            Dispatch(ErrorOccurred, error, false);
        }

        private void Dispatch<T>(Action<T> handlers, T value, bool reportSubscriberErrors = true)
        {
            if (handlers == null || Volatile.Read(ref disposed) != 0) return;
            try
            {
                dispatcher.Post(() =>
                {
                    foreach (Action<T> subscriber in handlers.GetInvocationList())
                    {
                        if (Volatile.Read(ref disposed) != 0) return;
                        try { subscriber(value); }
                        catch (Exception exception)
                        {
                            var error = new ConfigError(ConfigErrorCategory.Subscriber, exception.Message);
                            if (reportSubscriberErrors) Report(error);
                            else SetLastError(error);
                        }
                    }
                });
            }
            catch (Exception exception)
            {
                // If the dispatcher itself fails, reporting through it again would recurse.
                SetLastError(new ConfigError(ConfigErrorCategory.Subscriber,
                    "Callback dispatch failed: " + exception.Message));
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref disposed) != 0)
                throw new ObjectDisposedException(nameof(ConfigClient));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                lifetime.Cancel();
            // Keep the gate and token source alive for operations already in flight.
            // They own no OS handles unless consumers explicitly request their wait handles.
        }
    }
}
