using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class LiveCatchupRecordingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [InlineData(false, true)]
    public async Task RecordsRequestedHistoryAcrossPeriodsAndWaitsOnlyForUnpublishedMedia(bool needsRefresh, bool subtitles = false)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe")) return;
        var root = Directory.CreateTempSubdirectory("live-catchup-").FullName;
        try
        {
            await GenerateCutMedia(root);
            File.Copy(Path.Combine(root, "init.mp4"), Path.Combine(root, "next-init.mp4"));
            var origin = DateTimeOffset.UtcNow.AddMinutes(-1);
            await using var server = new MediaFixtureServer(root, (path, version) =>
            {
                if (path != "live.mpd") return null;
                var complete = !needsRefresh || version > 0;
                return $"""
                    <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" availabilityStartTime="{origin:O}" timeShiftBufferDepth="PT1H" minimumUpdatePeriod="PT1S">
                      <Period id="first" start="PT0S" duration="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                        <SegmentList duration="2"><Initialization sourceURL="init.mp4"/><SegmentURL media="media-0.m4s"/>
                          {(complete ? "<SegmentURL media=\"media-1.m4s\"/>" : "")}
                        </SegmentList></Representation></AdaptationSet>
                        <AdaptationSet mimeType="application/mp4"><Representation id="s" codecs="stpp">
                          <SegmentList duration="2"/>
                        </Representation></AdaptationSet></Period>
                      {(complete ? """
                        <Period id="next" start="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                          <SegmentList duration="2"><Initialization sourceURL="next-init.mp4"/><SegmentURL media="media-2.m4s"/>
                          </SegmentList></Representation></AdaptationSet>
                          <AdaptationSet mimeType="application/mp4"><Representation id="s" codecs="stpp">
                            <SegmentList duration="2"/>
                          </Representation></AdaptationSet></Period>
                        """ : "")}
                    </MPD>
                    """;
            });
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.mpd");
            var streams = await extractor.ExtractStreamsAsync();
            var options = CreateOptions(root);
            options.LiveCatchup = LiveCatchup.Parse(origin.AddSeconds(2.5).ToString("yyyy-MM-dd'T'HH:mm:sszzz"));
            options.LiveCatchupStart = origin.AddSeconds(2.5);
            options.LiveRecordLimit = TimeSpan.FromSeconds(3);
            options.LiveRealTimeMerge = true;
            options.MuxAfterDone = true;
            options.MuxOptions = new MuxOptions();
            var subtitle = Path.Combine(root, "imported.srt");
            const string text = "1\n00:00:04,100 --> 00:00:05,000\nCatch-up subtitle\n";
            if (subtitles)
            {
                await File.WriteAllTextAsync(subtitle, text);
                options.MuxImports = [new OutputFile { Index = 2, FilePath = subtitle }];
            }
            options.LiveTakeCount = 1; // 回看不能被默认的尾片选择截断。
            options.LiveWaitTime = 1;
            options.LiveIdleTimeout = 6;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.Equal(0, server.RequestCount("media-0.m4s"));
            Assert.Equal(1, server.RequestCount("media-1.m4s"));
            Assert.Equal(1, server.RequestCount("media-2.m4s"));
            Assert.Equal(needsRefresh ? 2 : 1, server.RequestCount("live.mpd"));
            var output = Assert.Single(Directory.GetFiles(options.SaveDir!));
            await AssertVideo(output, 4, 100);
            if (subtitles)
            {
                var extracted = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
                Assert.Contains("00:00:02,100 --> 00:00:03,000", extracted);
                Assert.Contains("Catch-up subtitle", extracted);
                Assert.Equal(text, await File.ReadAllTextAsync(subtitle));
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
