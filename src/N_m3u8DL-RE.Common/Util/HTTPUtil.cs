using System.Net;
using System.Net.Http.Headers;
using System.Text;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util;

public static class HTTPUtil
{
    public static readonly HttpClientHandler HttpClientHandler = new()
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
        ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
        MaxConnectionsPerServer = 1024,
    };

    public static readonly HttpClient AppHttpClient = new(HttpClientHandler)
    {
        Timeout = TimeSpan.FromSeconds(100),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
    };

    private static async Task<HttpResponseMessage> DoGetAsync(string url, Dictionary<string, string>? headers = null)
    {
        Logger.Debug(ResString.fetch + url);
        using var webRequest = new HttpRequestMessage(HttpMethod.Get, url);
        webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
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
        var webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead);
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
                    return await DoGetAsync(redirectedUrl, headers);
                }
            }
        }

        // 手动将跳转后的URL设置进去, 用于后续取用
        webResponse.Headers.Location = new Uri(url);
        webResponse.EnsureSuccessStatusCode();
        return webResponse;
    }

    public static async Task<byte[]> GetBytesAsync(string url, Dictionary<string, string>? headers = null)
    {
        if (url.StartsWith("file:"))
        {
            return await File.ReadAllBytesAsync(new Uri(url).LocalPath);
        }

        var webResponse = await DoGetAsync(url, headers);
        var bytes = await webResponse.Content.ReadAsByteArrayAsync();
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
    public static async Task<(string, string)> GetWebSourceAndNewUrlAsync(string url, Dictionary<string, string>? headers = null)
    {
        var webResponse = await DoGetAsync(url, headers);
        var htmlCode = "";

        // 如果响应是压缩的（gzip/deflate/br），直接按文本处理
        var encodings = webResponse.Content.Headers.ContentEncoding;
        if (encodings.Count != 0)
        {
            Logger.Debug($"Detected compression: {string.Join(",", encodings)}");
            htmlCode = await webResponse.Content.ReadAsStringAsync();
            return (htmlCode, webResponse.RequestMessage?.RequestUri?.AbsoluteUri ?? url);
        }

        // 打开流，读取少量样本检测类型
        const int sampleSize = 4096;
        await using var responseStream = await webResponse.Content.ReadAsStreamAsync();

        var buffer = new byte[sampleSize];
        var bytesRead = await responseStream.ReadAsync(buffer.AsMemory(0, sampleSize));

        // MPEG-TS 检测
        if (BinaryContentCheckUtil.IsMpeg2TsBuffer(buffer.AsSpan(0, bytesRead)))
        {
            Logger.Debug("Detected MPEG-TS stream");
            return (ResString.ReLiveTs, url);
        }

        // 启发式判断二进制
        if (BinaryContentCheckUtil.LooksLikeBinary(buffer.AsSpan(0, bytesRead)))
        {
            Logger.Debug("Heuristic detection: binary data");
            return (ResString.ReBinaryData, url);
        }

        // 否则是文本，完整读取
        using var ms = new MemoryStream();
        ms.Write(buffer, 0, bytesRead);
        await responseStream.CopyToAsync(ms);

        var allBytes = ms.ToArray();
        var encoding = GetEncodingFromResponse(webResponse) ?? Encoding.UTF8;
        htmlCode = encoding.GetString(allBytes);

        return (htmlCode, webResponse.RequestMessage?.RequestUri?.AbsoluteUri ?? url);
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
        using HttpRequestMessage request = new(HttpMethod.Post, Url);
        request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
        request.Headers.TryAddWithoutValidation("Content-Length", postData.Length.ToString());
        request.Content = new ByteArrayContent(postData);
        var webResponse = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        htmlCode = await webResponse.Content.ReadAsStringAsync();
        return htmlCode;
    }
}