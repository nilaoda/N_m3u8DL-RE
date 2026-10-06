using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Common.Util;

public static class HTTPUtil
{
    public static readonly SocketsHttpHandler HttpHandler = new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        SslOptions = new SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
        },
        MaxConnectionsPerServer = 1024,
    };

    private static readonly CookieFileHandler CookieHandler = new(HttpHandler);

    public static readonly HttpClient AppHttpClient = new(CookieHandler)
    {
        Timeout = TimeSpan.FromSeconds(100),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
    };

    public static HttpRequestMessage CreateRequest(HttpMethod method, string url, HttpClient? client = null)
    {
        client ??= AppHttpClient;
        // SendAsync 不会把 HttpClient 的默认版本复制到手动创建的请求，必须显式设置。
        // 默认在 HTTPS 上优先协商 HTTP/2，服务端不支持时回退 HTTP/1.1。
        return new HttpRequestMessage(method, url)
        {
            Version = client.DefaultRequestVersion,
            VersionPolicy = client.DefaultVersionPolicy,
        };
    }

    public static void ConfigureCookies(string? path)
    {
        if (path == null)
            return;
        var cookies = NetscapeCookieFile.Load(path);
        // 文件模式由外层统一匹配和更新 Cookie，避免底层把容器内容追加到手动 Cookie 请求头。
        // 未指定文件时仍由 SocketsHttpHandler 处理 Cookie，保持原有行为。
        HttpHandler.UseCookies = false;
        CookieHandler.Cookies = cookies;
    }

    public static void ConfigureNetworkInterface(string? value)
    {
        if (value == null)
            return;
        HttpHandler.ConnectCallback = NetworkInterfaceBinding.Create(value).ConnectAsync;
    }

    private static async Task<HttpResponseMessage> DoGetAsync(string url, Dictionary<string, string>? headers = null,
        bool identityEncoding = false, int redirectCount = 0, CancellationToken cancellationToken = default)
    {
        if (redirectCount > 10)
        {
            throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, ResString.httpTooManyRedirects);
        }
        Logger.Debug(ResString.fetch + url);
        using var webRequest = CreateRequest(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", identityEncoding ? "identity" : "gzip, deflate");
        webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
        webRequest.Headers.Connection.Clear();
        if (headers != null)
        {
            foreach (var item in headers)
            {
                webRequest.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
        }

        Logger.Debug(webRequest.Headers.ToString());
        // 手动处理跳转，以免自定义Headers丢失
        var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (((int)webResponse.StatusCode).ToString().StartsWith("30"))
        {
            HttpResponseHeaders respHeaders = webResponse.Headers;
            Logger.Debug(respHeaders.ToString());
            if (respHeaders.Location != null)
            {
                var redirectedUrl = "";
                if (!respHeaders.Location.IsAbsoluteUri)
                {
                    Uri uri1 = new Uri(url);
                    Uri uri2 = new Uri(uri1, respHeaders.Location);
                    redirectedUrl = uri2.ToString();
                }
                else
                {
                    redirectedUrl = respHeaders.Location.AbsoluteUri;
                }

                if (redirectedUrl != url)
                {
                    Logger.Extra($"Redirected => {redirectedUrl}");
                    webResponse.Dispose();
                    return await DoGetAsync(redirectedUrl, headers, identityEncoding, redirectCount + 1, cancellationToken);
                }
            }
        }

        // 手动将跳转后的URL设置进去, 用于后续取用
        webResponse.Headers.Location = new Uri(url);
        try
        {
            webResponse.EnsureSuccessStatusCode();
        }
        catch
        {
            webResponse.Dispose();
            throw;
        }
        return webResponse;
    }

    public static async Task<byte[]> GetBytesAsync(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default, TimeSpan? requestTimeout = null)
    {
        if (url.StartsWith("file:"))
        {
            return await File.ReadAllBytesAsync(new Uri(url).LocalPath, cancellationToken);
        }

        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (requestTimeout is { } timeout)
            readTimeout.CancelAfter(timeout);
        // 直播 key 从发起请求到读完共用一个计时，重定向也不能延长等待。
        using var webResponse = await DoGetAsync(url, headers, cancellationToken: readTimeout.Token);
        if (requestTimeout == null && AppHttpClient.Timeout != Timeout.InfiniteTimeSpan)
            readTimeout.CancelAfter(AppHttpClient.Timeout);
        var bytes = await webResponse.Content.ReadAsByteArrayAsync(readTimeout.Token);
        Logger.Debug(HexUtil.BytesToHex(bytes, " "));
        return bytes;
    }

    /// <summary>
    /// 获取网页源码
    /// </summary>
    /// <param name="url"></param>
    /// <param name="headers"></param>
    /// <returns></returns>
    public static async Task<string> GetWebSourceAsync(string url, Dictionary<string, string>? headers = null)
    {
        var webResponse = await DoGetAsync(url, headers);
        string htmlCode = await webResponse.Content.ReadAsStringAsync();
        Logger.Debug(htmlCode);
        return htmlCode;
    }

    /// <summary>
    /// 获取网页源码和跳转后的URL
    /// </summary>
    /// <param name="url"></param>
    /// <param name="headers"></param>
    /// <returns>(Source Code, RedirectedUrl)</returns>
    public static async Task<(string, string)> GetWebSourceAndNewUrlAsync(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default, TimeSpan? requestTimeout = null)
    {
        using var result = await GetWebSourceResultAsync(url, headers, cancellationToken, requestTimeout);
        return (result.Source, result.Url);
    }

    /// <summary>
    /// 读取少量响应内容判断格式。二进制响应由调用者负责释放，避免直录时再次请求 URL。
    /// </summary>
    public static async Task<WebSourceResult> GetWebSourceResultAsync(string url, Dictionary<string, string>? headers = null, CancellationToken cancellationToken = default, TimeSpan? requestTimeout = null)
    {
        using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (requestTimeout is { } timeout)
            readTimeout.CancelAfter(timeout);
        // 仅直播刷新传入独立超时；初始解析和点播保留全局请求、正文分别计时的行为。
        var webResponse = await DoGetAsync(url, headers, identityEncoding: true, cancellationToken: readTimeout.Token);
        try
        {
            // 打开流，读取少量样本检测类型
            const int sampleSize = 4096;
            const int minimumSampleSize = 188 * 3;
            var responseStream = await webResponse.Content.ReadAsStreamAsync();
            var buffer = new byte[sampleSize];
            if (requestTimeout == null && AppHttpClient.Timeout != Timeout.InfiniteTimeSpan)
            {
                readTimeout.CancelAfter(AppHttpClient.Timeout);
            }
            var bytesRead = await responseStream.ReadAtLeastAsync(buffer.AsMemory(0, sampleSize),
                minimumSampleSize, throwOnEndOfStream: false, cancellationToken: readTimeout.Token);
            var sample = buffer.AsSpan(0, bytesRead);
            var resolvedUrl = webResponse.RequestMessage?.RequestUri?.AbsoluteUri ?? url;

            if (BinaryContentCheckUtil.IsMpeg2TsBuffer(sample))
            {
                Logger.Debug("Detected MPEG-TS stream");
                return new WebSourceResult(ResString.ReLiveTs, resolvedUrl, webResponse, responseStream, buffer[..bytesRead]);
            }

            var encoding = GetEncodingFromBom(sample) ?? GetEncodingFromResponse(webResponse) ?? Encoding.UTF8;
            var prefix = encoding.GetString(sample).TrimStart('\uFEFF', ' ', '\r', '\n', '\t');
            var isManifest = prefix.StartsWith("#EXTM3U", StringComparison.Ordinal) ||
                             prefix.Contains("<MPD", StringComparison.Ordinal) ||
                             prefix.Contains("<SmoothStreamingMedia", StringComparison.Ordinal);
            var mediaType = webResponse.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            var binaryMediaType = mediaType is not null &&
                                  (mediaType.StartsWith("video/") || mediaType.StartsWith("audio/") ||
                                   mediaType.StartsWith("image/") || mediaType is "application/octet-stream" or
                                   "application/pdf" or "application/zip");
            if (!isManifest && (bytesRead == 0 || BinaryContentCheckUtil.LooksLikeBinary(sample) || binaryMediaType))
            {
                Logger.Debug("Detected binary data");
                return new WebSourceResult(ResString.ReBinaryData, resolvedUrl, webResponse, responseStream, buffer[..bytesRead]);
            }

            // 文本播放列表完整读取
            using var ms = new MemoryStream();
            ms.Write(buffer, 0, bytesRead);
            await responseStream.CopyToAsync(ms, readTimeout.Token);
            var htmlCode = encoding.GetString(ms.ToArray()).TrimStart('\uFEFF');
            webResponse.Dispose();
            return new WebSourceResult(htmlCode, resolvedUrl);
        }
        catch
        {
            webResponse.Dispose();
            throw;
        }
    }

    private static Encoding? GetEncodingFromBom(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xFE && data[2] == 0x00 && data[3] == 0x00)
        {
            return Encoding.UTF32;
        }
        if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
        {
            return new UTF32Encoding(bigEndian: true, byteOrderMark: true);
        }
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            return Encoding.UTF8;
        }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
        {
            return Encoding.Unicode;
        }
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }
        return null;
    }

    private static Encoding? GetEncodingFromResponse(HttpResponseMessage response)
    {
        var contentType = response.Content.Headers.ContentType;
        if (contentType?.CharSet == null) return null;
        
        try
        {
            return Encoding.GetEncoding(contentType.CharSet);
        }
        catch (ArgumentException)
        {
            // 无效 charset，回退
        }

        return null;
    }

    public static async Task<string> GetPostResponseAsync(string Url, byte[] postData)
    {
        string htmlCode;
        using var request = CreateRequest(HttpMethod.Post, Url);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("Content-Length", postData.Length.ToString());
        request.Content = new ByteArrayContent(postData);
        var webResponse = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        htmlCode = await webResponse.Content.ReadAsStringAsync();
        return htmlCode;
    }
}
