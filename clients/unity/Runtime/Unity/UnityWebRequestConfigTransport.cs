using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace BijiSDK.Unity
{
    /// <summary>Bounded HTTP GET transport. Unity request objects are accessed only on the main thread.</summary>
    public sealed class UnityWebRequestConfigTransport : IConfigTransport
    {
        private readonly IMainThreadDispatcher dispatcher;
        private readonly bool allowInsecureHttp;

        public UnityWebRequestConfigTransport(IMainThreadDispatcher dispatcher, bool allowInsecureHttp = false)
        {
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            if (allowInsecureHttp && !Application.isEditor && !Debug.isDebugBuild)
                throw new InvalidOperationException("Insecure HTTP is available only in the Editor or a development build.");
            this.allowInsecureHttp = allowInsecureHttp;
        }

        public Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (!request.Url.IsAbsoluteUri)
                throw new ArgumentException("An absolute request URI is required.", nameof(request));
            if (request.Url.Scheme != Uri.UriSchemeHttps &&
                !(allowInsecureHttp && request.Url.Scheme == Uri.UriSchemeHttp))
                throw new ConfigTransportException("This transport requires HTTPS.", isRetryable: false);
            if (request.Timeout <= TimeSpan.Zero || request.Timeout > TimeSpan.FromDays(1) || request.MaxResponseBytes < 1)
                throw new ArgumentException("Request timeout must be positive and at most one day; response bound must be positive.", nameof(request));
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<ConfigHttpResponse>(cancellationToken);

            var operation = new RequestOperation(dispatcher, request, cancellationToken);
            operation.Start();
            return operation.Task;
        }

        private sealed class RequestOperation
        {
            private readonly IMainThreadDispatcher dispatcher;
            private readonly ConfigHttpRequest request;
            private readonly CancellationToken cancellation;
            private readonly TaskCompletionSource<ConfigHttpResponse> completion =
                new TaskCompletionSource<ConfigHttpResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            private UnityWebRequest webRequest;
            private BoundedDownloadHandler download;
            private CancellationTokenRegistration registration;
            private Timer timer;
            private bool finished;
            private bool cancelled;
            private bool timedOut;

            internal RequestOperation(IMainThreadDispatcher dispatcher, ConfigHttpRequest request,
                CancellationToken cancellation)
            {
                this.dispatcher = dispatcher;
                this.request = request;
                this.cancellation = cancellation;
            }

            internal Task<ConfigHttpResponse> Task => completion.Task;

            internal void Start()
            {
                try { dispatcher.Post(BeginOnMainThread); }
                catch (Exception exception) { completion.TrySetException(exception); }
            }

            private void BeginOnMainThread()
            {
                if (finished) return;
                if (cancellation.IsCancellationRequested)
                {
                    finished = true;
                    completion.TrySetCanceled(cancellation);
                    return;
                }

                try
                {
                    download = new BoundedDownloadHandler(request.MaxResponseBytes);
                    webRequest = new UnityWebRequest(request.Url.AbsoluteUri, UnityWebRequest.kHttpVerbGET)
                    {
                        downloadHandler = download,
                        redirectLimit = 0,
                        timeout = Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(request.Timeout.TotalSeconds)))
                    };
                    webRequest.SetRequestHeader("Accept", "application/json");
                    if (!string.IsNullOrEmpty(request.IfNoneMatch))
                        webRequest.SetRequestHeader("If-None-Match", request.IfNoneMatch);

                    registration = cancellation.Register(() => QueueAbort(false));
                    // Register invokes synchronously if cancellation won the race above.
                    if (finished)
                    {
                        registration.Dispose();
                        return;
                    }
                    timer = new Timer(_ => QueueAbort(true), null, request.Timeout,
                        System.Threading.Timeout.InfiniteTimeSpan);
                    var operation = webRequest.SendWebRequest();
                    operation.completed += _ => CompleteOnMainThread();
                    if (operation.isDone) CompleteOnMainThread();
                }
                catch (Exception exception)
                {
                    FailOnMainThread(exception);
                }
            }

            private void QueueAbort(bool timeout)
            {
                try
                {
                    dispatcher.Post(() =>
                    {
                        if (finished) return;
                        timedOut = timeout && !cancellation.IsCancellationRequested;
                        cancelled = !timedOut;
                        webRequest?.Abort();
                        CompleteOnMainThread();
                    });
                }
                catch (Exception exception)
                {
                    // The Unity context is shutting down. Never touch Unity objects here.
                    completion.TrySetException(new ConfigTransportException(
                        "Could not dispatch request cancellation: " + exception.Message));
                }
            }

            private void CompleteOnMainThread()
            {
                if (finished) return;
                finished = true;
                try
                {
                    if (cancelled || cancellation.IsCancellationRequested)
                        completion.TrySetCanceled(cancellation);
                    else if (timedOut)
                        completion.TrySetException(new ConfigTransportException("HTTP request timed out.", isTimeout: true));
                    else if (download.ExceededLimit)
                        completion.TrySetException(new ConfigTransportException(
                            "HTTP response exceeds the configured byte limit.", isRetryable: false));
                    else if (webRequest.responseCode >= 300 && webRequest.responseCode <= 399 &&
                             webRequest.responseCode != 304)
                    {
                        // With redirects disabled, Unity reports a received redirect as a
                        // connection error. Preserve its HTTP status so the coordinator
                        // rejects it once rather than treating it as a transient outage.
                        completion.TrySetResult(new ConfigHttpResponse(
                            checked((int)webRequest.responseCode), string.Empty,
                            webRequest.GetResponseHeader("ETag"), webRequest.GetResponseHeader("Retry-After")));
                    }
                    else if (webRequest.result == UnityWebRequest.Result.ConnectionError)
                        completion.TrySetException(new ConfigTransportException(
                            webRequest.error ?? "HTTP connection failed.",
                            isTimeout: (webRequest.error ?? "").IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0));
                    else if (webRequest.result == UnityWebRequest.Result.DataProcessingError)
                        completion.TrySetException(new ConfigTransportException(
                            webRequest.error ?? "HTTP response processing failed.", isRetryable: false));
                    else
                    {
                        var status = checked((int)webRequest.responseCode);
                        var body = status == 200 ? download.ReadUtf8() : string.Empty;
                        completion.TrySetResult(new ConfigHttpResponse(status, body,
                            webRequest.GetResponseHeader("ETag"), webRequest.GetResponseHeader("Retry-After")));
                    }
                }
                catch (DecoderFallbackException)
                {
                    completion.TrySetException(new ConfigTransportException(
                        "HTTP response is not valid UTF-8.", isRetryable: false));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
                finally
                {
                    ReleaseOnMainThread();
                }
            }

            private void FailOnMainThread(Exception exception)
            {
                if (finished) return;
                finished = true;
                completion.TrySetException(exception is ConfigTransportException ? exception
                    : new ConfigTransportException("Could not start HTTP request: " + exception.Message, isRetryable: false));
                ReleaseOnMainThread();
            }

            private void ReleaseOnMainThread()
            {
                timer?.Dispose();
                registration.Dispose();
                webRequest?.Dispose();
                download?.ReleaseManagedBuffer();
            }
        }

        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private readonly int maximumBytes;
            private readonly MemoryStream bytes;
            internal bool ExceededLimit { get; private set; }

            internal BoundedDownloadHandler(int maximumBytes) : base(new byte[8192])
            {
                this.maximumBytes = maximumBytes;
                bytes = new MemoryStream(Math.Min(maximumBytes, 8192));
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)maximumBytes) ExceededLimit = true;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (ExceededLimit) return false;
                if (dataLength == 0) return true;
                if (data == null || dataLength < 0 || dataLength > data.Length) return false;
                if (bytes.Length > maximumBytes - dataLength)
                {
                    ExceededLimit = true;
                    return false;
                }
                bytes.Write(data, 0, dataLength);
                return true;
            }

            internal string ReadUtf8() => new UTF8Encoding(false, true).GetString(bytes.ToArray());
            internal void ReleaseManagedBuffer() => bytes.Dispose();
        }
    }
}
