using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Common.Util;

internal sealed class HttpResponseLoggingHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);
        Logger.Debug($"HTTP/{response.Version} {(int)response.StatusCode} {request.Method} {request.RequestUri}");
        return response;
    }
}
