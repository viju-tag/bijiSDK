using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    public enum ConfigSlot { Active, Pending, Fetch }
    public enum ConfigValueSource { Default, Remote }
    public enum ConfigOutcome
    {
        ReadyWithDefaults, ReadyWithSavedConfig, UpdateAvailable, Unchanged,
        Activated, NoPendingUpdate, Cancelled, Failed, FreshCache, Throttled, Superseded
    }
    public enum ConfigErrorCategory
    {
        Initialization, Protocol, Compatibility, Validation, Storage, Cancelled, Subscriber, Transport, Timeout, Throttled
    }

    public interface IConfigStorage
    {
        IReadOnlyList<string> ReadCandidates(string cacheKey, ConfigSlot slot);
        void Write(string cacheKey, ConfigSlot slot, string record);
        void Delete(string cacheKey, ConfigSlot slot);
    }

    public interface IMainThreadDispatcher
    {
        void Post(Action callback);
    }

    public sealed class ConfigOptions
    {
        public string ProjectId { get; }
        public string Environment { get; }
        public string Platform { get; }
        public long AppBuild { get; }
        public int ConfigContractVersion { get; }
        public int MaxPayloadBytes { get; }
        public string CacheKey { get; }

        public ConfigOptions(string projectId, string environment, string platform,
            long appBuild, int configContractVersion, int maxPayloadBytes = 1048576)
        {
            ValidateIdentifier(projectId, nameof(projectId));
            ValidateIdentifier(environment, nameof(environment));
            ValidateIdentifier(platform, nameof(platform));
            if (appBuild < 0 || appBuild > 9007199254740991L)
                throw new ArgumentOutOfRangeException(nameof(appBuild));
            if (configContractVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(configContractVersion));
            if (maxPayloadBytes < 1 || maxPayloadBytes > int.MaxValue - 8192)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            ProjectId = projectId;
            Environment = environment;
            Platform = platform;
            AppBuild = appBuild;
            ConfigContractVersion = configContractVersion;
            MaxPayloadBytes = maxPayloadBytes;
            CacheKey = JsonContract.Hash(new JArray(
                projectId, environment, platform, appBuild, configContractVersion));
        }

        private static void ValidateIdentifier(string value, string parameter)
        {
            if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"\A[a-z][a-z0-9-]*\z"))
                throw new ArgumentException("Use a lowercase identifier with optional digits and hyphens.", parameter);
        }
    }

    public sealed class ConfigError
    {
        public ConfigErrorCategory Category { get; }
        public string Message { get; }
        public ConfigError(ConfigErrorCategory category, string message)
        {
            Category = category;
            Message = message ?? string.Empty;
        }
        public override string ToString() => Category + ": " + Message;
    }

    public sealed class ConfigResult
    {
        public ConfigOutcome Outcome { get; }
        public ConfigError Error { get; }
        public bool IsSuccess => Outcome != ConfigOutcome.Failed && Outcome != ConfigOutcome.Cancelled &&
            Outcome != ConfigOutcome.Throttled && Outcome != ConfigOutcome.Superseded;

        internal ConfigResult(ConfigOutcome outcome, ConfigError error = null)
        {
            Outcome = outcome;
            Error = error;
        }
    }

    public sealed class ConfigStatus
    {
        public bool IsInitialized { get; }
        public string ActiveRevision { get; }
        public string PendingRevision { get; }
        public ConfigError LastError { get; }
        public DateTimeOffset? LastAttemptUtc { get; }
        public DateTimeOffset? LastSuccessUtc { get; }
        public DateTimeOffset? NextEligibleRefreshUtc { get; }
        public bool IsRefreshing { get; }

        internal ConfigStatus(bool initialized, string activeRevision, string pendingRevision, ConfigError lastError,
            DateTimeOffset? lastAttemptUtc = null, DateTimeOffset? lastSuccessUtc = null,
            DateTimeOffset? nextEligibleRefreshUtc = null, bool isRefreshing = false)
        {
            IsInitialized = initialized;
            ActiveRevision = activeRevision;
            PendingRevision = pendingRevision;
            LastError = lastError;
            LastAttemptUtc = lastAttemptUtc;
            LastSuccessUtc = lastSuccessUtc;
            NextEligibleRefreshUtc = nextEligibleRefreshUtc;
            IsRefreshing = isRefreshing;
        }
    }

    public interface IConfigDefinition
    {
        string Key { get; }
        Type ValueType { get; }
        JToken CreateDefaultValue();
        JToken ValidateAndCopy(JToken value);
        object DeserializeCopy(JToken value);
    }
}
