using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BijiSDK.Samples
{
    /// <summary>Sample-only mapping from the delivery API to static HTTPS test fixtures.</summary>
    public sealed class PagesConfigTransport : IConfigTransport
    {
        private readonly IConfigTransport inner;
        private readonly Uri expectedRequest;
        private readonly Uri fixture;

        public PagesConfigTransport(IConfigTransport inner, Uri pagesBaseUri, ConfigOptions context)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
            if (context == null) throw new ArgumentNullException(nameof(context));
            var remote = new ConfigRemoteOptions(pagesBaseUri);
            string contract = context.ConfigContractVersion.ToString(CultureInfo.InvariantCulture);
            string project = Uri.EscapeDataString(context.ProjectId);
            string environment = Uri.EscapeDataString(context.Environment);
            string platform = Uri.EscapeDataString(context.Platform);
            expectedRequest = new Uri(remote.BaseUri, "v1/projects/" + project +
                "/environments/" + environment + "/config?platform=" + platform +
                "&appBuild=" + context.AppBuild.ToString(CultureInfo.InvariantCulture) +
                "&configContractVersion=" + contract);
            fixture = new Uri(remote.BaseUri, "configs/" + project + "/" + environment +
                "/" + platform + "/contract-" + contract + "/config.json");
        }

        public Task<ConfigHttpResponse> GetAsync(ConfigHttpRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // This decorator must never reroute a request for another context or origin.
            if (!string.Equals(request.Url.AbsoluteUri, expectedRequest.AbsoluteUri, StringComparison.Ordinal))
                throw new ConfigTransportException("The Pages sample received a different endpoint or context.",
                    isRetryable: false);
            cancellationToken.ThrowIfCancellationRequested();
            return inner.GetAsync(new ConfigHttpRequest(fixture, request.IfNoneMatch,
                request.Timeout, request.MaxResponseBytes), cancellationToken);
        }
    }
}
