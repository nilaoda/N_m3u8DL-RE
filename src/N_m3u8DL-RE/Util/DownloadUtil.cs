using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using System.Net.Http.Headers;

namespace N_m3u8DL_RE.Util;

internal static class DownloadUtil
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

    public static async Task<DownloadResult> DownloadToFileAsync(string url, string path, SpeedContainer speedContainer, CancellationTokenSource cancellationTokenSource, Dictionary<string, string>? headers = null, long? fromPosition = null, long? toPosition = null, TimeSpan? networkTimeout = null)
    {
        using var requestTimeout = networkTimeout == null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);
        if (networkTimeout is { } timeout)
            requestTimeout!.CancelAfter(timeout);
        try
        {
            return await DownloadToFileCoreAsync(url, path, speedContainer, cancellationTokenSource, headers, fromPosition,
                toPosition, requestTimeout, networkTimeout);
        }
        catch (OperationCanceledException) when (!cancellationTokenSource.IsCancellationRequested && requestTimeout is { IsCancellationRequested: true })
        {
            throw new TimeoutException(ResString.liveNetworkTimeout);
        }
    }

    private static async Task<int> ReadResponseAsync(Stream stream, Memory<byte> buffer, CancellationTokenSource? requestTimeout,
        TimeSpan? networkTimeout, CancellationToken cancellationToken)
    {
        if (requestTimeout == null || networkTimeout is not { } timeout)
            return await stream.ReadAsync(buffer, cancellationToken);
        // 只限制连续收不到数据的等待；本地写盘、限速等待和下载总时长不计入网络超时。
        requestTimeout.CancelAfter(timeout);
        try
        {
            return await stream.ReadAsync(buffer, requestTimeout.Token);
        }
        finally
        {
            requestTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
        }
    }

    private static async Task<DownloadResult> DownloadToFileCoreAsync(string url, string path, SpeedContainer speedContainer,
        CancellationTokenSource cancellationTokenSource, Dictionary<string, string>? headers, long? fromPosition, long? toPosition,
        CancellationTokenSource? requestTimeout, TimeSpan? networkTimeout, int redirectCount = 0)
    {
        // 跳转超限属于 URL 配置错误，不能进入直播的持续网络恢复循环。
        if (redirectCount > 10)
            throw new HttpRequestException(HttpRequestError.ConfigurationLimitExceeded, ResString.httpTooManyRedirects);
        Logger.Debug(ResString.fetch + url);
        if (url.StartsWith("file:"))
        {
            var file = new Uri(url).LocalPath;
            return await CopyFileAsync(file, path, speedContainer, fromPosition, toPosition);
        }
        if (url.StartsWith("base64://"))
        {
            var bytes = Convert.FromBase64String(url[9..]);
            await File.WriteAllBytesAsync(path, bytes);
            return new DownloadResult()
            {
                ActualContentLength = bytes.Length,
                ActualFilePath = path,
            };
        }
        if (url.StartsWith("hex://"))
        {
            var bytes = HexUtil.HexToBytes(url[6..]);
            await File.WriteAllBytesAsync(path, bytes);
            return new DownloadResult()
            {
                ActualContentLength = bytes.Length,
                ActualFilePath = path,
            };
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
        try
        {
            // 跳转链共用响应头等待计时，不因每次重定向重新获得一份超时预算。
            var requestToken = requestTimeout?.Token ?? cancellationTokenSource.Token;
            using var response = await AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
            if (((int)response.StatusCode).ToString().StartsWith("30"))
            {
                HttpResponseHeaders respHeaders = response.Headers;
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
                    return await DownloadToFileCoreAsync(redirectedUrl, path, speedContainer, cancellationTokenSource, headers,
                        fromPosition, toPosition, requestTimeout, networkTimeout, redirectCount + 1);
                }
            }
            response.EnsureSuccessStatusCode();
            requestTimeout?.CancelAfter(Timeout.InfiniteTimeSpan);
            var contentLength = response.Content.Headers.ContentLength;
            if (speedContainer.SingleSegment) speedContainer.ResponseLength = contentLength;

            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var responseStream = await response.Content.ReadAsStreamAsync(requestToken);
            var buffer = new byte[16 * 1024];
            var size = 0;

            size = await ReadResponseAsync(responseStream, buffer, requestTimeout, networkTimeout, cancellationTokenSource.Token);
            speedContainer.Add(size);
            await stream.WriteAsync(buffer.AsMemory(0, size));
            // 检测imageHeader
            bool imageHeader = ImageHeaderUtil.IsImageHeader(buffer);
            // 检测GZip（For DDP Audio）
            bool gZipHeader = buffer.Length > 2 && buffer[0] == 0x1f && buffer[1] == 0x8b;

            while ((size = await ReadResponseAsync(responseStream, buffer, requestTimeout, networkTimeout, cancellationTokenSource.Token)) > 0)
            {
                speedContainer.Add(size);
                await stream.WriteAsync(buffer.AsMemory(0, size));
                // 限速策略
                while (speedContainer.Downloaded > speedContainer.SpeedLimit)
                {
                    await Task.Delay(1, cancellationTokenSource.Token);
                }
            }

            return new DownloadResult()
            {
                ActualContentLength = stream.Length,
                RespContentLength = contentLength,
                ActualFilePath = path,
                ImageHeader= imageHeader,
                GzipHeader = gZipHeader
            };
        }
        catch (OperationCanceledException oce) when (oce.CancellationToken == cancellationTokenSource.Token)
        {
            speedContainer.ResetLowSpeedCount();
            throw new TimeoutException("Download speed too slow!");
        }
    }
}