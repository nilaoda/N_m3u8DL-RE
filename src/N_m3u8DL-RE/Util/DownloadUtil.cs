using System.Net.Http.Headers;

using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util
{
    internal static class DownloadUtil
    {
        private static readonly HttpClient AppHttpClient = HTTPUtil.AppHttpClient;

        private static async Task<DownloadResult> CopyFileAsync(string sourceFile, string path, SpeedContainer speedContainer, long? fromPosition = null, long? toPosition = null)
        {
            using FileStream inputStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            using FileStream outputStream = new FileStream(path, FileMode.OpenOrCreate);
            inputStream.Seek(fromPosition ?? 0L, SeekOrigin.Begin);
            long expect = (toPosition ?? inputStream.Length) - inputStream.Position + 1;
            if (expect == inputStream.Length + 1)
            {
                await inputStream.CopyToAsync(outputStream);
                speedContainer.Add(inputStream.Length);
            }
            else
            {
                byte[] buffer = new byte[expect];
                _ = await inputStream.ReadAsync(buffer);
                await outputStream.WriteAsync(buffer);
                speedContainer.Add(buffer.Length);
            }
            return new DownloadResult()
            {
                ActualContentLength = outputStream.Length,
                ActualFilePath = path
            };
        }

        public static async Task<DownloadResult> DownloadToFileAsync(string url, string path, SpeedContainer speedContainer, CancellationTokenSource cancellationTokenSource, Dictionary<string, string>? headers = null, long? fromPosition = null, long? toPosition = null)
        {
            Logger.Debug(ResString.fetch + url);
            if (url.StartsWith("file:"))
            {
                string file = new Uri(url).LocalPath;
                return await CopyFileAsync(file, path, speedContainer, fromPosition, toPosition);
            }
            if (url.StartsWith("base64://"))
            {
                byte[] bytes = Convert.FromBase64String(url[9..]);
                await File.WriteAllBytesAsync(path, bytes);
                return new DownloadResult()
                {
                    ActualContentLength = bytes.Length,
                    ActualFilePath = path,
                };
            }
            if (url.StartsWith("hex://"))
            {
                byte[] bytes = HexUtil.HexToBytes(url[6..]);
                await File.WriteAllBytesAsync(path, bytes);
                return new DownloadResult()
                {
                    ActualContentLength = bytes.Length,
                    ActualFilePath = path,
                };
            }
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
            if (fromPosition != null || toPosition != null)
            {
                request.Headers.Range = new(fromPosition, toPosition);
            }

            if (headers != null)
            {
                foreach (KeyValuePair<string, string> item in headers)
                {
                    request.Headers.TryAddWithoutValidation(item.Key, item.Value);
                }
            }
            Logger.Debug(request.Headers.ToString());
            try
            {
                using HttpResponseMessage response = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationTokenSource.Token);
                if (((int)response.StatusCode).ToString().StartsWith("30"))
                {
                    HttpResponseHeaders respHeaders = response.Headers;
                    Logger.Debug(respHeaders.ToString());
                    if (respHeaders.Location != null)
                    {
                        string redirectedUrl = "";
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
                        return await DownloadToFileAsync(redirectedUrl, path, speedContainer, cancellationTokenSource, headers, fromPosition, toPosition);
                    }
                }
                response.EnsureSuccessStatusCode();
                long? contentLength = response.Content.Headers.ContentLength;
                if (speedContainer.SingleSegment)
                {
                    speedContainer.ResponseLength = contentLength;
                }

                using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationTokenSource.Token);
                byte[] buffer = new byte[16 * 1024];
                int size = 0;

                size = await responseStream.ReadAsync(buffer, cancellationTokenSource.Token);
                speedContainer.Add(size);
                await stream.WriteAsync(buffer.AsMemory(0, size));
                // 检测imageHeader
                bool imageHeader = ImageHeaderUtil.IsImageHeader(buffer);
                // 检测GZip（For DDP Audio）
                bool gZipHeader = buffer.Length > 2 && buffer[0] == 0x1f && buffer[1] == 0x8b;

                while ((size = await responseStream.ReadAsync(buffer, cancellationTokenSource.Token)) > 0)
                {
                    speedContainer.Add(size);
                    await stream.WriteAsync(buffer.AsMemory(0, size));
                    // 限速策略
                    while (speedContainer.Downloaded > speedContainer.SpeedLimit)
                    {
                        await Task.Delay(1);
                    }
                }

                return new DownloadResult()
                {
                    ActualContentLength = stream.Length,
                    RespContentLength = contentLength,
                    ActualFilePath = path,
                    ImageHeader = imageHeader,
                    GzipHeader = gZipHeader
                };
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == cancellationTokenSource.Token)
            {
                speedContainer.ResetLowSpeedCount();
                throw new Exception("Download speed too slow!");
            }
        }
    }
}