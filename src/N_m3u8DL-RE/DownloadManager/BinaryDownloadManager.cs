using System.Net;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.DownloadManager;

/// <summary>
/// 直链文件下载：并发获取相邻的 Range 块，由一个写入者按偏移顺序写入文件。
/// 下载窗口有上限，避免先完成的远端块无限占用内存。
/// </summary>
internal sealed class BinaryDownloadManager
{
    private readonly HttpClient _client;
    private readonly int _blockSize;
    private readonly TimeSpan _readTimeout;
    private readonly int _flushThreshold;

    public BinaryDownloadManager(HttpClient? client = null, int blockSize = 2 * 1024 * 1024,
        TimeSpan? readTimeout = null, int flushThreshold = 32 * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(flushThreshold, 1);
        _client = client ?? HTTPUtil.AppHttpClient;
        _blockSize = blockSize;
        _flushThreshold = flushThreshold;
        _readTimeout = readTimeout ?? TimeSpan.FromSeconds(30);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_readTimeout, TimeSpan.Zero, nameof(readTimeout));
    }

    // MPD 中的整文件也复用同一下载器；直接 GET 并验证 Range，避免依赖 HEAD。
    public async Task DownloadAsync(string url, string outputPath, Dictionary<string, string> headers,
        int threadCount, int retryCount, Action<long?> onLength, Action<int> onReceived,
        Action<long> onDownloaded, CancellationToken cancellationToken = default, long? maxSpeed = null)
    {
        using var response = await SendAsync(url, headers, null, null, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        onLength(response.Content.Headers.ContentLength);
        using var source = new WebSourceResult("", response.RequestMessage?.RequestUri?.AbsoluteUri ?? url,
            response, stream);
        await DownloadAsync(source, outputPath, headers, threadCount, retryCount,
            cancellationToken: cancellationToken, maxSpeed: maxSpeed, onReceived: onReceived, onDownloaded: onDownloaded);
    }

    public async Task DownloadAsync(WebSourceResult source, string outputPath, Dictionary<string, string> headers,
        int threadCount, int retryCount, Action<long>? onWritten = null, CancellationToken cancellationToken = default,
        long? maxSpeed = null, Action<int>? onReceived = null, Action<long>? onDownloaded = null)
    {
        if (source.Response == null || source.Stream == null)
        {
            throw new ArgumentException("A binary HTTP response is required.", nameof(source));
        }
        ArgumentOutOfRangeException.ThrowIfLessThan(threadCount, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(retryCount);
        if (maxSpeed is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxSpeed.Value, 1L, nameof(maxSpeed));
        }
        if (File.Exists(outputPath))
        {
            throw new IOException($"Output file already exists: {outputPath}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        var temporaryPath = outputPath + ".downloading";
        var metadataPath = temporaryPath + ".meta";

        var expectedLength = source.Response.Content.Headers.ContentLength;
        var validator = GetValidator(source.Response);
        var limiter = maxSpeed is > 0 ? new BandwidthLimiter(maxSpeed.Value) : null;
        // 单线程也使用 Range 块重试，不能因并发数为 1 而在断流后重下整个文件。
        var supportsRanges = expectedLength > 0 &&
                             await SupportsRangesAsync(source.Url, expectedLength.Value, validator, headers, cancellationToken);
        if (supportsRanges)
        {
            source.Response.Dispose();
        }
        var metadataKey = GetMetadataKey(source.Url, expectedLength, validator, headers);
        var resumeOffset = PreparePartialFile(temporaryPath, metadataPath, metadataKey,
            supportsRanges && validator != null, expectedLength);
        if (supportsRanges && expectedLength is long totalLength)
        {
            await DownloadRangesAsync(source.Url, temporaryPath, totalLength, validator, headers,
                threadCount, retryCount, resumeOffset, metadataPath, metadataKey, onWritten, onReceived,
                onDownloaded, limiter, cancellationToken);
        }
        else
        {
            await DownloadSequentialAsync(source, temporaryPath, expectedLength, headers, retryCount, onWritten,
                onReceived, onDownloaded, limiter, cancellationToken);
        }

        if (expectedLength is not null && new FileInfo(temporaryPath).Length != expectedLength.Value)
        {
            throw new IOException($"Downloaded size differs from expected size {expectedLength.Value}.");
        }
        File.Move(temporaryPath, outputPath);
        File.Delete(metadataPath);
    }

    private async Task<bool> SupportsRangesAsync(string url, long length, RangeConditionHeaderValue? validator,
        Dictionary<string, string> headers, CancellationToken token)
    {
        try
        {
            using var response = await SendAsync(url, headers, 0, 0, validator, token);
            return IsExpectedRange(response, 0, 0, length, validator);
        }
        catch (Exception ex) when (!token.IsCancellationRequested &&
                                   ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task DownloadRangesAsync(string url, string path, long length, RangeConditionHeaderValue? validator,
        Dictionary<string, string> headers, int threadCount, int retryCount, long resumeOffset,
        string metadataPath, string metadataKey, Action<long>? onWritten, Action<int>? onReceived,
        Action<long>? onDownloaded, BandwidthLimiter? limiter, CancellationToken token)
    {
        var concurrency = Math.Min(threadCount, 16);
        var window = Math.Min(concurrency * 2, 32);
        using var slots = new SemaphoreSlim(concurrency);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = new Queue<Task<byte[]>>();
        long nextOffset = resumeOffset;
        long written = resumeOffset;
        long committed = resumeOffset;
        long downloaded = resumeOffset;
        var progressLock = new object();

        void TrackDownloaded(int bytes)
        {
            lock (progressLock)
            {
                downloaded += bytes;
                onDownloaded?.Invoke(downloaded);
            }
        }

        void FillWindow()
        {
            while (pending.Count < window && nextOffset < length)
            {
                var from = nextOffset;
                var count = (int)Math.Min(_blockSize, length - from);
                pending.Enqueue(FetchBlockAsync(url, from, count, length, validator, headers, retryCount,
                    slots, onReceived, TrackDownloaded, limiter, stop.Token));
                nextOffset += count;
            }
        }

        await using var output = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read,
            bufferSize: 128 * 1024, options: FileOptions.SequentialScan | FileOptions.Asynchronous);
        try
        {
            output.SetLength(resumeOffset);
            output.Position = resumeOffset;
            onWritten?.Invoke(resumeOffset);
            onDownloaded?.Invoke(resumeOffset);
            FillWindow();
            while (pending.Count != 0)
            {
                var data = await pending.Dequeue();
                await output.WriteAsync(data, stop.Token);
                written += data.Length;
                onWritten?.Invoke(written);
                if (written - committed >= _flushThreshold)
                {
                    output.Flush(flushToDisk: true);
                    WriteMetadata(metadataPath, metadataKey, written);
                    committed = written;
                }
                FillWindow();
            }
            output.Flush(flushToDisk: true);
            WriteMetadata(metadataPath, metadataKey, written);
        }
        catch
        {
            stop.Cancel();
            try
            {
                await Task.WhenAll(pending);
            }
            catch
            {
                // 保留首先发生的错误。
            }
            try
            {
                // 重试耗尽时也提交已顺序写入的完整块；不足定期 flush 阈值的前缀仍可续传。
                output.Flush(flushToDisk: true);
                WriteMetadata(metadataPath, metadataKey, written);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 无法提交时沿用上一次安全偏移，保留原始下载错误。
            }
            throw;
        }
    }

    private async Task<byte[]> FetchBlockAsync(string url, long from, int count, long totalLength,
        RangeConditionHeaderValue? validator, Dictionary<string, string> headers, int retries, SemaphoreSlim slots,
        Action<int>? onReceived, Action<int> onDownloadedDelta, BandwidthLimiter? limiter, CancellationToken token)
    {
        await slots.WaitAsync(token);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var credited = 0;
                try
                {
                    var to = from + count - 1;
                    using var response = await SendAsync(url, headers, from, to, validator, token);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                    {
                        response.EnsureSuccessStatusCode();
                    }
                    if (!IsExpectedRange(response, from, to, totalLength, validator))
                    {
                        throw new InvalidDataException($"Invalid range response for bytes {from}-{to}.");
                    }
                    var data = new byte[count];
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    var received = 0;
                    while (received < data.Length)
                    {
                        var read = await ReadWithTimeoutAsync(input,
                            data.AsMemory(received, Math.Min(128 * 1024, data.Length - received)), token);
                        if (read == 0)
                        {
                            throw new EndOfStreamException($"Range response ended after {received} of {data.Length} bytes.");
                        }
                        received += read;
                        credited += read;
                        onDownloadedDelta(read);
                        if (limiter != null)
                        {
                            await limiter.AddAsync(read, token);
                        }
                        onReceived?.Invoke(read);
                    }
                    if (await ReadWithTimeoutAsync(input, new byte[1], token) != 0)
                    {
                        throw new InvalidDataException($"Range response exceeded bytes {from}-{to}.");
                    }
                    return data;
                }
                catch (Exception ex) when (ex is not OperationCanceledException && attempt < retries &&
                                           ex is HttpRequestException or IOException)
                {
                    onDownloadedDelta(-credited);
                    await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500 * (attempt + 1), 5000)), token);
                }
                catch
                {
                    onDownloadedDelta(-credited);
                    throw;
                }
            }
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task DownloadSequentialAsync(WebSourceResult source, string path, long? length,
        Dictionary<string, string> headers, int retries, Action<long>? onWritten, Action<int>? onReceived,
        Action<long>? onDownloaded, BandwidthLimiter? limiter, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            HttpResponseMessage? retryResponse = null;
            try
            {
                var response = source.Response!;
                var input = source.Stream!;
                var prefix = source.Prefix;
                if (attempt > 0)
                {
                    retryResponse = await SendAsync(source.Url, headers, null, null, null, token);
                    retryResponse.EnsureSuccessStatusCode();
                    response = retryResponse;
                    input = await response.Content.ReadAsStreamAsync(token);
                    prefix = [];
                    if (length != null && response.Content.Headers.ContentLength != length)
                    {
                        throw new InvalidDataException("File size changed between attempts.");
                    }
                }

                await using var output = new FileStream(path, FileMode.Create,
                    FileAccess.Write, FileShare.Read, bufferSize: 128 * 1024,
                    options: FileOptions.SequentialScan | FileOptions.Asynchronous);
                await output.WriteAsync(prefix, token);
                onWritten?.Invoke(prefix.Length);
                onDownloaded?.Invoke(prefix.Length);
                onReceived?.Invoke(prefix.Length);
                var buffer = new byte[128 * 1024];
                long written = prefix.Length;
                int read;
                while ((read = await ReadWithTimeoutAsync(input, buffer, token)) != 0)
                {
                    written += read;
                    if (length != null && written > length)
                    {
                        throw new InvalidDataException("Response exceeded its Content-Length.");
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), token);
                    if (limiter != null)
                    {
                        await limiter.AddAsync(read, token);
                    }
                    onWritten?.Invoke(written);
                    onDownloaded?.Invoke(written);
                    onReceived?.Invoke(read);
                }
                if (length != null && written != length)
                {
                    throw new IOException($"Response ended after {written} of {length} bytes.");
                }
                await output.FlushAsync(token);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && attempt < retries &&
                                       ex is HttpRequestException or IOException)
            {
                if (attempt == 0)
                {
                    source.Response?.Dispose();
                }
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(500 * (attempt + 1), 5000)), token);
            }
            finally
            {
                retryResponse?.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> SendAsync(string url, Dictionary<string, string> headers,
        long? from, long? to, RangeConditionHeaderValue? validator, CancellationToken token)
    {
        for (var redirects = 0; redirects <= 10; redirects++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (from != null || to != null)
            {
                request.Headers.Range = new RangeHeaderValue(from, to);
                if (validator != null)
                {
                    request.Headers.IfRange = validator;
                }
            }

            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location != null)
            {
                url = new Uri(request.RequestUri!, response.Headers.Location).AbsoluteUri;
                response.Dispose();
                continue;
            }
            return response;
        }
        throw new HttpRequestException("Too many redirects while downloading binary file.");
    }

    private static RangeConditionHeaderValue? GetValidator(HttpResponseMessage response)
    {
        var etag = response.Headers.ETag;
        if (etag is { IsWeak: false })
        {
            return new RangeConditionHeaderValue(etag);
        }
        var modified = response.Content.Headers.LastModified;
        return modified != null ? new RangeConditionHeaderValue(modified.Value) : null;
    }

    private static bool IsExpectedRange(HttpResponseMessage response, long from, long to, long totalLength,
        RangeConditionHeaderValue? validator)
    {
        var range = response.Content.Headers.ContentRange;
        if (validator?.EntityTag != null && response.Headers.ETag != null &&
            !validator.EntityTag.Equals(response.Headers.ETag))
        {
            return false;
        }
        if (validator?.Date != null && response.Content.Headers.LastModified != null &&
            validator.Date != response.Content.Headers.LastModified)
        {
            return false;
        }
        return response.StatusCode == HttpStatusCode.PartialContent &&
               response.Content.Headers.ContentEncoding.Count == 0 &&
               range is { HasLength: true, HasRange: true } &&
               range.From == from && range.To == to && range.Length == totalLength &&
               (response.Content.Headers.ContentLength == null || response.Content.Headers.ContentLength == to - from + 1);
    }

    private async Task<int> ReadWithTimeoutAsync(Stream input, Memory<byte> buffer, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_readTimeout);
        try
        {
            return await input.ReadAsync(buffer, timeout.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new IOException($"No data received for {_readTimeout.TotalSeconds:0} seconds.");
        }
    }

    private sealed class BandwidthLimiter(long bytesPerSecond)
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();
        private long _received;

        public async Task AddAsync(int bytes, CancellationToken token)
        {
            var total = Interlocked.Add(ref _received, bytes);
            var delay = TimeSpan.FromSeconds((double)total / bytesPerSecond) - _watch.Elapsed;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, token);
            }
        }
    }

    private static string GetMetadataKey(string url, long? length, RangeConditionHeaderValue? validator,
        Dictionary<string, string> headers)
    {
        var identity = new StringBuilder()
            .Append(url).Append('\n')
            .Append(length?.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append(validator).Append('\n');
        foreach (var header in headers.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            identity.Append(header.Key.ToLowerInvariant()).Append(':')
                .Append(header.Value.Length).Append(':').Append(header.Value).Append('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity.ToString())));
    }

    private static long PreparePartialFile(string temporaryPath, string metadataPath, string key,
        bool canResume, long? length)
    {
        if (!File.Exists(temporaryPath))
        {
            WriteMetadata(metadataPath, key, 0);
            return 0;
        }
        if (!File.Exists(metadataPath))
        {
            throw new IOException($"Incomplete file has no matching metadata: {temporaryPath}");
        }
        var lines = File.ReadAllLines(metadataPath);
        if (lines.Length != 3 || lines[0] != "binary-download-v1" || lines[1] != key ||
            !long.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out var committed) ||
            committed > new FileInfo(temporaryPath).Length ||
            (length != null && committed > length))
        {
            throw new IOException($"Incomplete file belongs to a different download: {temporaryPath}");
        }
        return canResume ? committed : 0;
    }

    private static void WriteMetadata(string path, string key, long offset)
    {
        var temporaryMetadataPath = path + ".tmp";
        File.WriteAllLines(temporaryMetadataPath,
            ["binary-download-v1", key, offset.ToString(CultureInfo.InvariantCulture)]);
        File.Move(temporaryMetadataPath, path, overwrite: true);
    }
}
