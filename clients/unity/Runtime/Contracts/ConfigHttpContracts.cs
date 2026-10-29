using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace BijiSDK
{
    public interface IConfigTransport
    {
        Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken cancellationToken);
    }

    public sealed class ConfigHttpRequest
    {
        public Uri Url { get; }
        public string IfNoneMatch { get; }
        public TimeSpan Timeout { get; }
        public int MaxResponseBytes { get; }

        public ConfigHttpRequest(Uri url, string ifNoneMatch, TimeSpan timeout, int maxResponseBytes)
        {
            Url = url ?? throw new ArgumentNullException(nameof(url));
            IfNoneMatch = ifNoneMatch;
            Timeout = timeout;
            MaxResponseBytes = maxResponseBytes;
        }
    }

    public sealed class ConfigHttpResponse
    {
        public int StatusCode { get; }
        public string Body { get; }
        public string ETag { get; }
        public string RetryAfter { get; }

        public ConfigHttpResponse(int statusCode, string body, string eTag = null, string retryAfter = null)
        {
            StatusCode = statusCode;
            Body = body;
            ETag = eTag;
            RetryAfter = retryAfter;
        }
    }

    public sealed class ConfigTransportException : Exception
    {
        public bool IsTimeout { get; }
        public bool IsRetryable { get; }

        public ConfigTransportException(string message, bool isTimeout = false, bool isRetryable = true)
            : base(message)
        {
            IsTimeout = isTimeout;
            IsRetryable = isRetryable;
        }
    }

    public interface IConfigClock
    {
        DateTimeOffset UtcNow { get; }
        TimeSpan Elapsed { get; }
        Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
    }

    public sealed class SystemConfigClock : IConfigClock
    {
        private readonly Stopwatch elapsed = Stopwatch.StartNew();
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
        public TimeSpan Elapsed => elapsed.Elapsed;
        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
            => Task.Delay(delay, cancellationToken);
    }

    public sealed class ConfigRemoteOptions
    {
        public Uri BaseUri { get; }
        public bool AllowInsecureHttp { get; }
        public TimeSpan MinimumRefreshInterval { get; }
        public TimeSpan RequestTimeout { get; }
        public int MaxAttempts { get; }
        public TimeSpan OperationTimeout { get; }
        public TimeSpan FailureCooldown { get; }

        public ConfigRemoteOptions(Uri baseUri, bool allowInsecureHttp = false,
            TimeSpan? minimumRefreshInterval = null, TimeSpan? requestTimeout = null,
            int maxAttempts = 3, TimeSpan? operationTimeout = null, TimeSpan? failureCooldown = null)
        {
            if (baseUri == null || !baseUri.IsAbsoluteUri)
                throw new ArgumentException("An absolute config base URI is required.", nameof(baseUri));
            if (baseUri.Scheme != Uri.UriSchemeHttps &&
                !(allowInsecureHttp && baseUri.Scheme == Uri.UriSchemeHttp))
                throw new ArgumentException("HTTPS is required unless development HTTP is explicitly enabled.", nameof(baseUri));
            if (!string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) ||
                !string.IsNullOrEmpty(baseUri.Fragment))
                throw new ArgumentException("The base URI must not contain credentials, query parameters, or a fragment.", nameof(baseUri));
            if (maxAttempts < 1 || maxAttempts > 3)
                throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Use one to three attempts.");

            BaseUri = new Uri(baseUri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
            AllowInsecureHttp = allowInsecureHttp;
            MinimumRefreshInterval = ValidateDuration(minimumRefreshInterval ?? TimeSpan.FromMinutes(15),
                nameof(minimumRefreshInterval), true);
            RequestTimeout = ValidateDuration(requestTimeout ?? TimeSpan.FromSeconds(10), nameof(requestTimeout), false);
            OperationTimeout = ValidateDuration(operationTimeout ?? TimeSpan.FromSeconds(45), nameof(operationTimeout), false);
            if (RequestTimeout > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(requestTimeout), "Request timeout must not exceed one day.");
            if (OperationTimeout > TimeSpan.FromDays(1))
                throw new ArgumentOutOfRangeException(nameof(operationTimeout), "Operation timeout must not exceed one day.");
            MaxAttempts = maxAttempts;
            FailureCooldown = ValidateDuration(failureCooldown ?? TimeSpan.FromSeconds(60), nameof(failureCooldown), true);
        }

        private static TimeSpan ValidateDuration(TimeSpan value, string parameter, bool allowZero)
        {
            if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) ||
                value > TimeSpan.FromDays(365))
                throw new ArgumentOutOfRangeException(parameter, "Duration must be positive and no more than 365 days.");
            return value;
        }
    }
}
