using System.Net;
using System.Net.Http.Headers;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.DownloadManager;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class BinaryDownloadManagerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UrlDownloadKeepsHeadersAndRetriesWithoutOvercountingProgress(bool supportsRanges)
    {
        var data = MakeData(12_000);
        var fullRequests = 0;
        var failedBlock = 0;
        var progress = new List<long>();
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("Bearer fixture", request.Headers.Authorization?.ToString());
            HttpResponseMessage response;
            if (request.Headers.Range != null && supportsRanges)
            {
                var (from, to) = GetRange(request);
                var shortBody = from == 1024 && Interlocked.Increment(ref failedBlock) == 1;
                response = RangeResponse(data, from, to, shortBody ? 100 : null);
                response.Content.Headers.ContentLength = to - from + 1;
            }
            else
            {
                var shortBody = request.Headers.Range == null && Interlocked.Increment(ref fullRequests) == 1;
                response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(shortBody ? data[..6000] : data)
                };
                response.Content.Headers.ContentLength = data.Length;
            }
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
            return Task.FromResult(response);
        }));
        await WithFileAsync(async path =>
        {
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await manager.DownloadAsync("https://example.test/file.mp4", path,
                new Dictionary<string, string> { ["Authorization"] = "Bearer fixture" },
                threadCount: 1, retryCount: 1, onLength: length => Assert.Equal(data.Length, length),
                onReceived: _ => { }, onDownloaded: progress.Add);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Equal(supportsRanges ? 1 : 2, fullRequests);
            Assert.All(progress, value => Assert.InRange(value, 0, data.Length));
            Assert.Equal(data.Length, progress[^1]);
            Assert.Contains(Enumerable.Range(1, progress.Count - 1), i => progress[i] < progress[i - 1]);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task RangeBlocksMayFinishOutOfOrderButFileIsWrittenInOrder(int threads)
    {
        var data = MakeData(80_000);
        var active = 0;
        var maxActive = 0;
        long received = 0;
        long downloaded = 0;
        using var client = new HttpClient(new DelegateHandler(async request =>
        {
            var current = Interlocked.Increment(ref active);
            maxActive = Math.Max(maxActive, current);
            try
            {
                var (from, to) = GetRange(request);
                await Task.Delay(from == 0 && to != 0 ? 100 : 5);
                return RangeResponse(data, from, to);
            }
            finally
            {
                Interlocked.Decrement(ref active);
            }
        }));

        await WithFileAsync(async path =>
        {
            using var source = await CreateSourceAsync(data);
            var manager = new BinaryDownloadManager(client, blockSize: 4096);
            await manager.DownloadAsync(source, path, [], threadCount: threads, retryCount: 1,
                onReceived: bytes => Interlocked.Add(ref received, bytes),
                onDownloaded: bytes => Interlocked.Exchange(ref downloaded, bytes));
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Equal(threads > 1, maxActive > 1);
            Assert.Equal(data.Length, received);
            Assert.Equal(data.Length, downloaded);
        });
    }

    [Fact]
    public async Task ServerIgnoringRangeUsesOriginalResponseIncludingProbePrefix()
    {
        var data = MakeData(12_000);
        var requests = 0;
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data)
            });
        }));

        await WithFileAsync(async path =>
        {
            using var source = await CreateSourceAsync(data);
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await manager.DownloadAsync(source, path, [], threadCount: 4, retryCount: 0);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Equal(1, requests);
        });
    }

    [Fact]
    public async Task FailedRangeProbeUsesOriginalResponse()
    {
        var data = MakeData(12_000);
        using var client = new HttpClient(new DelegateHandler(_ =>
            throw new HttpRequestException("Range probe failed")));

        await WithFileAsync(async path =>
        {
            using var source = await CreateSourceAsync(data);
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await manager.DownloadAsync(source, path, [], threadCount: 4, retryCount: 0);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        });
    }

    [Fact]
    public async Task SequentialDownloadRestartsAfterReadFailure()
    {
        var data = MakeData(12_000);
        var requests = 0;
        var progress = new List<long>();
        using var client = new HttpClient(new DelegateHandler(_ =>
        {
            Interlocked.Increment(ref requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(data)
            });
        }));

        await WithFileAsync(async path =>
        {
            using var failingStream = new FailingStream(data, 128);
            using var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(failingStream)
            };
            response.Content.Headers.ContentLength = data.Length;
            using var source = new WebSourceResult("<RE_BINARY_DATA>", "https://example.test/file.bin",
                response, failingStream, data[..128]);
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await manager.DownloadAsync(source, path, [], threadCount: 1, retryCount: 1,
                onDownloaded: progress.Add);
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.Equal(2, requests); // Range 探测被忽略后，顺序下载失败再 GET 一次。
            Assert.Contains(384, progress);
            Assert.Equal(0, progress[progress.IndexOf(384) + 1]);
            Assert.All(progress, value => Assert.InRange(value, 0, data.Length));
            Assert.Equal(data.Length, progress[^1]);
        });
    }

    [Fact]
    public async Task ShortRangeBodyIsRetriedBeforeItCanBeWritten()
    {
        var data = MakeData(15_000);
        var shortResponses = 0;
        long downloaded = 0;
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            var (from, to) = GetRange(request);
            if (from == 1024 && Interlocked.Increment(ref shortResponses) == 1)
            {
                var shortResponse = RangeResponse(data, from, to, actualLength: 100);
                shortResponse.Content.Headers.ContentLength = to - from + 1;
                return Task.FromResult(shortResponse);
            }
            return Task.FromResult(RangeResponse(data, from, to));
        }));

        await WithFileAsync(async path =>
        {
            using var source = await CreateSourceAsync(data);
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await manager.DownloadAsync(source, path, [], threadCount: 3, retryCount: 2,
                onDownloaded: bytes => Interlocked.Exchange(ref downloaded, bytes));
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.True(shortResponses > 1);
            Assert.Equal(data.Length, downloaded);
        });
    }

    [Fact]
    public async Task InvalidRangeResponseCannotProduceACompletedFile()
    {
        var data = MakeData(10_000);
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            var (from, to) = GetRange(request);
            var response = RangeResponse(data, from, to);
            if (to != 0)
            {
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from + 1, to + 1, data.Length);
            }
            return Task.FromResult(response);
        }));

        await WithFileAsync(async path =>
        {
            using var source = await CreateSourceAsync(data);
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                manager.DownloadAsync(source, path, [], threadCount: 3, retryCount: 0));
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public async Task InterruptedRangeDownloadResumesFromCommittedOffset()
    {
        var data = MakeData(12_000);
        var fail = true;
        var secondRunRanges = new List<long>();
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            var (from, to) = GetRange(request);
            if (!fail && to != 0)
            {
                lock (secondRunRanges)
                {
                    secondRunRanges.Add(from);
                }
            }
            if (fail && from == 4096)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
            return Task.FromResult(RangeResponse(data, from, to));
        }));

        await WithFileAsync(async path =>
        {
            // 文件远小于默认的定期 flush 阈值，失败时仍应提交已写完的连续前缀。
            var manager = new BinaryDownloadManager(client, blockSize: 1024);
            using (var firstSource = await CreateSourceAsync(data))
            {
                firstSource.Response!.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
                await Assert.ThrowsAsync<HttpRequestException>(() =>
                    manager.DownloadAsync(firstSource, path, [], threadCount: 4, retryCount: 0));
            }
            Assert.Equal(4096, new FileInfo(path + ".downloading").Length);
            using (var changedHeaderSource = await CreateSourceAsync(data))
            {
                changedHeaderSource.Response!.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
                await Assert.ThrowsAsync<IOException>(() => manager.DownloadAsync(changedHeaderSource, path,
                    new Dictionary<string, string> { ["Authorization"] = "Bearer another-user" },
                    threadCount: 4, retryCount: 0));
            }
            Assert.Equal(4096, new FileInfo(path + ".downloading").Length);
            fail = false;
            using (var secondSource = await CreateSourceAsync(data))
            {
                secondSource.Response!.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
                await manager.DownloadAsync(secondSource, path, [], threadCount: 1, retryCount: 0);
            }
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
            Assert.All(secondRunRanges, from => Assert.True(from >= 4096));
            Assert.False(File.Exists(path + ".downloading.meta"));
        });
    }

    private static async Task<WebSourceResult> CreateSourceAsync(byte[] data)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(data)
        };
        var stream = await response.Content.ReadAsStreamAsync();
        var prefix = new byte[Math.Min(128, data.Length)];
        await stream.ReadExactlyAsync(prefix);
        return new WebSourceResult("<RE_BINARY_DATA>", "https://example.test/file.bin", response, stream, prefix);
    }

    private static HttpResponseMessage RangeResponse(byte[] data, long from, long to, int? actualLength = null)
    {
        var count = actualLength ?? (int)(to - from + 1);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(data.AsSpan((int)from, count).ToArray())
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, data.Length);
        return response;
    }

    private static (long From, long To) GetRange(HttpRequestMessage request)
    {
        var range = Assert.Single(request.Headers.Range!.Ranges);
        return (range.From!.Value, range.To!.Value);
    }

    private static byte[] MakeData(int size)
    {
        var data = new byte[size];
        new Random(471).NextBytes(data);
        return data;
    }

    private static async Task WithFileAsync(Func<string, Task> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "binary-download-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await action(Path.Combine(directory, "output.bin"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return send(request);
        }
    }

    private sealed class FailingStream(byte[] data, int position) : MemoryStream(data)
    {
        private bool _readOnce;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_readOnce)
            {
                throw new IOException("Connection interrupted");
            }
            _readOnce = true;
            Position = position;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 256)], cancellationToken);
        }
    }
}
