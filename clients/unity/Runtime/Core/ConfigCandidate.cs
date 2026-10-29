using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    internal sealed class ConfigCandidate
    {
        internal JObject Envelope { get; }
        internal ConfigSnapshot Snapshot { get; }
        internal string Hash { get; }
        internal string Revision => Snapshot.Revision;

        private ConfigCandidate(JObject envelope, ConfigSnapshot snapshot)
        {
            Envelope = envelope;
            Snapshot = snapshot;
            Hash = JsonContract.Hash(envelope);
        }

        internal static ConfigCandidate Create(JObject envelope, ConfigOptions options,
            Dictionary<string, IConfigDefinition> definitions, Dictionary<string, JToken> defaults)
        {
            if (JsonContract.Integer(envelope, "schemaVersion") != 1)
                throw new ConfigException(ConfigErrorCategory.Compatibility, "Unsupported response schema version.");
            if (JsonContract.Text(envelope, "projectId") != options.ProjectId ||
                JsonContract.Text(envelope, "environment") != options.Environment ||
                JsonContract.Text(envelope, "platform") != options.Platform ||
                JsonContract.Integer(envelope, "configContractVersion") != options.ConfigContractVersion)
                throw new ConfigException(ConfigErrorCategory.Compatibility, "Response context does not match this client.");

            string revision = JsonContract.Text(envelope, "revision");
            string timestamp = JsonContract.Text(envelope, "publishedAt");
            string[] timestampFormats = { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };
            if (!DateTimeOffset.TryParseExact(timestamp, timestampFormats, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out _))
                throw new ConfigException(ConfigErrorCategory.Protocol, "publishedAt must be a valid UTC timestamp ending in Z.");

            JObject compatibility = JsonContract.Object(envelope, "compatibility");
            long minimum = JsonContract.Integer(compatibility, "minAppBuild");
            JToken maxToken = compatibility["maxAppBuild"];
            if (maxToken == null || (maxToken.Type != JTokenType.Null && maxToken.Type != JTokenType.Integer))
                throw new ConfigException(ConfigErrorCategory.Protocol, "maxAppBuild must be an integer or null.");
            long? maximum = maxToken.Type == JTokenType.Null ? (long?)null : maxToken.Value<long>();
            if (minimum < 0 || (maximum.HasValue && maximum.Value < minimum))
                throw new ConfigException(ConfigErrorCategory.Protocol, "Invalid app build compatibility range.");
            if (options.AppBuild < minimum || (maximum.HasValue && options.AppBuild > maximum.Value))
                throw new ConfigException(ConfigErrorCategory.Compatibility, "Config is incompatible with this app build.");

            JObject remote = JsonContract.Object(envelope, "values");
            var values = new Dictionary<string, JToken>(StringComparer.Ordinal);
            var sources = new Dictionary<string, ConfigValueSource>(StringComparer.Ordinal);
            foreach (var item in definitions)
            {
                bool supplied = remote.TryGetValue(item.Key, StringComparison.Ordinal, out JToken token);
                try
                {
                    values.Add(item.Key, supplied
                        ? item.Value.ValidateAndCopy(token)
                        : defaults[item.Key].DeepClone());
                    sources.Add(item.Key, supplied ? ConfigValueSource.Remote : ConfigValueSource.Default);
                }
                catch (Exception exception)
                {
                    throw new ConfigException(ConfigErrorCategory.Validation,
                        "Invalid feature '" + item.Key + "': " + exception.Message);
                }
            }
            return new ConfigCandidate((JObject)envelope.DeepClone(),
                new ConfigSnapshot(revision, definitions, values, sources));
        }

        internal string ToRecord(ConfigOptions options)
        {
            return JsonContract.CanonicalJson(new JObject
            {
                ["storageFormatVersion"] = 1,
                ["cacheKey"] = options.CacheKey,
                ["responseHash"] = Hash,
                ["response"] = Envelope.DeepClone()
            });
        }

        internal static ConfigCandidate FromRecord(string record, ConfigOptions options,
            Dictionary<string, IConfigDefinition> definitions, Dictionary<string, JToken> defaults)
        {
            JObject wrapper = JsonContract.ParseObject(record, options.MaxPayloadBytes + 8192);
            if (JsonContract.Integer(wrapper, "storageFormatVersion") != 1 ||
                JsonContract.Text(wrapper, "cacheKey") != options.CacheKey)
                throw new ConfigException(ConfigErrorCategory.Compatibility, "Saved record belongs to another context or format.");
            JObject envelope = JsonContract.Object(wrapper, "response");
            // Reparse the envelope separately to enforce the same size bound as a new input.
            envelope = JsonContract.ParseObject(JsonContract.CanonicalJson(envelope), options.MaxPayloadBytes);
            if (!string.Equals(JsonContract.Hash(envelope), JsonContract.Text(wrapper, "responseHash"),
                    StringComparison.Ordinal))
                throw new ConfigException(ConfigErrorCategory.Protocol, "Saved configuration checksum does not match.");
            return Create(envelope, options, definitions, defaults);
        }
    }
}
