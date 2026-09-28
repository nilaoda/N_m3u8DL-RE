using Shouldly;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using System.Globalization;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class DASHExtractor2Tests
{
    private static ParserConfig CreateTestConfig(string mpdFileName) => new ParserConfig
    {
        OriginalUrl = $"file:///fake/path/to/{mpdFileName}",
    };
    
    [Fact]
    public async Task DASHExtractor2_Normal()
    {
        const string mpdName = "Dash.Manifest_1080p.mpd";
        var config = CreateTestConfig(mpdName);
        var content = ResourceHelper.Read(mpdName);
        var extractor = new DASHExtractor2(config);
        var results = await extractor.ExtractStreamsAsync(content);
        results.ShouldNotBeNull();
        results.Count.ShouldBe(23);

        var first = results.First();
        first.ToString().ShouldBe("[aqua]Vid[/] 512x288 | 386 Kbps | 1 | avc1.64001f | 184 Segments | Main | ~12m16s");
        first.AudioId.ShouldBe("15");
        first.Bandwidth.ShouldBe(386437);
        first.Extension.ShouldBe("m4s");
        first.Language.ShouldBe("und");
        first.SubtitleId.ShouldBe("25");
        first.Playlist.ShouldNotBeNull();
        first.Playlist.IsLive.ShouldBe(false);
        first.Playlist.TotalDuration.ShouldBe(736);
        first.Playlist.MediaInit.ShouldNotBeNull();
        first.Playlist.MediaInit.Url.ShouldBe("1/init.mp4");
    }

    [Fact]
    public async Task DASHExtractor2_RemovesDuplicateSegments()
    {
        // 该MPD含有一个重复引用的分片(seg-2.m4s 出现两次), 解析后应只保留一次 (#684)
        const string mpdName = "Dash.Manifest_DuplicateSegments.mpd";
        var config = CreateTestConfig(mpdName);
        var content = ResourceHelper.Read(mpdName);
        var extractor = new DASHExtractor2(config);
        var results = await extractor.ExtractStreamsAsync(content);

        results.ShouldNotBeNull();
        results.Count.ShouldBe(1);

        var segments = results.First().Playlist!.MediaParts[0].MediaSegments;
        // 原始MPD有4个SegmentURL(seg-2重复), 去重后应为3个
        segments.Count.ShouldBe(3);
        // 顺序保持不变
        segments.Select(s => s.Url).ShouldBe(new[] { "seg-1.m4s", "seg-2.m4s", "seg-3.m4s" });
    }

    [Theory]
    // America/New_York spring-forward and fall-back transitions, expressed in UTC.
    [InlineData("2026-03-08T06:59:00Z", "2026-03-08T07:01:00Z")]
    [InlineData("2026-11-01T05:59:00Z", "2026-11-01T06:01:00Z")]
    // The same instants with explicit offsets on either side of each transition.
    [InlineData("2026-03-08T01:59:00-05:00", "2026-03-08T03:01:00-04:00")]
    [InlineData("2026-11-01T01:59:00-04:00", "2026-11-01T01:01:00-05:00")]
    [InlineData("2026-03-08T12:29:00+05:30", "2026-03-08T07:01:00Z")]
    // Controls with no offset change.
    [InlineData("2026-03-07T01:59:00-05:00", "2026-03-07T02:01:00-05:00")]
    [InlineData("2026-07-01T01:59:00-04:00", "2026-07-01T02:01:00-04:00")]
    public async Task DASHExtractor2_LiveSegmentTemplate_UsesElapsedTime(
        string availabilityStartTime, string now)
    {
        var config = new ParserConfig { Url = "https://example.com/live/manifest.mpd" };
        var clock = new FixedTimeProvider(DateTimeOffset.Parse(now, CultureInfo.InvariantCulture));
        var extractor = new DASHExtractor2(config, clock);
        var content = $$"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic"
                 availabilityStartTime="{{availabilityStartTime}}" timeShiftBufferDepth="PT30S">
              <Period>
                <AdaptationSet mimeType="video/mp4">
                  <Representation id="video" bandwidth="1000000">
                    <SegmentTemplate timescale="1" duration="6" startNumber="1"
                                     initialization="init.mp4" media="seg-$Number$.m4s" />
                  </Representation>
                </AdaptationSet>
              </Period>
            </MPD>
            """;

        var results = await extractor.ExtractStreamsAsync(content);

        var playlist = results.ShouldHaveSingleItem().Playlist!;
        playlist.IsLive.ShouldBeTrue();
        var segments = playlist.MediaParts.ShouldHaveSingleItem().MediaSegments;
        // Every case has 120 seconds of elapsed time: the last 30 seconds start at segment 16.
        segments.Count.ShouldBe(5);
        segments.Select(s => s.Url).ShouldBe(Enumerable.Range(16, 5)
            .Select(number => $"https://example.com/live/seg-{number}.m4s"));
        segments.Select(s => s.Index).ShouldBe(Enumerable.Range(16, 5).Select(number => (long)number));
        segments.ShouldAllBe(s => s.Duration == 6);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}