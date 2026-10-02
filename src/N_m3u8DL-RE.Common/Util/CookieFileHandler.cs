using System.Net;

namespace N_m3u8DL_RE.Common.Util;

internal sealed class CookieFileHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    internal CookieFileJar? Cookies { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var cookies = Cookies;
        var uri = request.RequestUri!;
        // 手动指定 Cookie 时完全以请求头为准；每次跳转都会用新 URL 重新匹配文件中的 Cookie。
        if (cookies != null && !request.Headers.Contains("Cookie"))
        {
            var header = cookies.GetCookieHeader(uri);
            if (header.Length > 0)
                request.Headers.TryAddWithoutValidation("Cookie", header);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (cookies != null && response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            // 包括 302 响应；服务器更新的会话 Cookie 也要用于后续清单、密钥和分片请求。
            foreach (var value in values)
            {
                try
                {
                    cookies.SetCookies(uri, value);
                }
                catch (CookieException)
                {
                    // 与 SocketsHttpHandler 一致，忽略服务器返回的无效 Cookie。
                }
            }
        }
        return response;
    }
}
