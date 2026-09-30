using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSMultiMapTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VodBindsEachMapToItsMediaWithOrWithoutDiscontinuity(bool discontinuity)
    {
        var extractor = new HLSExtractor(new ParserConfig { Url = "https://example.com/media.m3u8" });
        var streams = await extractor.ExtractStreamsAsync($"""
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MAP:URI="first.mp4"
            #EXTINF:2,
            first.m4s
            {(discontinuity ? "#EXT-X-DISCONTINUITY" : "")}
            #EXT-X-MAP:URI="second.mp4"
            #EXTINF:2,
            second.m4s
            #EXT-X-ENDLIST
            """);
        var playlist = Assert.Single(streams).Playlist!;
        Assert.False(playlist.IsLive);
        Assert.Equal(4, playlist.TotalDuration);
        Assert.Equal(2, playlist.MediaParts.Count);
        Assert.Equal(discontinuity ? new long?[] { 0, 1 } : [0, 0],
            playlist.MediaParts.Select(p => p.DiscontinuitySequence));
        Assert.EndsWith("first.mp4", playlist.MediaParts[0].MediaInit!.Url);
        Assert.EndsWith("first.m4s", Assert.Single(playlist.MediaParts[0].MediaSegments).Url);
        Assert.EndsWith("second.mp4", playlist.MediaParts[1].MediaInit!.Url);
        Assert.EndsWith("second.m4s", Assert.Single(playlist.MediaParts[1].MediaSegments).Url);
    }

    [Fact]
    public async Task DiscontinuitySequenceIsHeaderRatherThanBoundary()
    {
        var streams = await new HLSExtractor(new ParserConfig { Url = "https://example.com/vod.m3u8" })
            .ExtractStreamsAsync("""
            #EXTM3U
            #EXT-X-DISCONTINUITY-SEQUENCE:9
            #EXT-X-MAP:URI="a.mp4"
            #EXTINF:3,
            a.m4s
            #EXT-X-DISCONTINUITY
            #EXT-X-MAP:URI="b.mp4"
            #EXTINF:4,
            b.m4s
            #EXT-X-ENDLIST
            """);
        Assert.Equal(new long?[] { 9, 10 }, streams[0].Playlist!.MediaParts.Select(p => p.DiscontinuitySequence));
    }

    [Fact]
    public void AlignmentUsesCommonDiscontinuityDurationAndKeepsMapBoundaries()
    {
        static MediaPart Part(long sequence, double duration) => new()
        {
            DiscontinuitySequence = sequence, MediaSegments = [new() { Duration = duration }],
        };
        var video = new StreamSpec { Playlist = new() { MediaParts = [Part(0, 1), Part(0, 2), Part(2, 4)] } };
        var audio = new StreamSpec { Playlist = new() { MediaParts = [Part(0, 2.987), Part(2, 4.01)] } };
        VodStreamPlanner.AlignHlsDiscontinuities([video, audio]);
        Assert.Equal(new double?[] { 0, 1, 3 }, video.Playlist.MediaParts.Select(p => p.OutputStart));
        Assert.Equal(new double?[] { 0, 3 }, audio.Playlist.MediaParts.Select(p => p.OutputStart));
        Assert.Equal(3, audio.Playlist.MediaParts[0].OutputDuration);
        Assert.Equal(4.01, video.Playlist.MediaParts[2].OutputDuration);
    }

    [Fact]
    public async Task RepeatedIdenticalMapDoesNotTruncateOrCreateEmptyParts()
    {
        var extractor = new HLSExtractor(new ParserConfig { Url = "https://example.com/media.m3u8" });
        var streams = await extractor.ExtractStreamsAsync("""
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:2,
            first.m4s
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:2,
            second.m4s
            #EXT-X-MAP:URI="unused.mp4"
            #EXT-X-ENDLIST
            """);
        var part = Assert.Single(Assert.Single(streams).Playlist!.MediaParts);
        Assert.EndsWith("init.mp4", part.MediaInit!.Url);
        Assert.Equal(2, part.MediaSegments.Count);
    }

    [Fact]
    public async Task DifferentRangesInSameInitUrlRemainSeparate()
    {
        var extractor = new HLSExtractor(new ParserConfig { Url = "https://example.com/media.m3u8" });
        var streams = await extractor.ExtractStreamsAsync("""
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXT-X-MAP:URI="init.mp4",BYTERANGE="100@0"
            #EXTINF:2,
            first.m4s
            #EXT-X-MAP:URI="init.mp4",BYTERANGE="120@100"
            #EXTINF:2,
            second.m4s
            #EXT-X-ENDLIST
            """);
        var parts = Assert.Single(streams).Playlist!.MediaParts;
        Assert.Equal(2, parts.Count);
        Assert.Equal(0, parts[0].MediaInit!.StartRange);
        Assert.Equal(100, parts[1].MediaInit!.StartRange);
        Assert.Equal(120, parts[1].MediaInit!.ExpectLength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FirstMapDoesNotApplyRetroactivelyToEarlierMedia(bool discontinuity)
    {
        var streams = await new HLSExtractor(new ParserConfig { Url = "https://example.com/vod.m3u8" })
            .ExtractStreamsAsync($"""
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXTINF:2,
            first.ts
            {(discontinuity ? "#EXT-X-DISCONTINUITY" : "")}
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:2,
            second.m4s
            #EXT-X-ENDLIST
            """);
        var parts = streams[0].Playlist!.MediaParts;
        Assert.Equal(2, parts.Count);
        Assert.Null(parts[0].MediaInit);
        Assert.NotNull(parts[1].MediaInit);
    }
    [Fact]
    public async Task VodWithoutEndlistKeepsFinalMediaAndImplicitRangesCrossMaps()
    {
        var streams = await new HLSExtractor(new ParserConfig { Url = "https://example.com/vod.m3u8" })
            .ExtractStreamsAsync("""
            #EXTM3U
            #EXT-X-PLAYLIST-TYPE:VOD
            #EXT-X-MAP:URI="a.mp4"
            #EXTINF:2,
            #EXT-X-BYTERANGE:100@20
            media.mp4
            #EXT-X-MAP:URI="b.mp4"
            #EXTINF:2,
            #EXT-X-BYTERANGE:120
            media.mp4
            """);
        var playlist = streams[0].Playlist!;
        Assert.False(playlist.IsLive);
        Assert.Equal(2, playlist.MediaParts.Count);
        Assert.Equal(120, playlist.MediaParts[1].MediaSegments[0].StartRange);
        Assert.Equal(4, playlist.TotalDuration);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LiveInitWaitsForFirstMediaUnlessAdFilteringIsRequested(bool filter)
    {
        var streams = await new HLSExtractor(new ParserConfig { Url = "https://example.com/live.m3u8" })
            .ExtractStreamsAsync("#EXTM3U\n#EXT-X-MAP:URI=\"init.mp4\"\n");
        Assert.True(streams[0].Playlist!.IsLive);
        FilterUtil.CleanAd(streams, filter ? ["ads"] : null);
        if (filter)
            Assert.Empty(streams[0].Playlist!.MediaParts);
        else
            Assert.NotNull(Assert.Single(streams[0].Playlist!.MediaParts).MediaInit);
    }

}
