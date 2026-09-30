using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using System.Buffers.Binary;
using System.Globalization;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodPartLoggingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task SinglePartWithSubtitlesDownloadsConcurrentlyAndKeepsBinaryOutput(bool deleteAfterDone, bool failedMedia)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-single-part-").FullName;
        var oldPath = Logger.LogFilePath;
        var oldWrite = Logger.IsWriteFile;
        var oldLevel = Logger.LogLevel;
        try
        {
            // Apple 点播的媒体时钟从约 10 秒开始，单段二进制输出应保留源时钟，
            // 最终混流则应归零媒体并保持已经映射到输出时间轴的字幕同步。
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
                "testsrc2=s=160x90:r=25", "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0",
                "-f", "hls", "-hls_time", "2", "-hls_segment_type", "fmp4",
                "-hls_segment_options", "video_track_timescale=1000",
                "-hls_fmp4_init_filename", "init.mp4", "-hls_segment_filename",
                Path.Combine(root, "media-%d.m4s"), Path.Combine(root, "source.m3u8"));
            for (var i = 0; i < 3; i++)
            {
                // HLS muxer 会归零 output_ts_offset，直接设置分片的解码时间原点。
                var path = Path.Combine(root, $"media-{i}.m4s");
                var bytes = await File.ReadAllBytesAsync(path);
                var tfdt = bytes.AsSpan().IndexOf("tfdt"u8);
                Assert.True(tfdt >= 0);
                if (bytes[tfdt + 4] == 1)
                    BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(tfdt + 8, 8), (ulong)(10 + i * 2) * 1000);
                else
                    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(tfdt + 8, 4), (uint)(10 + i * 2) * 1000);
                await File.WriteAllBytesAsync(path, bytes);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "subs.m3u8"),
                "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsub.vtt\n#EXT-X-ENDLIST\n");
            await File.WriteAllTextAsync(Path.Combine(root, "sub.vtt"),
                "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:900000\n\n00:00:00.250 --> 00:00:01.000\nhello\n\n");
            await File.WriteAllTextAsync(Path.Combine(root, "master.m3u8"), """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",LANGUAGE="en",NAME="en",URI="subs.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e",RESOLUTION=160x90,SUBTITLES="s"
                source.m3u8
                """);
            var subtitleRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var server = new MediaFixtureServer(root,
                (path, _) => failedMedia && path.EndsWith(".m4s") ? "invalid media" : null,
                async path =>
                {
                    if (path == "sub.vtt")
                        subtitleRequested.TrySetResult();
                    if (path.EndsWith(".m4s"))
                        // 媒体响应等到字幕请求出现才放行；串行下载会超时，不能靠耗时猜并发。
                        await subtitleRequested.Task.WaitAsync(TimeSpan.FromSeconds(10));
                });
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            await extractor.FetchPlayListAsync(streams);
            VodStreamPlanner.CaptureHlsTimeline(streams);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var options = CreateOptions(root);
            options.ConcurrentDownload = true;
            options.SubtitleFormat = SubtitleFormat.VTT;
            options.DelAfterDone = deleteAfterDone;
            options.MuxAfterDone = !failedMedia;
            options.MuxOptions = new MuxOptions { KeepFiles = true };
            var log = Path.Combine(root, "run.log");
            await File.WriteAllTextAsync(log, "");
            Logger.LogFilePath = log;
            Logger.IsWriteFile = true;
            Logger.LogLevel = LogLevel.INFO;
            var tmp = Path.Combine(root, "tmp");
            var manager = new SimpleDownloadManager(new DownloaderConfig { DirPrefix = tmp, MyOptions = options }, streams, extractor);
            if (failedMedia)
            {
                // 媒体时钟探测失败也必须释放字幕等待，保留已下载的输入。
                await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await manager.StartDownloadAsync().WaitAsync(TimeSpan.FromSeconds(15)));
                Assert.False(File.Exists(Path.Combine(root, "out", "result.en.vtt")));
                Assert.NotEmpty(Directory.GetFiles(tmp, "*", SearchOption.AllDirectories));
                return;
            }
            Assert.True(await manager.StartDownloadAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            var output = Path.Combine(root, "out", "result.mp4");
            await AssertVideo(output, 6, 150);
            var sourceStart = await Run("ffprobe", "-v", "error", "-show_entries", "format=start_time",
                "-of", "default=noprint_wrappers=1:nokey=1", output);
            Assert.InRange(double.Parse(sourceStart, CultureInfo.InvariantCulture), 9.999, 10.001);
            // 字节完全相同证明没有二次 remux，而不仅仅是隐藏了拼接日志。
            var expected = new List<byte>(await File.ReadAllBytesAsync(Path.Combine(root, "init.mp4")));
            for (var i = 0; i < 3; i++)
                expected.AddRange(await File.ReadAllBytesAsync(Path.Combine(root, $"media-{i}.m4s")));
            Assert.Equal(expected.ToArray(), await File.ReadAllBytesAsync(output));
            var cue = Assert.Single(WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt"))).Cues);
            Assert.Equal(0.25, cue.StartTime.TotalSeconds);
            var muxed = Path.Combine(root, "out", "tmp.mp4");
            await AssertVideo(muxed, 6, 150);
            var muxedCue = Assert.Single(WebVttSub.Parse(await Run("ffmpeg", "-v", "error", "-i", muxed,
                "-map", "0:s:0", "-f", "webvtt", "-")).Cues);
            Assert.Equal(cue.StartTime, muxedCue.StartTime);
            var lines = await File.ReadAllLinesAsync(log);
            Assert.DoesNotContain(lines, l => l.Contains(string.Format(ResString.vodPartsConcat, 1)) || l.Contains(ResString.ffmpegMerge));
            Assert.Equal(2, lines.Count(l => l.Contains(ResString.binaryMerge)));
            Assert.Equal(!deleteAfterDone, Directory.Exists(tmp));
        }
        finally
        {
            Logger.LogFilePath = oldPath;
            Logger.IsWriteFile = oldWrite;
            Logger.LogLevel = oldLevel;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MultiPartLoggingShowsTrackOnceAndRespectsSegmentRetention(bool deleteAfterDone, bool changedInit)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-part-logging-").FullName;
        var oldPath = Logger.LogFilePath;
        var oldWrite = Logger.IsWriteFile;
        var oldLevel = Logger.LogLevel;
        try
        {
            await GenerateCutMedia(root);
            if (changedInit)
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=128x72:r=25",
                    "-t", "2", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls",
                    "-hls_time", "2", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "other-init.mp4",
                    "-hls_segment_filename", Path.Combine(root, "other-%d.m4s"), Path.Combine(root, "other.m3u8"));
            var manifest = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
            for (var i = 0; i < 3; i++)
            {
                if (i > 0)
                    manifest += "#EXT-X-DISCONTINUITY\n";
                var init = changedInit && i == 1 ? "other-init.mp4" : "init.mp4";
                var media = changedInit && i == 1 ? "other-0.m4s" : $"media-{i}.m4s";
                manifest += $"#EXT-X-MAP:URI=\"{init}\"\n#EXTINF:2,\n{media}\n";
            }
            await File.WriteAllTextAsync(Path.Combine(root, "vod.m3u8"), manifest + "#EXT-X-ENDLIST\n");
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "vod.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            VodStreamPlanner.CaptureHlsTimeline(streams);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var options = CreateOptions(root);
            options.DelAfterDone = deleteAfterDone;
            var log = Path.Combine(root, "run.log");
            await File.WriteAllTextAsync(log, "");
            Logger.LogFilePath = log;
            Logger.IsWriteFile = true;
            Logger.LogLevel = LogLevel.INFO;
            var tmp = Path.Combine(root, "tmp");
            Directory.CreateDirectory(Path.Combine(tmp, "finder", "nested"));
            await File.WriteAllTextAsync(Path.Combine(tmp, ".DS_Store"), "Finder metadata");
            await File.WriteAllTextAsync(Path.Combine(tmp, "finder", "nested", ".DS_Store"), "Finder metadata");
            var manager = new SimpleDownloadManager(new DownloaderConfig { DirPrefix = tmp, MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            await AssertVideo(Path.Combine(root, "out", "result.mp4"), 6, 150);
            var lines = await File.ReadAllLinesAsync(log);
            Assert.Single(lines, l => l.Contains(ResString.startDownloading));
            Assert.Single(lines, l => l.Contains(ResString.autoBinaryMerge));
            Assert.Single(lines, l => l.Contains(ResString.readingInfo));
            Assert.Single(lines, l => l.Contains(ResString.binaryMerge));
            Assert.Equal(changedInit ? 2 : 1, lines.Count(l => l.Contains("Video, h264")));
            Assert.Equal(1, server.RequestCount("init.mp4"));
            Assert.Equal(changedInit ? 1 : 0, server.RequestCount("other-init.mp4"));
            // 原始分片的保存扩展名由提取器决定，可能是 mp4，不能仅按 m4s 判断是否保留。
            var segments = Directory.Exists(tmp) ? Directory.GetFiles(tmp, "*", SearchOption.AllDirectories)
                .Where(p => int.TryParse(Path.GetFileNameWithoutExtension(p), out _)).ToArray() : [];
            if (deleteAfterDone)
            {
                Assert.Empty(segments);
                Assert.False(Directory.Exists(tmp));
                Assert.True(Directory.Exists(root));
            }
            else
            {
                Assert.True(File.Exists(Path.Combine(tmp, ".DS_Store")));
                Assert.True(File.Exists(Path.Combine(tmp, "finder", "nested", ".DS_Store")));
                Assert.Equal(3, segments.Length);
                for (var i = 0; i < 3; i++)
                {
                    var original = changedInit && i == 1 ? "other-0.m4s" : $"media-{i}.m4s";
                    var saved = Assert.Single(segments, p => Path.GetFileNameWithoutExtension(p) == i.ToString());
                    Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, original)), await File.ReadAllBytesAsync(saved));
                }
                Assert.Equal(3, Directory.GetFiles(tmp, "_init.mp4", SearchOption.AllDirectories).Length);
            }
        }
        finally
        {
            Logger.LogFilePath = oldPath;
            Logger.IsWriteFile = oldWrite;
            Logger.LogLevel = oldLevel;
            Directory.Delete(root, true);
        }
    }
}
