using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using System.Net;
using System.Net.Http.Headers;

namespace N_m3u8DL_RE.Util
{
    internal class DownloadUtil
    {
        private static readonly HttpClient AppHttpClient = HTTPUtil.AppHttpClient;

        private static async Task<DownloadResult> CopyFileAsync(string sourceFile, string path, SpeedContainer speedContainer, long? fromPosition = null, long? toPosition = null)
        {
            using var inputStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var outputStream = new FileStream(path, FileMode.OpenOrCreate);
            inputStream.Seek(fromPosition ?? 0L, SeekOrigin.Begin);
            var expect = (toPosition ?? inputStream.Length) - inputStream.Position + 1;
            if (expect == inputStream.Length + 1)
            {
                await inputStream.CopyToAsync(outputStream);
                speedContainer.Add(inputStream.Length);
            }
            else
            {
                var buffer = new byte[expect];
                await inputStream.ReadAsync(buffer);
                await outputStream.WriteAsync(buffer, 0, buffer.Length);
                speedContainer.Add(buffer.Length);
            }
            return new DownloadResult()
            {
                ActualContentLength = outputStream.Length,
                ActualFilePath = path
            };
        }

        public static async Task<DownloadResult> DownloadToFileAsync(string url, string path, SpeedContainer speedContainer, Dictionary<string, string>? headers = null, long? fromPosition = null, long? toPosition = null)
        {
            Logger.Debug(ResString.fetch + url);
            if (url.StartsWith("file:"))
            {
                var file = new Uri(url).LocalPath;
                return await CopyFileAsync(file, path, speedContainer, fromPosition, toPosition);
            }
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
            CancellationTokenSource cancellationTokenSource = new(); //取消下载
            using var watcher = Task.Factory.StartNew(async () =>
            {
                while (true)
                {
                    if (speedContainer == null) break;
                    if (speedContainer.ShouldStop)
                    {
                        cancellationTokenSource.Cancel();
                        speedContainer.ResetLowSpeedCount();
                        Logger.DebugMarkUp("Cancel...");
                        break;
                    }
                    await Task.Delay(500);
                }
            });
            try
            {
                using var response = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token);
                if (response.StatusCode == HttpStatusCode.Found || response.StatusCode == HttpStatusCode.Moved || response.StatusCode == HttpStatusCode.SeeOther)
                {
                    HttpResponseHeaders respHeaders = response.Headers;
                    Logger.Debug(respHeaders.ToString());
                    if (respHeaders != null && respHeaders.Location != null)
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
                        return await DownloadToFileAsync(redirectedUrl, path, speedContainer, headers, fromPosition, toPosition);
                    }
                }
                response.EnsureSuccessStatusCode();
                var contentLength = response.Content.Headers.ContentLength;
                if (speedContainer.SingleSegment) speedContainer.ResponseLength = contentLength;

                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationTokenSource.Token);
                var buffer = new byte[16 * 1024];
                var size = 0;
                while ((size = await responseStream.ReadAsync(buffer, cancellationTokenSource.Token)) > 0)
                {
                    speedContainer.Add(size);
                    await stream.WriteAsync(buffer, 0, size);
                }

                return new DownloadResult()
                {
                    ActualContentLength = stream.Length,
                    RespContentLength = contentLength,
                    ActualFilePath = path
                };
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == cancellationTokenSource.Token)
            {
                throw new Exception("Download speed too slow!");
            }
        }
    }
}
