using System.Text;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class LiveRequestTimeoutTests
{
    [Fact]
    public void ShortTailDoesNotOverrideNormalSegmentDurationOrManualTimeout()
    {
        var stream = new StreamSpec
        {
            Playlist = new Playlist
            {
                MediaParts = [new MediaPart
                {
                    MediaSegments = [new MediaSegment { Duration = 6 }, new MediaSegment { Duration = 6 },
                        new MediaSegment { Duration = 0.1 }]
                }]
            }
        };
        var options = new MyOption { HttpRequestTimeout = 100 };
        var automatic = LiveRequestTimeoutPolicy.GetTimeouts(stream, options);
        Assert.Equal(TimeSpan.FromSeconds(6), automatic.Request);
        Assert.Equal(TimeSpan.FromSeconds(6), automatic.Read);
        options.HttpRequestTimeoutSpecified = true;
        var manual = LiveRequestTimeoutPolicy.GetTimeouts(stream, options);
        Assert.Equal(TimeSpan.FromSeconds(100), manual.Request);
        Assert.Equal(TimeSpan.FromSeconds(100), manual.Read);
    }

    [Theory]
    [InlineData(0.1, 3, 5)]
    [InlineData(60, 10, 15)]
    [InlineData(double.NaN, 5, 5)]
    public void AutomaticTimeoutBoundsHandleVeryShortLongAndUnknownSegments(double duration, int requestSeconds, int readSeconds)
    {
        var stream = new StreamSpec
        {
            Playlist = new Playlist { MediaParts = [new MediaPart { MediaSegments = [new MediaSegment { Duration = duration }] }] }
        };
        var timeouts = LiveRequestTimeoutPolicy.GetTimeouts(stream, new MyOption());
        Assert.Equal(TimeSpan.FromSeconds(requestSeconds), timeouts.Request);
        Assert.Equal(TimeSpan.FromSeconds(readSeconds), timeouts.Read);
    }

    [Theory]
    [InlineData("hls", false)]
    [InlineData("hls", true)]
    [InlineData("dash", false)]
    [InlineData("dash", true)]
    [InlineData("mss", false)]
    [InlineData("mss", true)]
    public async Task RefreshTimeoutCoversHeadersAndBodyWithoutCancellingRecording(string format, bool body)
    {
        var root = Directory.CreateTempSubdirectory("live-refresh-timeout-").FullName;
        try
        {
            var manifest = format switch
            {
                "hls" => "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nmedia.ts\n",
                "dash" => """
                    <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" mediaPresentationDuration="PT2S">
                      <Period><AdaptationSet mimeType="video/mp4"><Representation id="v" bandwidth="1000" codecs="avc1.64001e">
                        <SegmentList duration="2"><Initialization sourceURL="init.mp4"/><SegmentURL media="media.m4s"/></SegmentList>
                      </Representation></AdaptationSet></Period>
                    </MPD>
                    """,
                _ => """
                    <SmoothStreamingMedia MajorVersion="2" MinorVersion="1" IsLive="TRUE" Duration="2000" TimeScale="1000">
                      <StreamIndex Type="audio" Name="audio" TimeScale="1000" Url="media-{start time}.m4s">
                        <QualityLevel Index="0" Bitrate="64000" FourCC="AACL" SamplingRate="48000" Channels="2" CodecPrivateData="1190"/>
                        <c t="0" d="2000"/>
                      </StreamIndex>
                    </SmoothStreamingMedia>
                    """
            };
            await using var server = new MediaFixtureServer(root, (_, _) => manifest,
                responseWriter: async (_, count, stream, token) =>
                {
                    if (count == 1)
                        return false;
                    if (body)
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1024\r\n\r\n"), token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return true;
                });
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live");
            var streams = await extractor.ExtractStreamsAsync();
            Assert.NotEmpty(streams);
            using var stop = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await extractor.RefreshPlayListAsync(streams, stop.Token, TimeSpan.FromSeconds(1)).WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(stop.IsCancellationRequested);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SegmentTimeoutAllowsOngoingTransferAndLocalSpeedLimitWait(bool limited)
    {
        var root = Directory.CreateTempSubdirectory("live-read-timeout-").FullName;
        using var stop = new CancellationTokenSource();
        Task<DownloadResult?>? downloading = null;
        try
        {
            var bytes = new byte[128 * 1024];
            await File.WriteAllBytesAsync(Path.Combine(root, "media.bin"), bytes);
            await using var server = new MediaFixtureServer(root, (_, _) => null,
                responseWriter: limited ? null : async (_, _, stream, token) =>
                {
                    await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\n\r\n"), token);
                    for (var offset = 0; offset < bytes.Length; offset += 16 * 1024)
                    {
                        await stream.WriteAsync(bytes.AsMemory(offset, 16 * 1024), token);
                        await Task.Delay(500, token);
                    }
                    return true;
                });
            var speed = new SpeedContainer { SpeedLimit = limited ? 1 : long.MaxValue };
            // 直播的逐片读取超时须独立于进度栏零速计数。
            for (var i = 0; i < 20; i++)
                speed.AddLowSpeedCount();
            var downloader = new SimpleDownloader(new DownloaderConfig { DirPrefix = root, MyOptions = CreateOptions(root) });
            var output = Path.Combine(root, "segment.tmp");
            downloading = downloader.DownloadSegmentAsync(new MediaSegment { Url = server.Url + "media.bin" }, output, speed,
                cancellationToken: stop.Token, throwOnFailure: true, networkTimeout: TimeSpan.FromSeconds(1));
            if (limited)
            {
                await Task.Delay(1500);
                Assert.False(downloading.IsCompleted);
                Assert.True(speed.RDownloaded > 0);
                speed.SpeedLimit = long.MaxValue;
                speed.Reset();
            }
            var result = await downloading.WaitAsync(TimeSpan.FromSeconds(6));
            Assert.True(result!.Success);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result.ActualFilePath));
        }
        finally
        {
            stop.Cancel();
            if (downloading != null && !downloading.IsCompleted)
            {
                try { await downloading.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (OperationCanceledException) { }
            }
            Directory.Delete(root, true);
        }
    }
}
