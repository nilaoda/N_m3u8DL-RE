using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class DashSingleFileDownloadTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SingleFileRetriesOrResumesWithoutOvercountingProgress(bool ranges, bool resume)
    {
        var root = Directory.CreateTempSubdirectory("dash-single-file-").FullName;
        try
        {
            // 跨越两个 Range 块即可验证续传，无需生成或解码媒体文件。
            var data = new byte[3 * 1024 * 1024];
            new Random(883).NextBytes(data);
            await using var server = new SingleFileServer(data, ranges);
            var options = new MyOption { ThreadCount = 1, DownloadRetryCount = resume ? 0 : 1 };
            var config = new DownloaderConfig { DirPrefix = root, MyOptions = options };
            var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer fixture" };
            var segment = new MediaSegment { Url = server.Url };
            var path = Path.Combine(root, "file.mp4.tmp");
            var speed = new SpeedContainer();
            var downloader = new SimpleDownloader(config);
            if (resume)
            {
                var failed = await downloader.DownloadSegmentAsync(segment, path, speed, headers, singleFile: true);
                Assert.Null(failed);
                Assert.Equal(2 * 1024 * 1024, new FileInfo(path + ".downloading").Length);
                server.RequestedRanges.Clear();
                options.DownloadRetryCount = 1;
                // 再次运行从已提交的字节开始，不能继承上次运行的进度计数。
                speed = new SpeedContainer();
                downloader = new SimpleDownloader(config);
            }
            var result = await downloader.DownloadSegmentAsync(segment, path, speed, headers, singleFile: true);
            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.Equal(data, await File.ReadAllBytesAsync(result.ActualFilePath));
            Assert.Equal(data.Length, speed.ResponseLength);
            Assert.Equal(data.Length, speed.RDownloaded);
            Assert.True(speed.Downloaded >= (resume ? data.Length - 2 * 1024 * 1024 : data.Length));
            Assert.Equal(0, server.HeadRequests);
            Assert.Equal(0, server.UnauthorizedRequests);
            if (ranges)
            {
                var blocks = server.RequestedRanges.Where(r => r.To != 0).ToList();
                Assert.NotEmpty(blocks);
                if (resume)
                    Assert.All(blocks, range => Assert.True(range.From >= 2 * 1024 * 1024));
                else
                    Assert.Equal(2, blocks.Count(r => r.From == 2 * 1024 * 1024));
            }
            else
            {
                Assert.Equal(2, server.FullRequests);
            }
            Assert.Single(Directory.GetFiles(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private sealed class SingleFileServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly List<Task> handlers = [];
        private readonly Task loop;
        private int failed;
        private int fullRequests;
        private int headRequests;
        private int unauthorizedRequests;
        public int FullRequests => fullRequests;
        public int HeadRequests => headRequests;
        public int UnauthorizedRequests => unauthorizedRequests;
        public ConcurrentQueue<(long From, long To)> RequestedRanges { get; } = new();
        public string Url { get; }

        public SingleFileServer(byte[] data, bool ranges)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/file.mp4";
            loop = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                        handlers.Add(Serve(await listener.AcceptTcpClientAsync(stop.Token)));
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
            async Task Serve(TcpClient client)
            {
                using (client)
                {
                    try
                    {
                        await using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                        var request = await reader.ReadLineAsync(stop.Token);
                        if (request == null)
                            return;
                        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        string? line;
                        while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync(stop.Token)))
                        {
                            var pair = line.Split(':', 2);
                            headers[pair[0]] = pair[1].Trim();
                        }
                        if (request.StartsWith("HEAD "))
                        {
                            Interlocked.Increment(ref headRequests);
                            await stream.WriteAsync("HTTP/1.1 405 Method Not Allowed\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
                            return;
                        }
                        if (headers.GetValueOrDefault("Authorization") != "Bearer fixture")
                            Interlocked.Increment(ref unauthorizedRequests);
                        long from = 0, to = data.Length - 1;
                        var partial = headers.TryGetValue("Range", out var range);
                        if (partial)
                        {
                            var pair = range![6..].Split('-');
                            from = long.Parse(pair[0]);
                            to = long.Parse(pair[1]);
                            RequestedRanges.Enqueue((from, to));
                        }
                        else
                        {
                            Interlocked.Increment(ref fullRequests);
                        }
                        var shouldFail = ranges ? from == 2 * 1024 * 1024 : !partial;
                        shouldFail = shouldFail && Interlocked.CompareExchange(ref failed, 1, 0) == 0;
                        if (!ranges)
                        {
                            from = 0;
                            to = data.Length - 1;
                        }
                        var status = ranges && partial ? "206 Partial Content" : "200 OK";
                        var contentRange = ranges && partial ? $"Content-Range: bytes {from}-{to}/{data.Length}\r\n" : "";
                        var response = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Length: {to - from + 1}\r\n{contentRange}ETag: \"fixture-v1\"\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(response, stop.Token);
                        var count = (int)(to - from + 1);
                        await stream.WriteAsync(data.AsMemory((int)from, shouldFail ? count / 2 : count), stop.Token);
                    }
                    // Range 下载会提前释放首次整文件响应，服务端允许客户端主动断开。
                    catch (IOException) { }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel();
            listener.Stop();
            await loop;
            await Task.WhenAll(handlers);
            stop.Dispose();
        }
    }
}
