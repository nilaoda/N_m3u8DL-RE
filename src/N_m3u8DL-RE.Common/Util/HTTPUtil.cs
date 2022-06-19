using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util
{
    public class HTTPUtil
    {

        public static readonly HttpClient AppHttpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        })
        {
            Timeout = TimeSpan.FromMinutes(2)
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
            var webResponse = (await AppHttpClient.SendAsync(webRequest, HttpCompletionOption.ResponseHeadersRead)).EnsureSuccessStatusCode();
            return webResponse;
        }

        public static async Task<byte[]> GetBytesAsync(string url, Dictionary<string, string>? headers = null)
        {
            byte[] bytes = new byte[0];
            var webResponse = await DoGetAsync(url, headers);
            bytes = await webResponse.Content.ReadAsByteArrayAsync();
            Logger.Debug(HexUtil.BytesToHex(bytes, " "));
            return bytes;
        }

        public static async Task<string> GetWebSourceAsync(string url, Dictionary<string, string>? headers = null)
        {
            string htmlCode = string.Empty;
            var webResponse = await DoGetAsync(url, headers);
            htmlCode = await webResponse.Content.ReadAsStringAsync();
            Logger.Debug(htmlCode);
            return htmlCode;
        }

        public static async Task<string> GetPostResponseAsync(string Url, byte[] postData)
        {
            string htmlCode = string.Empty;
            using HttpRequestMessage request = new(HttpMethod.Post, Url);
            request.Headers.TryAddWithoutValidation("Content-Type", "application/json");
            request.Headers.TryAddWithoutValidation("Content-Length", postData.Length.ToString());
            request.Content = new ByteArrayContent(postData);
            var webResponse = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            htmlCode = await webResponse.Content.ReadAsStringAsync();
            return htmlCode;
        }
    }
}
