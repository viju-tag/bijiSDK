using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BijiSDK
{
    public sealed partial class ConfigClient
    {
        private Task<ConfigResult> AcceptResponseAsync(string json, string etag, long expectedGeneration,
            CancellationToken cancellationToken)
        {
            return ExecuteAsync(token =>
            {
                if (expectedGeneration != Interlocked.Read(ref contentGeneration))
                    return new ConfigResult(ConfigOutcome.Superseded);
                State current = RequireReady();
                ConfigCandidate candidate = ConfigCandidate.Create(
                    JsonContract.ParseObject(json, options.MaxPayloadBytes), options, definitions, defaults);
                ValidateRevision(candidate, current);
                string safeETag = ValidateETag(etag);
                DateTimeOffset now = clock.UtcNow;
                var representation = new FetchRepresentation(candidate, safeETag, now);
                token.ThrowIfCancellationRequested();

                // Save metadata first. If staging fails, this orphan record is ignored on restore
                // because it does not match the latest active/pending candidate. Existing metadata
                // remains available as a backup. No pending value is changed on metadata failure.
                StorageAction(() => storage.Write(options.CacheKey, ConfigSlot.Fetch,
                    SerializeFetch(representation)));
                ConfigResult result = StageCandidate(candidate, token);
                Volatile.Write(ref fetched, representation);
                RecordSuccess(now);
                return result;
            }, cancellationToken);
        }

        private Task<ConfigResult> AcceptNotModifiedAsync(FetchRepresentation representation, string etag,
            long expectedGeneration, CancellationToken cancellationToken)
        {
            return ExecuteAsync(token =>
            {
                if (expectedGeneration != Interlocked.Read(ref contentGeneration))
                    return new ConfigResult(ConfigOutcome.Superseded);
                RequireReady();
                if (!RepresentationMatchesCurrent(representation))
                    throw new ConfigException(ConfigErrorCategory.Protocol,
                        "The representation for the 304 response is no longer available.");
                DateTimeOffset now = clock.UtcNow;
                var updated = new FetchRepresentation(representation.Candidate, ValidateETag(etag), now);
                token.ThrowIfCancellationRequested();
                StorageAction(() => storage.Write(options.CacheKey, ConfigSlot.Fetch, SerializeFetch(updated)));
                Volatile.Write(ref fetched, updated);
                RecordSuccess(now);
                return new ConfigResult(ConfigOutcome.Unchanged);
            }, cancellationToken);
        }

        private static string ValidateETag(string etag)
        {
            if (string.IsNullOrWhiteSpace(etag)) return null;
            if (etag.Length > 1024)
                throw new ConfigException(ConfigErrorCategory.Protocol, "ETag exceeds the supported header size.");
            foreach (char character in etag)
            {
                if (character < 32 || character == 127)
                    throw new ConfigException(ConfigErrorCategory.Protocol, "ETag contains an invalid header character.");
            }
            return etag;
        }

        private string SerializeFetch(FetchRepresentation representation)
        {
            return JsonContract.CanonicalJson(new JObject
            {
                ["storageFormatVersion"] = 1,
                ["cacheKey"] = options.CacheKey,
                ["sourceUrl"] = requestUri.AbsoluteUri,
                ["etag"] = representation.ETag == null ? JValue.CreateNull() : new JValue(representation.ETag),
                ["lastSuccessUtc"] = representation.SuccessUtc.Value.UtcDateTime.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
                ["responseHash"] = representation.Candidate.Hash,
                ["response"] = representation.Candidate.Envelope.DeepClone()
            });
        }

        private void RestoreFetchMetadata()
        {
            if (remoteOptions == null) return;
            try
            {
                foreach (string record in storage.ReadCandidates(options.CacheKey, ConfigSlot.Fetch))
                {
                    try
                    {
                        JObject wrapper = JsonContract.ParseObject(record, options.MaxPayloadBytes + 8192);
                        if (JsonContract.Integer(wrapper, "storageFormatVersion") != 1 ||
                            JsonContract.Text(wrapper, "cacheKey") != options.CacheKey ||
                            JsonContract.Text(wrapper, "sourceUrl") != requestUri.AbsoluteUri)
                            continue;
                        JObject envelope = JsonContract.ParseObject(
                            JsonContract.CanonicalJson(JsonContract.Object(wrapper, "response")), options.MaxPayloadBytes);
                        if (JsonContract.Hash(envelope) != JsonContract.Text(wrapper, "responseHash"))
                            continue;
                        ConfigCandidate candidate = ConfigCandidate.Create(envelope, options, definitions, defaults);
                        JToken etagToken = wrapper["etag"];
                        if (etagToken == null || (etagToken.Type != JTokenType.Null && etagToken.Type != JTokenType.String))
                            continue;
                        string etag = ValidateETag(etagToken.Type == JTokenType.Null ? null : etagToken.Value<string>());
                        DateTimeOffset? successUtc = null;
                        JToken timestamp = wrapper["lastSuccessUtc"];
                        if (timestamp != null && timestamp.Type == JTokenType.String &&
                            DateTimeOffset.TryParseExact(timestamp.Value<string>(),
                                "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
                                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed) &&
                            parsed <= clock.UtcNow)
                            successUtc = parsed;
                        var restored = new FetchRepresentation(candidate, etag, successUtc);
                        if (!RepresentationMatchesCurrent(restored))
                            continue;
                        Volatile.Write(ref fetched, restored);
                        if (successUtc.HasValue)
                        {
                            TimeSpan age = clock.UtcNow - successUtc.Value;
                            TimeSpan successElapsed = clock.Elapsed - age;
                            UpdateTiming(previous => new RefreshTiming(previous.AttemptUtc,
                                successUtc, successElapsed, previous.BlockedUntilUtc, previous.BlockedUntilElapsed));
                        }
                        return;
                    }
                    catch (Exception exception)
                    {
                        Report(new ConfigError(ConfigErrorCategory.Storage,
                            "Skipped invalid saved fetch metadata: " + exception.Message));
                    }
                }
            }
            catch (Exception exception)
            {
                Report(new ConfigError(ConfigErrorCategory.Storage,
                    "Could not restore fetch metadata: " + exception.Message));
            }
        }

        private bool RepresentationMatchesCurrent(FetchRepresentation representation)
        {
            if (representation == null) return false;
            State current = Volatile.Read(ref state);
            ConfigCandidate latest = current.Pending ?? current.Active;
            return current.Ready && latest != null && latest.Hash == representation.Candidate.Hash;
        }

        private void InvalidateFetchForLocalStage()
        {
            if (remoteOptions == null) return;
            StorageAction(() => storage.Delete(options.CacheKey, ConfigSlot.Fetch));
            Volatile.Write(ref fetched, null);
            UpdateTiming(previous => new RefreshTiming(previous.AttemptUtc, null, null,
                previous.BlockedUntilUtc, previous.BlockedUntilElapsed));
        }

        private void RecordSuccess(DateTimeOffset now)
        {
            TimeSpan successElapsed = clock.Elapsed;
            UpdateTiming(previous => new RefreshTiming(previous.AttemptUtc, now, successElapsed));
        }
    }
}
