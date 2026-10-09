using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class DASHLivePeriodsTests
{
    [Theory]
    [InlineData("old", "current")]
    [InlineData("", "")]
    [InlineData("same", "same")]
    public async Task InitialWindowEndsAtLatestPeriodRegardlessOfPeriodIds(string firstId, string lastId)
    {
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" });
        var streams = await extractor.ExtractStreamsAsync(Manifest(
            Period(firstId, 0, 0, 4) + Period(lastId, 8, 8, 4)));
        var stream = Assert.Single(streams);
        Assert.Equal(lastId, stream.PeriodId);
        Assert.Equal(1, stream.Playlist!.MediaParts[^1].PeriodIndex);
        FilterUtil.SyncStreams(streams, 3);
        Assert.Equal(["https://example.com/media-10.m4s", "https://example.com/media-12.m4s", "https://example.com/media-14.m4s"],
            stream.Playlist.MediaParts[0].MediaSegments.Select(s => s.Url));
    }

    [Fact]
    public async Task AudioAndVideoUseCommonWindowWithDifferentSegmentDurations()
    {
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" });
        var current = Period("current", 8, 8, 4).Replace("</Period>", """
            <AdaptationSet mimeType="audio/mp4"><Representation id="a" codecs="mp4a.40.2">
              <SegmentTemplate timescale="1000" presentationTimeOffset="8000" media="audio-$Time$.m4s">
                <SegmentTimeline><S t="12032" d="1000" r="3"/></SegmentTimeline>
              </SegmentTemplate>
            </Representation></AdaptationSet></Period>
            """);
        var streams = await extractor.ExtractStreamsAsync(Manifest(Period("old", 0, 0, 4) + current));
        FilterUtil.SyncStreams(streams, 3);
        Assert.Equal(["https://example.com/media-12.m4s", "https://example.com/media-14.m4s"],
            streams.Single(s => s.GroupId == "v").Playlist!.MediaParts.SelectMany(p => p.MediaSegments).Select(s => s.Url));
        Assert.Equal(3, streams.Single(s => s.GroupId == "a").SegmentsCount);
    }

    [Fact]
    public async Task OpenEndedLiveTimelineUsesCompletedSegmentsWithinBufferWindow()
    {
        var now = new DateTimeOffset(2026, 10, 10, 0, 0, 21, TimeSpan.Zero);
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" }, new FixedTimeProvider(now));
        var content = Manifest(Period("current", 8, 8, 1)).Replace("duration=\"PT8S\"", "")
            .Replace("r=\"0\"", "r=\"-1\"").Replace("type=\"dynamic\"",
                "type=\"dynamic\" availabilityStartTime=\"2026-10-10T00:00:00Z\" timeShiftBufferDepth=\"PT6S\"");
        var stream = Assert.Single(await extractor.ExtractStreamsAsync(content));
        FilterUtil.SyncStreams([stream], 2);
        Assert.Equal(["https://example.com/media-16.m4s", "https://example.com/media-18.m4s"],
            stream.Playlist!.MediaParts.SelectMany(p => p.MediaSegments).Select(s => s.Url));
    }

    [Fact]
    public async Task EmptyOrFuturePeriodDoesNotDisplaceAvailableMedia()
    {
        var now = new DateTimeOffset(2026, 10, 10, 0, 0, 10, TimeSpan.Zero);
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" }, new FixedTimeProvider(now));
        var content = Manifest(Period("current", 0, 0, 4) + Period("empty", 8, 8, 0) + Period("future", 20, 20, 2))
            .Replace("type=\"dynamic\"", "type=\"dynamic\" availabilityStartTime=\"2026-10-10T00:00:00Z\"");
        var stream = Assert.Single(await extractor.ExtractStreamsAsync(content));
        Assert.Equal("current", stream.PeriodId);
        Assert.Equal(4, stream.SegmentsCount);

        var empty = Assert.Single(await extractor.ExtractStreamsAsync(Manifest(Period("empty", 0, 0, 0))));
        Assert.Equal(0, empty.SegmentsCount);
        Assert.NotNull(empty.Playlist!.MediaParts[0].MediaInit);
    }

    [Theory]
    [InlineData("video/mp4", "avc1.64001e")]
    [InlineData("application/mp4", "stpp")]
    public async Task CurrentPeriodKeepsEmptyTrackButDropsHistoricalQuality(string mimeType, string codecs)
    {
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" });
        var old = Period("old", 0, 0, 2).Replace("</AdaptationSet>", """
            <Representation id="old-high" bandwidth="2000000" codecs="avc1.64001e">
              <SegmentTemplate media="old-$Number$.m4s" duration="2"/>
            </Representation></AdaptationSet>
            """);
        var current = Period("current", 4, 4, 0).Replace("</Period>", """
            <AdaptationSet mimeType="audio/mp4"><Representation id="a" codecs="mp4a.40.2">
              <SegmentList duration="2"><SegmentURL media="audio.m4s"/></SegmentList>
            </Representation></AdaptationSet></Period>
            """);
        var streams = await extractor.ExtractStreamsAsync(Manifest(old + current)
            .Replace("mimeType=\"video/mp4\"", $"mimeType=\"{mimeType}\"").Replace("avc1.64001e", codecs));
        Assert.Equal(2, streams.Count);
        Assert.All(streams, s => Assert.Equal("current", s.PeriodId));
        FilterUtil.SyncStreams(streams);
        Assert.Equal(0, streams.Single(s => s.GroupId == "v").SegmentsCount);
        if (codecs == "stpp")
            Assert.Equal("m4s", streams.Single(s => s.GroupId == "v").Extension);
        Assert.DoesNotContain(streams, s => s.GroupId == "old-high");
    }

    [Fact]
    public async Task RefreshAcrossPeriodBoundaryKeepsUnrecordedTail()
    {
        var root = Directory.CreateTempSubdirectory("dash-live-period-refresh-").FullName;
        try
        {
            var initial = Manifest(Period("p0", 0, 0, 2));
            var updated = Manifest(Period("p0", 0, 0, 3) + Period("p1", 4, 4, 3));
            await using var server = new MediaFixtureServer(root, (path, _) => path == "live.mpd" ? updated : null);
            var extractor = new DASHExtractor2(new ParserConfig { Url = server.Url + "live.mpd" });
            var streams = await extractor.ExtractStreamsAsync(initial);
            var stream = Assert.Single(streams);
            var tracker = new LiveSegmentTracker();
            tracker.Record(stream.Playlist!.MediaParts.SelectMany(p => p.MediaSegments).ToList());
            await extractor.RefreshPlayListAsync(streams);
            Assert.Equal("p1", stream.PeriodId);
            var segments = tracker.Filter(stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).ToList(), false);
            Assert.Equal([server.Url + "media-4.m4s", server.Url + "media-6.m4s", server.Url + "media-8.m4s"],
                segments.Select(s => s.Url));
            tracker.Record(segments);
            await extractor.RefreshPlayListAsync(streams);
            Assert.Empty(tracker.Filter(stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).ToList(), false));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PeriodsKeepTheirOwnInitialization()
    {
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" });
        var streams = await extractor.ExtractStreamsAsync(Manifest(
            Period("p0", 0, 0, 2) + Period("p1", 4, 4, 2).Replace("init.mp4", "new-init.mp4")));
        var parts = Assert.Single(streams).Playlist!.MediaParts;
        Assert.Equal(2, parts.Count);
        Assert.Equal("https://example.com/init.mp4", parts[0].MediaInit!.Url);
        var part = parts[1];
        Assert.Equal("https://example.com/new-init.mp4", part.MediaInit!.Url);
        Assert.Equal(["https://example.com/media-4.m4s", "https://example.com/media-6.m4s"],
            part.MediaSegments.Select(s => s.Url));
    }

    private static string Manifest(string periods) => $"""
        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" minimumUpdatePeriod="PT1S">{periods}</MPD>
        """;

    private static string Period(string id, int start, int time, int count) => $"""
        <Period id="{id}" start="PT{start}S" duration="PT8S"><AdaptationSet mimeType="video/mp4">
          <Representation id="v" bandwidth="1000000" codecs="avc1.64001e">
            <SegmentTemplate initialization="init.mp4" media="media-$Time$.m4s" presentationTimeOffset="{time}">
              <SegmentTimeline>{(count > 0 ? $"<S t=\"{time}\" d=\"2\" r=\"{count - 1}\"/>" : "")}</SegmentTimeline>
            </SegmentTemplate>
          </Representation>
        </AdaptationSet></Period>
        """;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
