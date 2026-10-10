using System.CommandLine;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class LiveCatchupTests
{
    private static readonly DateTimeOffset Origin = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("01:00:00", -3600)]
    [InlineData("25:00:00", -90000)]
    [InlineData("2026-10-10T08:00:00+08:00", 0)]
    [InlineData("2026-10-10T00:00:00Z", 0)]
    [InlineData(" 2026-10-10T00:00:00Z ", 0)]
    public void CommandLineAcceptsLookbackAndAbsoluteTime(string input, int seconds)
    {
        var root = CommandInvoker.CreateRootCommand();
        var result = root.Parse(["https://example.com/live.mpd", "--live-catchup", input]);
        Assert.Empty(result.Errors);
        var option = Assert.IsType<Option<LiveCatchup?>>(root.Options.Single(o => o.Name == "--live-catchup"));
        Assert.Equal(Origin.AddSeconds(seconds), result.GetValue(option)!.Resolve(Origin));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-01:00:00")]
    [InlineData("01:60:00")]
    [InlineData("today")]
    public void CommandLineRejectsInvalidTime(string input)
    {
        Assert.NotEmpty(CommandInvoker.CreateRootCommand().Parse(["https://example.com/live.mpd", "--live-catchup", input]).Errors);
    }

    [Fact]
    public async Task DashUsesPeriodAndPtoAndKeepsOverlappingAudioAndVideo()
    {
        var extractor = new DASHExtractor2(new ParserConfig { Url = "https://example.com/live.mpd" });
        var stream = Assert.Single(await extractor.ExtractStreamsAsync($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" availabilityStartTime="{Origin:O}" timeShiftBufferDepth="PT1H">
              <Period start="PT0S" duration="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1">
                <SegmentTemplate timescale="1000" presentationTimeOffset="10000" media="first-$Time$.m4s">
                  <SegmentTimeline><S t="10000" d="2000" r="1"/></SegmentTimeline>
                </SegmentTemplate></Representation></AdaptationSet></Period>
              <Period start="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1">
                <SegmentTemplate timescale="1000" presentationTimeOffset="90000" media="next-$Time$.m4s">
                  <SegmentTimeline><S t="90000" d="2000" r="1"/></SegmentTimeline>
                </SegmentTemplate></Representation></AdaptationSet></Period>
            </MPD>
            """));
        var window = new LiveCatchupWindow(Origin.AddSeconds(2.5), TimeSpan.FromSeconds(3));
        var audio = HlsStream(0.032, 1, 8);
        window.Validate([stream, audio], Origin.AddSeconds(10));
        Assert.Equal(["https://example.com/first-12000.m4s", "https://example.com/next-90000.m4s"],
            window.Filter(stream, out var videoEnded, Origin.AddSeconds(10)).Select(s => s.Url));
        Assert.Equal([2L, 3L, 4L, 5L], window.Filter(audio, out var audioEnded, Origin.AddSeconds(10)).Select(s => s.Index));
        Assert.True(videoEnded && audioEnded);
    }

    [Fact]
    public void HlsInfersDatesWithinPartAndKeepsFixedEndAcrossRefreshes()
    {
        var stream = HlsStream(0, 2, 2);
        var window = new LiveCatchupWindow(Origin.AddSeconds(2), TimeSpan.FromSeconds(4));
        window.Validate([stream], Origin.AddSeconds(10));
        Assert.Equal([1L], window.Filter(stream, out var ended, Origin.AddSeconds(10)).Select(s => s.Index));
        Assert.False(ended);
        stream.Playlist!.MediaParts[0].MediaSegments.AddRange([new() { Index = 2, Duration = 2 }, new() { Index = 3, Duration = 2 }]);
        Assert.Empty(window.Filter(stream, out ended, Origin.AddSeconds(3)));
        Assert.False(ended);
        Assert.Equal([1L, 2L], window.Filter(stream, out ended, Origin.AddSeconds(6)).Select(s => s.Index));
        Assert.True(ended);
    }

    [Fact]
    public void RejectsExpiredFutureMissingTimeAndVodInsteadOfUsingLatestMedia()
    {
        var stream = HlsStream(0, 2, 10);
        stream.Playlist!.TimeShiftBufferDepth = TimeSpan.FromSeconds(10);
        var now = Origin.AddSeconds(20);
        Assert.Throws<ArgumentException>(() => new LiveCatchupWindow(Origin.AddSeconds(9), null).Validate([stream], now));
        new LiveCatchupWindow(Origin.AddSeconds(10), null).Validate([stream], now);
        Assert.Throws<ArgumentException>(() => new LiveCatchupWindow(now.AddSeconds(1), null).Validate([stream], now));
        stream.Playlist.MediaParts[0].MediaSegments[0].DateTime = null;
        Assert.Throws<ArgumentException>(() => new LiveCatchupWindow(Origin.AddSeconds(10), null).Validate([stream], now));
        stream.Playlist.IsLive = false;
        Assert.Throws<ArgumentException>(() => new LiveCatchupWindow(Origin.AddSeconds(10), null).Validate([stream], now));
    }

    private static StreamSpec HlsStream(double offset, double duration, int count) => new()
    {
        MediaType = MediaType.AUDIO,
        Playlist = new Playlist
        {
            IsLive = true,
            MediaParts = [new MediaPart
            {
                MediaSegments = Enumerable.Range(0, count).Select(i => new MediaSegment
                {
                    Index = i, Duration = duration,
                    DateTime = i == 0 ? Origin.AddSeconds(offset).UtcDateTime : null,
                }).ToList()
            }]
        }
    };
}
