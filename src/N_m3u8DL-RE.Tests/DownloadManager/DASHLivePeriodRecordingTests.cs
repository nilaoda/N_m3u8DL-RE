using System.Net;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class DASHLivePeriodRecordingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(true, "mux-failure")]
    [InlineData(true, "mux-import")]
    [InlineData(true, "recording-failure")]
    [InlineData(true, "reused-init")]
    [InlineData(true, "keep-temporary")]
    [InlineData(true, "skip-merge")]
    public async Task RecordsCurrentMediaAndFollowsPeriodRefresh(bool changesInit, string scenario = "normal")
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var muxFails = scenario == "mux-failure";
        var muxImport = scenario == "mux-import";
        var recordingFails = scenario == "recording-failure";
        var reusedInit = scenario == "reused-init";
        var keepsInit = scenario is "keep-temporary" or "skip-merge";
        var root = Directory.CreateTempSubdirectory("dash-live-period-recording-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls", "-hls_time", "2",
                "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                "-hls_segment_filename", Path.Combine(root, "media-%d.m4s"), Path.Combine(root, "source.m3u8"));
            File.Copy(Path.Combine(root, "init.mp4"), Path.Combine(root, "next-init.mp4"));
            string Period(string id, int start, string init, params string[] files) => $"""
                <Period id="{id}" start="PT{start}S"><AdaptationSet mimeType="video/mp4">
                  <Representation id="{(changesInit && id == "next" ? "next-v" : "v")}" bandwidth="1000000" codecs="avc1.64001e">
                    <SegmentList duration="2"><Initialization sourceURL="{init}"/>
                      {string.Join('\n', files.Select(file => $"<SegmentURL media=\"{file}\"/>"))}
                    </SegmentList>
                  </Representation>
                </AdaptationSet></Period>
                """;
            await using var server = new MediaFixtureServer(root, (path, version) =>
            {
                if (path != "live.mpd") return null;
                var history = Period("history", 0, "old-init.mp4", "old-media.m4s");
                var current = Period("current", 43200, "init.mp4", version == 0 ? ["media-0.m4s"] : ["media-0.m4s", "media-1.m4s"]);
                // 一次刷新同时发布上一 Period 的尾片和新 Period，二者都应保留。
                var next = version == 0 ? "" : Period("next", 43204, changesInit ? "next-init.mp4" : "init.mp4", "media-2.m4s");
                return $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"dynamic\" minimumUpdatePeriod=\"PT1S\">{history}{current}{next}</MPD>";
            }, responseStatus: (path, _) => recordingFails && path == "media-2.m4s" ? HttpStatusCode.Forbidden : null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.mpd");
            var streams = await extractor.ExtractStreamsAsync();
            Assert.Equal("current", Assert.Single(streams).PeriodId);
            var options = CreateOptions(root);
            options.LiveRealTimeMerge = true;
            options.LiveKeepSegments = false;
            options.DelAfterDone = scenario != "keep-temporary";
            options.SkipMerge = scenario == "skip-merge";
            if (muxFails || muxImport)
            {
                options.MuxAfterDone = true;
                options.MuxOptions = new MuxOptions();
                if (muxFails)
                    options.MuxImports = [new OutputFile { Index = 1, FilePath = Path.Combine(root, "missing.mp4"), Mediainfos = [new Mediainfo { Type = "Video" }] }];
                else
                {
                    var importedFile = Path.Combine(root, "imported.srt");
                    File.WriteAllText(importedFile, "1\n00:00:00,000 --> 00:00:05,000\nKeep external input\n");
                    options.MuxImports = [new OutputFile { Index = 1, FilePath = importedFile }];
                }
            }
            options.LiveRecordLimit = TimeSpan.FromSeconds(6);
            options.LiveIdleTimeout = 5;
            options.DownloadRetryCount = 0;
            string? existingInit = null;
            List<string> createdMetadataFiles = [];
            if (reusedInit)
            {
                var stream = streams[0];
                var name = OtherUtil.GetSafeFileName($"0_{OtherUtil.GetValidFileName(stream.GroupId ?? "", "-")}_{stream.Codecs}_{stream.Bandwidth}_{stream.Language}");
                var track = Directory.CreateDirectory(Path.Combine(root, "tmp", name)).FullName;
                existingInit = Path.Combine(track, "_init.mp4");
                File.Copy(Path.Combine(root, "init.mp4"), existingInit);
                File.WriteAllText(Path.Combine(track, "notes.txt"), "keep");
                File.WriteAllText(Path.Combine(root, "tmp", "meta.json"), "keep metadata");
                extractor.RawFiles["meta.json"] = "new metadata";
                extractor.RawFiles["meta_selected.json"] = "selected metadata";
                options.WriteMetaJson = true;
                createdMetadataFiles = await Program.WriteRawFilesAsync(options, extractor, Path.Combine(root, "tmp"));
            }
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options, CreatedMetadataFiles = createdMetadataFiles }, streams, extractor);
            Assert.Equal(!muxFails && !recordingFails, await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            if (muxFails || recordingFails || keepsInit)
                Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "tmp"), "_init*.mp4", SearchOption.AllDirectories).Length);
            else if (reusedInit)
            {
                Assert.Equal(File.ReadAllBytes(Path.Combine(root, "init.mp4")), File.ReadAllBytes(existingInit!));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(Path.GetDirectoryName(existingInit!)!, "notes.txt")));
                Assert.Single(Directory.GetFiles(Path.Combine(root, "tmp"), "_init*.mp4", SearchOption.AllDirectories));
                Assert.Equal("keep metadata", File.ReadAllText(Path.Combine(root, "tmp", "meta.json")));
                Assert.False(File.Exists(Path.Combine(root, "tmp", "meta_selected.json")));
            }
            else
                Assert.False(Directory.Exists(Path.Combine(root, "tmp")));
            Assert.Equal(0, server.RequestCount("old-init.mp4"));
            Assert.Equal(0, server.RequestCount("old-media.m4s"));
            for (var i = 0; i < 3; i++) Assert.Equal(1, server.RequestCount($"media-{i}.m4s"));
            Assert.Equal(reusedInit ? 0 : 1, server.RequestCount("init.mp4"));
            Assert.Equal(changesInit ? 1 : 0, server.RequestCount("next-init.mp4"));
            if (muxImport)
                Assert.Contains("Keep external input", File.ReadAllText(options.MuxImports![0].FilePath));
            var output = Assert.Single(Directory.GetFiles(options.SaveDir!));
            await AssertVideo(output, recordingFails ? 4 : 6, recordingFails ? 100 : 150);
        }
        finally { Directory.Delete(root, true); }
    }
}
