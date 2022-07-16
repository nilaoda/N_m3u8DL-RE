using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    internal class DownloadUtil
    {
        private static readonly HttpClient AppHttpClient = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };

        public static async Task<DownloadResult> DownloadToFileAsync(string url, string path, Dictionary<string, string>? headers = null, long? fromPosition = null, long? toPosition = null)
        {
            Logger.Debug(ResString.fetch + url);
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            if (fromPosition != null || toPosition != null)
                request.Headers.Range = new(fromPosition, toPosition);
            if (headers != null)
            {
                foreach (var item in headers)
                {
                    request.Headers.TryAddWithoutValidation(item.Key, item.Value);
                }
            }
            Logger.Debug(request.Headers.ToString());
            using var response = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.Moved)
            {
                HttpResponseHeaders respHeaders = response.Headers;
                Logger.Debug(respHeaders.ToString());
                if (respHeaders != null && respHeaders.Location != null)
                {
                    var redirectedUrl = respHeaders.Location.AbsoluteUri;
                    return await DownloadToFileAsync(redirectedUrl, path, headers);
                }
            }
            response.EnsureSuccessStatusCode();
            var contentLength = response.Content.Headers.ContentLength;
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var responseStream = await response.Content.ReadAsStreamAsync();
            var buffer = new byte[16 * 1024];
            var size = 0;
            while ((size = await responseStream.ReadAsync(buffer)) > 0)
            {
                await stream.WriteAsync(buffer, 0, size);
            }

            return new DownloadResult()
            {
                ActualContentLength = stream.Length,
                RespContentLength = contentLength,
                ActualFilePath = path
            };
        }

        /// <summary>
        /// 输入一堆已存在的文件，合并到新文件
        /// </summary>
        /// <param name="files"></param>
        /// <param name="outputFilePath"></param>
        public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath)
        {
            if (files.Length == 0) return;
            if (files.Length == 1)
            {
                FileInfo fi = new FileInfo(files[0]);
                fi.CopyTo(outputFilePath, true);
                return;
            }

            if (!Directory.Exists(Path.GetDirectoryName(outputFilePath)))
                Directory.CreateDirectory(Path.GetDirectoryName(outputFilePath)!);

            string[] inputFilePaths = files;
            using (var outputStream = File.Create(outputFilePath))
            {
                foreach (var inputFilePath in inputFilePaths)
                {
                    if (inputFilePath == "")
                        continue;
                    using (var inputStream = File.OpenRead(inputFilePath))
                    {
                        inputStream.CopyTo(outputStream);
                    }
                }
            }
        }
    }
}
