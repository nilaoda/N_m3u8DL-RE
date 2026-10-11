using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public sealed class IPTVRecordingTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-iptv-tests-").FullName;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandLineOnlyConnectsSelectedChannelAndSupportsSkipDownload(bool skipDownload)
    {
        var bytes = Packets(1);
        await using var server = new MediaFixtureServer(directory, (_, _) => null, responseWriter: async (_, _, stream, token) =>
        {
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
            await stream.WriteAsync(bytes, token);
            return true;
        });
        var path = Path.Combine(directory, "channels.m3u");
        var list = $"#EXTM3U\n#EXTINF:-1,频道A\n{server.Url}a\n#EXTINF:-1,频道B[高清]\n{server.Url}b\n";
        await File.WriteAllTextAsync(path, list);
        var output = Path.Combine(directory, "out");
        var args = new List<string>
        {
            typeof(CommandInvoker).Assembly.Location, path, "-sv", "name=频道B:for=all",
            "--save-dir", output, "--no-config", "--disable-update-check", "--no-log", "--no-ansi-color"
        };
        if (skipDownload)
            args.Add("--skip-download");
        await Run("dotnet", args.ToArray());
        Assert.Equal(0, server.RequestCount("a"));
        Assert.Equal(skipDownload ? 0 : 1, server.RequestCount("b"));
        if (skipDownload)
            Assert.False(Directory.Exists(output));
        else
        {
            var file = Assert.Single(Directory.GetFiles(output));
            Assert.StartsWith("频道B[高清]_", Path.GetFileName(file));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
        }
        Assert.Equal(list, await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("end")]
    [InlineData("failure")]
    [InlineData("limit")]
    public async Task ChannelsHaveIndependentResponsesStopConditionsAndOutputFiles(string scenario)
    {
        var first = Packets(1);
        var second = Packets(2);
        await using var server = new MediaFixtureServer(directory, (_, _) => null, responseWriter: async (path, _, stream, token) =>
        {
            if (path == "a" && scenario == "failure")
            {
                await stream.WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
                return true;
            }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nConnection: close\r\n\r\n"u8.ToArray(), token);
            await stream.WriteAsync(path == "a" ? first : second, token);
            if (path == "b")
            {
                if (scenario == "limit")
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(50, token);
                        await stream.WriteAsync(second, token);
                    }
                }
                else
                {
                    await Task.Delay(1200, token);
                    await stream.WriteAsync(second, token);
                }
            }
            return true;
        });
        var path = Path.Combine(directory, "channels.m3u");
        await File.WriteAllTextAsync(path, $"#EXTM3U\n#EXTINF:-1,相同名称\n{server.Url}a\n#EXTINF:-1,相同名称\n{server.Url}b\n");
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(path);
        var channels = await extractor.ExtractStreamsAsync();
        var output = Directory.CreateDirectory(Path.Combine(directory, "out")).FullName;
        var original = Path.Combine(output, "same.ts");
        await File.WriteAllBytesAsync(original, [99]);
        var manager = new HTTPLiveRecordManager(new DownloaderConfig
        {
            MyOptions = new MyOption
            {
                SaveDir = output, SavePattern = "same", NoAnsiColor = true, LogLevel = LogLevel.OFF,
                LiveRecordLimit = scenario == "limit" ? TimeSpan.FromSeconds(1) : null
            },
            DirPrefix = directory
        }, channels, extractor);
        Assert.Equal(scenario != "failure", await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(8)));
        Assert.Equal(1, server.RequestCount("a"));
        Assert.Equal(1, server.RequestCount("b"));
        Assert.Equal(new byte[] { 99 }, await File.ReadAllBytesAsync(original));
        var recorded = Directory.GetFiles(output).Where(file => file != original).Select(File.ReadAllBytes).ToArray();
        Assert.Equal(scenario == "failure" ? 1 : 2, recorded.Length);
        if (scenario != "failure")
            Assert.Contains(recorded, bytes => bytes.SequenceEqual(first));
        var secondOutput = Assert.Single(recorded, bytes => bytes[1] == 2);
        if (scenario == "limit")
        {
            Assert.True(secondOutput.Length >= second.Length);
            Assert.Equal(second, secondOutput[..second.Length]);
        }
        else
            Assert.Equal(second.Concat(second).ToArray(), secondOutput);
    }

    private static byte[] Packets(byte value)
    {
        var bytes = Enumerable.Repeat(value, 188 * 60).ToArray();
        for (var offset = 0; offset < bytes.Length; offset += 188)
            bytes[offset] = 0x47;
        return bytes;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
