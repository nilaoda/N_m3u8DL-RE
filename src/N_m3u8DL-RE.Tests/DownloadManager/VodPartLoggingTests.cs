using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public partial class VodMultiInitTests
{
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
            await using var server = new LiveFixtureServer(root, (_, _) => null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "vod.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            VodStreamPlanner.CaptureHlsTimeline(streams);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var options = LegacyOptions(root);
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
