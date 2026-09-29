using System.Net;
using System.Net.Sockets;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class HTTPLiveRecordManagerTests
{
    [Fact]
    public async Task RecordsCompleteTsFromInitialResponseWithoutAnotherRequest()
    {
        var bytes = new byte[188 * 60];
        new Random(956).NextBytes(bytes);
        for (var offset = 0; offset < bytes.Length; offset += 188)
        {
            bytes[offset] = 0x47;
        }

        var directory = Path.Combine(Path.GetTempPath(), $"http-live-ts-{Guid.NewGuid():N}");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var requestCount = 0;
        var serverTask = ServeOnceAsync(listener, bytes, () => Interlocked.Increment(ref requestCount));
        try
        {
            var url = $"http://127.0.0.1:{port}/live.ts";
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(url);
            Assert.Equal(ExtractorType.HTTP_LIVE, extractor.ExtractorType);

            var options = new MyOption
            {
                Input = url,
                SaveName = "recording",
                SaveDir = directory,
                NoAnsiColor = true,
                LogLevel = LogLevel.OFF
            };
            var manager = new HTTPLiveRecordManager(new DownloaderConfig
            {
                MyOptions = options,
                DirPrefix = Path.Combine(directory, "metadata")
            }, await extractor.ExtractStreamsAsync(), extractor);

            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, requestCount);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(directory, "recording.ts")));
            Assert.False(Directory.Exists(Path.Combine(directory, "metadata")));
        }
        finally
        {
            listener.Stop();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static async Task ServeOnceAsync(TcpListener listener, byte[] bytes, Action onRequest)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var requestBuffer = new byte[1024];
        await stream.ReadAtLeastAsync(requestBuffer, 1, throwOnEndOfStream: true);
        onRequest();
        var headers = "HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\n\r\n"u8.ToArray();
        await stream.WriteAsync(headers);
        await stream.WriteAsync(bytes);
    }
}
