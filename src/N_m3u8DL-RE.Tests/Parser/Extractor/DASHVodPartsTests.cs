using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class DASHVodPartsTests
{
    [Theory]
    [InlineData("video-1", "video-2")]
    [InlineData("video", "video")]
    public async Task SelectionSpansPeriodsWithoutLosingInitOrKid(string firstId, string secondId)
    {
        var streams = await Parse($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="static" mediaPresentationDuration="PT8S">
              {Period("p0", firstId, "first", "11111111-1111-1111-1111-111111111111")}
              {Period("p1", secondId, "second", "22222222-2222-2222-2222-222222222222")}
            </MPD>
            """);
        Assert.Equal(2, streams.Count);
        var plan = Assert.Single(VodStreamPlanner.Build(streams, [streams[0]]));
        var parts = plan.Playlist!.MediaParts;
        Assert.Equal(2, parts.Count);
        Assert.EndsWith("first-init.mp4", parts[0].MediaInit!.Url);
        Assert.EndsWith("second-init.mp4", parts[1].MediaInit!.Url);
        Assert.Equal(new string('1', 32), parts[0].MediaInit!.EncryptInfo.KID);
        Assert.Equal(new string('2', 32), parts[1].MediaInit!.EncryptInfo.KID);
        Assert.Equal([0, 1, 2, 3], parts.SelectMany(p => p.MediaSegments).Select(s => s.Index));
        // 编排不能改变原始流，其它画质的选择还会用到这些对象。
        Assert.Equal(0, streams[1].Playlist!.MediaParts[0].MediaSegments[0].Index);
    }

    [Fact]
    public async Task MissingPeriodIdsStillUseDistinctPeriodOrderAndInferDurationFromStarts()
    {
        var streams = await Parse("""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              <Period start="PT0S"><AdaptationSet mimeType="video/mp4"><Representation id="v" bandwidth="1">
                <SegmentTemplate duration="2" initialization="p0-init.mp4" media="p0-$Number$.m4s" />
              </Representation></AdaptationSet></Period>
              <Period start="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v" bandwidth="1">
                <SegmentTemplate duration="2" initialization="p1-init.mp4" media="p1-$Number$.m4s" />
              </Representation></AdaptationSet></Period>
            </MPD>
            """);
        Assert.Equal(2, streams.Count);
        Assert.All(streams, stream => Assert.Equal(4, stream.Playlist!.TotalDuration));
        var plan = Assert.Single(VodStreamPlanner.Build(streams, [streams[0]]));
        Assert.Equal(new int?[] { 0, 1 }, plan.Playlist!.MediaParts.Select(p => p.PeriodIndex));
        Assert.Equal(8, plan.Playlist.TotalDuration);
    }

    [Fact]
    public async Task PeriodFilterDoesNotReintroduceExcludedPeriods()
    {
        var streams = await Parse($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              {Period("ad", "v1", "first")}{Period("main", "v2", "second")}
            </MPD>
            """);
        var filter = new StreamFilter { PeriodIdReg = new Regex("^main$"), For = "best" };
        var selected = FilterUtil.DoFilterKeep(streams, filter);
        var plan = Assert.Single(VodStreamPlanner.Build(streams, selected, videoFilter: filter));
        Assert.Equal("main", Assert.Single(plan.Playlist!.MediaParts).PeriodId);
    }

    [Fact]
    public void AlignPeriodsUsesCommonDurationAndClosesRemovedAdPeriod()
    {
        static MediaPart Part(int period, double duration, double manifestDuration) => new()
        {
            PeriodIndex = period, PeriodDuration = manifestDuration,
            MediaSegments = [new() { Duration = duration }],
        };
        var video = new StreamSpec { Playlist = new Playlist { MediaParts = [Part(0, 4.1, 4), Part(2, 4, 4)] } };
        var audio = new StreamSpec { MediaType = MediaType.AUDIO,
            Playlist = new Playlist { MediaParts = [Part(0, 4.05, 4), Part(2, 4.05, 4)] } };
        VodStreamPlanner.AlignPeriods([video, audio]);
        Assert.Equal(new double?[] { 0, 4 }, video.Playlist.MediaParts.Select(p => p.OutputStart));
        Assert.Equal(new double?[] { 0, 4 }, audio.Playlist.MediaParts.Select(p => p.OutputStart));
        Assert.All(audio.Playlist.MediaParts, part => Assert.Equal(4, part.OutputDuration));
    }

    [Fact]
    public void SharedInpointKeepsTheVideoFrameOffsetRelativeToAudio()
    {
        var video = new StreamSpec { Playlist = new() { MediaParts = [new()
        {
            PeriodIndex = 0, PresentationTimeOffset = 5,
            MediaSegments = [new() { PresentationTime = 7.04, Duration = 2 }],
        }] } };
        var audio = new StreamSpec { Playlist = new() { MediaParts = [new()
        {
            PeriodIndex = 0, PresentationTimeOffset = 10,
            MediaSegments = [new() { PresentationTime = 12, Duration = 2 }],
        }] } };
        VodStreamPlanner.AlignPeriods([video, audio]);
        Assert.Equal(7, video.Playlist.MediaParts[0].OutputInpoint);
        Assert.Equal(12, audio.Playlist.MediaParts[0].OutputInpoint);
    }

    private static Task<List<StreamSpec>> Parse(string text) => new DASHExtractor2(new ParserConfig
    {
        Url = "https://example.com/manifest.mpd",
        OriginalUrl = "https://example.com/manifest.mpd",
    }).ExtractStreamsAsync(text);

    [Fact]
    public async Task MultipleSelectedQualitiesDoNotReuseTheSameLaterRepresentation()
    {
        var streams = await Parse("""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              <Period duration="PT4S"><AdaptationSet mimeType="video/mp4">
                <SegmentTemplate duration="2" initialization="$RepresentationID$-init.mp4" media="$RepresentationID$-$Number$.m4s"/>
                <Representation id="high0" bandwidth="1000000" width="1920" height="1080" codecs="avc1.640028"/>
                <Representation id="low0" bandwidth="500000" width="1280" height="720" codecs="avc1.640028"/>
              </AdaptationSet></Period>
              <Period duration="PT4S"><AdaptationSet mimeType="video/mp4">
                <SegmentTemplate duration="2" initialization="$RepresentationID$-init.mp4" media="$RepresentationID$-$Number$.m4s"/>
                <Representation id="high1" bandwidth="1000000" width="1920" height="1080" codecs="avc1.640028"/>
                <Representation id="low1" bandwidth="500000" width="1280" height="720" codecs="avc1.640028"/>
              </AdaptationSet></Period>
            </MPD>
            """);
        var plans = VodStreamPlanner.Build(streams, streams);
        Assert.Equal(2, plans.Count);
        Assert.Equal(["high0", "high1"], plans[0].Playlist!.MediaParts.Select(p => p.RepresentationId));
        Assert.Equal(["low0", "low1"], plans[1].Playlist!.MediaParts.Select(p => p.RepresentationId));
    }

    [Fact]
    public async Task IncompatibleCodecFailsInsteadOfSilentlyDroppingAPeriod()
    {
        var streams = await Parse($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              {Period("p0", "v0", "a")}{Period("p1", "v1", "b")}
            </MPD>
            """);
        streams[1].Codecs = "hvc1.1.6.L93";
        Assert.Throws<NotSupportedException>(() => VodStreamPlanner.Build(streams, [streams[0]]));
    }

    [Fact]
    public async Task BestSelectionDoesNotCompareVideoWithHigherBitrateAudio()
    {
        var streams = await Parse($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              {Period("p0", "v0", "a")}{Period("p1", "v1", "b")}
            </MPD>
            """);
        var audio0 = streams[0].WithPlaylist(streams[0].Playlist!);
        audio0.MediaType = MediaType.AUDIO; audio0.Bandwidth = 2000000;
        var audio1 = streams[1].WithPlaylist(streams[1].Playlist!);
        audio1.MediaType = MediaType.AUDIO; audio1.Bandwidth = 2000000;
        var plan = Assert.Single(VodStreamPlanner.Build([..streams, audio0, audio1], [streams[0]],
            videoFilter: new StreamFilter { For = "best" }));
        Assert.Equal(2, plan.Playlist!.MediaParts.Count);
    }


    [Fact]
    public async Task BestSelectionPrefersCompatibleCodecBeforeBitrate()
    {
        var streams = await Parse($"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              {Period("p0", "v0", "a")}{Period("p1", "v1", "b")}
            </MPD>
            """);
        var hevc = streams[1].WithPlaylist(streams[1].Playlist!);
        hevc.Codecs = "hvc1.1.6.L93"; hevc.Bandwidth = 2000000;
        var plan = Assert.Single(VodStreamPlanner.Build([..streams, hevc], [streams[0]],
            videoFilter: new StreamFilter { For = "best" }));
        Assert.Equal(2, plan.Playlist!.MediaParts.Count);
        Assert.Equal("v1", plan.Playlist.MediaParts[1].RepresentationId);
    }

    [Fact]
    public async Task ImplicitTemplateTimeIncludesPtoAfterRangeSelection()
    {
        var streams = await Parse("""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT4S">
              <Period duration="PT4S"><AdaptationSet mimeType="video/mp4"><Representation id="v">
                <SegmentTemplate presentationTimeOffset="5" duration="2" initialization="init.mp4" media="$Number$.m4s"/>
              </Representation></AdaptationSet></Period>
            </MPD>
            """);
        FilterUtil.ApplyCustomRange(streams, new CustomRange { StartSegIndex = 1, EndSegIndex = 1, InputStr = "1-1" });
        VodStreamPlanner.AlignPeriods(streams);
        var part = Assert.Single(streams[0].Playlist!.MediaParts);
        Assert.Equal(7, part.OutputInpoint);
        Assert.Equal(2, part.OutputDuration);
    }


    [Fact]
    public async Task NegativeRepeatsStopAtNextTimelineEntryAndPeriodEndWithPto()
    {
        var streams = await Parse("""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT8S">
              <Period duration="PT8S"><AdaptationSet mimeType="video/mp4"><Representation id="v">
                <SegmentTemplate presentationTimeOffset="10" timescale="1" initialization="init.mp4" media="$Time$.m4s">
                  <SegmentTimeline><S t="10" d="2" r="-1"/><S t="14" d="2" r="-1"/></SegmentTimeline>
                </SegmentTemplate>
              </Representation></AdaptationSet></Period>
            </MPD>
            """);
        var segments = streams[0].Playlist!.MediaParts[0].MediaSegments;
        Assert.Equal(new double?[] { 10, 12, 14, 16 }, segments.Select(segment => segment.PresentationTime));
        Assert.Equal(8, streams[0].Playlist!.TotalDuration);
    }

    private static string Period(string periodId, string representation, string prefix, string? kid = null) => $"""
        <Period id="{periodId}" duration="PT4S"><AdaptationSet mimeType="video/mp4">
          {(kid == null ? "" : $"<ContentProtection cenc:default_KID=\"{kid}\" />")}
          <Representation id="{representation}" bandwidth="1000000" width="1920" height="1080" codecs="avc1.640028">
            <SegmentTemplate duration="2" initialization="{prefix}-init.mp4" media="{prefix}-$Number$.m4s" />
          </Representation>
        </AdaptationSet></Period>
        """;
}
