using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util
{
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
            Logger.Debug(ResString.Fetch + url);
            using HttpRequestMessage webRequest = new(HttpMethod.Get, url);
            _ = webRequest.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate");
            webRequest.Headers.CacheControl = CacheControlHeaderValue.Parse("no-cache");
            webRequest.Headers.Connection.Clear();
            if (headers != null)
            {
                foreach (KeyValuePair<string, string> item in headers)
                {
                    _ = webRequest.Headers.TryAddWithoutValidation(item.Key, item.Value);
                }
            }
            Logger.Debug(webRequest.Headers.ToString());
            // 手动处理跳转，以免自定义Headers丢失
            HttpResponseMessage webResponse = await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead);
            if (((int)webResponse.StatusCode).ToString(CultureInfo.InvariantCulture).StartsWith("30", StringComparison.OrdinalIgnoreCase))
            {
                HttpResponseHeaders respHeaders = webResponse.Headers;
                Logger.Debug(respHeaders.ToString());
                if (respHeaders.Location != null)
                {
                    string redirectedUrl = "";
                    if (!respHeaders.Location.IsAbsoluteUri)
                    {
                        Uri uri1 = new(url);
                        Uri uri2 = new(uri1, respHeaders.Location);
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
            _ = webResponse.EnsureSuccessStatusCode();
            return webResponse;
        }

        public static async Task<byte[]> GetBytesAsync(string url, Dictionary<string, string>? headers = null)
        {
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return await File.ReadAllBytesAsync(new Uri(url).LocalPath);
            }
            HttpResponseMessage webResponse = await DoGetAsync(url, headers);
            byte[] bytes = await webResponse.Content.ReadAsByteArrayAsync();
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
            HttpResponseMessage webResponse = await DoGetAsync(url, headers);
            string htmlCode = await webResponse.Content.ReadAsStringAsync();
            Logger.Debug(htmlCode);
            return htmlCode;
        }

        private static bool CheckMPEG2TS(HttpResponseMessage? webResponse)
        {
            string? mediaType = webResponse?.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            return (webResponse?.Content.Headers.ContentLength) == null && mediaType is "video/ts" or "video/mp2t" or "video/mpeg" or "application/octet-stream";
        }

        /// <summary>
        /// 获取网页源码和跳转后的URL
        /// </summary>
        /// <param name="url"></param>
        /// <param name="headers"></param>
        /// <returns>(Source Code, RedirectedUrl)</returns>
        public static async Task<(string, string)> GetWebSourceAndNewUrlAsync(string url, Dictionary<string, string>? headers = null)
        {
            string htmlCode;
            HttpResponseMessage webResponse = await DoGetAsync(url, headers);
            htmlCode = CheckMPEG2TS(webResponse) ? ResString.ReLiveTs : await webResponse.Content.ReadAsStringAsync();
            Logger.Debug(htmlCode);
            return (htmlCode, webResponse.Headers.Location != null ? webResponse.Headers.Location.AbsoluteUri : url);
        }

        public static async Task<string> GetPostResponseAsync(string Url, byte[] postData)
        {
            string htmlCode;
            using HttpRequestMessage request = new(HttpMethod.Post, Url);
            _ = request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            _ = request.Headers.TryAddWithoutValidation("Content-Length", postData.Length.ToString(CultureInfo.InvariantCulture));
            request.Content = new ByteArrayContent(postData);
            HttpResponseMessage webResponse = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            htmlCode = await webResponse.Content.ReadAsStringAsync();
            return htmlCode;
        }
    }
}