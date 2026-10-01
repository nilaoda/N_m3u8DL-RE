using System.Globalization;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class LiveDurationTests
{
    [Theory]
    [InlineData(0.1, "20", 2, false)]
    [InlineData(0.5, "20", 10, false)]
    [InlineData(2.002, "30000/1001", 60, false)]
    [InlineData(1, "20", 20, true)]
    public async Task RecordLimitKeepsFractionalDurationAndRecoversNegativeExtinf(
        double duration, string frameRate, int framesPerSegment, bool negativeDuration)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-duration-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", $"testsrc2=s=160x90:r={frameRate}",
                "-t", (duration * 4).ToString(CultureInfo.InvariantCulture), "-c:v", "libx264",
                "-g", framesPerSegment.ToString(CultureInfo.InvariantCulture), "-bf", "0", "-f", "hls",
                "-hls_time", duration.ToString(CultureInfo.InvariantCulture), "-hls_segment_filename",
                Path.Combine(root, "media-%d.ts"), Path.Combine(root, "source.m3u8"));
            await using var server = new MediaFixtureServer(root, (path, version) => path == "live.m3u8"
                ? Playlist(version, duration, negativeDuration, "ts") : null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            var options = CreateOptions(root);
            options.LiveRealTimeMerge = true;
            options.LiveWaitTime = 1;
            options.LiveIdleTimeout = 5;
            options.LiveRecordLimit = TimeSpan.FromSeconds(duration * 3);
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.Equal(3, server.RequestCount("live.m3u8"));
            for (var i = 0; i < 3; i++)
                Assert.Equal(1, server.RequestCount($"media-{i}.ts"));
            Assert.Equal(0, server.RequestCount("media-3.ts"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), duration * 3, framesPerSegment * 3);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SubtitleOffsetsKeepFractionalSecondsAcrossRefreshes()
    {
        var root = Directory.CreateTempSubdirectory("live-subtitle-duration-").FullName;
        try
        {
            for (var i = 0; i < 4; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"media-{i}.vtt"), $"WEBVTT\n\n00:00:00.000 --> 00:00:00.{200 + i * 100:000}\nCue {i}\n");
            await using var server = new MediaFixtureServer(root, (path, version) => path == "live.m3u8"
                ? Playlist(version, 0.5, false, "vtt") : null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            streams[0].MediaType = MediaType.SUBTITLES;
            streams[0].Extension = "vtt";
            var options = CreateOptions(root);
            options.LiveRealTimeMerge = true;
            options.LiveWaitTime = 1;
            options.LiveIdleTimeout = 5;
            options.LiveRecordLimit = TimeSpan.FromSeconds(1.5);
            options.SubtitleFormat = SubtitleFormat.VTT;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            var output = Assert.Single(Directory.GetFiles(Path.Combine(root, "out")));
            var cues = WebVttSub.Parse(await File.ReadAllTextAsync(output)).Cues;
            Assert.Equal(3, cues.Count);
            Assert.Equal([TimeSpan.Zero, TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1)], cues.Select(cue => cue.StartTime));
            Assert.Equal(TimeSpan.FromSeconds(1.4), cues[^1].EndTime);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string Playlist(int version, double duration, bool negativeDuration, string extension)
    {
        // 每次刷新只增加一个分片，确保消费者分批累计，而非一次下载全部媒体。
        var midnight = new DateTimeOffset(2026, 10, 1, 23, 59, 59, TimeSpan.FromHours(8));
        var targetDuration = Math.Ceiling(duration).ToString(CultureInfo.InvariantCulture);
        var content = $"#EXTM3U\n#EXT-X-TARGETDURATION:{targetDuration}\n#EXT-X-MEDIA-SEQUENCE:0\n";
        for (var i = 0; i <= Math.Min(version, 3); i++)
        {
            var extinf = negativeDuration && i == 1 ? -86399 : duration;
            content += $"#EXT-X-PROGRAM-DATE-TIME:{midnight.AddSeconds(i * duration):O}\n" +
                       $"#EXTINF:{extinf.ToString(CultureInfo.InvariantCulture)},\nmedia-{i}.{extension}\n";
        }
        if (version >= 3)
            content += "#EXT-X-ENDLIST\n";
        return content;
    }
}
