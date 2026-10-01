using System.Globalization;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSDurationTests
{
    [Theory]
    [InlineData("-86390", false)]
    [InlineData("NaN", false)]
    [InlineData("Infinity", false)]
    [InlineData("1e400", false)]
    [InlineData("1e100", false)]
    [InlineData("invalid", false)]
    [InlineData("-86390", true)]
    public async Task InvalidDurationUsesTargetDurationAndKeepsMedia(string duration, bool ended)
    {
        var stream = Assert.Single(await CreateExtractor().ExtractStreamsAsync($"""
            #EXTM3U
            #EXT-X-TARGETDURATION:2
            #EXTINF:0.5,
            first.ts
            #EXTINF:{duration},
            second.ts
            #EXTINF:0,
            compatibility.ts
            {(ended ? "#EXT-X-ENDLIST" : "")}
            """));
        var segments = stream.Playlist!.MediaParts.SelectMany(part => part.MediaSegments).ToList();
        Assert.Equal(3, segments.Count);
        Assert.Equal([0.5, 2, 0], segments.Select(segment => segment.Duration));
        Assert.EndsWith("second.ts", segments[1].Url);
        Assert.Equal(!ended, stream.Playlist.IsLive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#EXT-X-TARGETDURATION:-1")]
    [InlineData("#EXT-X-TARGETDURATION:NaN")]
    [InlineData("#EXT-X-TARGETDURATION:Infinity")]
    [InlineData("#EXT-X-TARGETDURATION:1e100")]
    public async Task MissingValidTargetUsesNormalSegmentFromAnotherPart(string target)
    {
        var stream = Assert.Single(await CreateExtractor().ExtractStreamsAsync($"""
            #EXTM3U
            {target}
            #EXTINF:0.5,
            first.ts
            #EXT-X-DISCONTINUITY
            #EXTINF:-86399.5,
            second.ts
            """));
        Assert.Null(stream.Playlist!.TargetDuration);
        Assert.Equal([0.5, 0.5], stream.Playlist.MediaParts.SelectMany(part => part.MediaSegments).Select(segment => segment.Duration));
    }

    [Fact]
    public async Task AllInvalidDurationsWithoutFallbackFailClearly()
    {
        var error = await Assert.ThrowsAsync<FormatException>(() => CreateExtractor().ExtractStreamsAsync("""
            #EXTM3U
            #EXTINF:NaN,
            first.ts
            """));
        Assert.Equal(ResString.hlsInvalidDuration, error.Message);
    }

    [Fact]
    public async Task MidnightAndDecimalDurationsRemainCorrectUnderAnotherCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var stream = Assert.Single(await CreateExtractor().ExtractStreamsAsync("""
                #EXTM3U
                #EXT-X-TARGETDURATION:1
                #EXT-X-PROGRAM-DATE-TIME:2026-10-01T23:59:59.5+08:00
                #EXTINF:0.5,
                first.ts
                #EXT-X-PROGRAM-DATE-TIME:2026-10-02T00:00:00+08:00
                #EXTINF:0.5,
                second.ts
                """));
            var segments = stream.Playlist!.MediaParts[0].MediaSegments;
            Assert.Equal(1, segments.Sum(segment => segment.Duration));
            Assert.Equal(TimeSpan.FromMilliseconds(500), segments[1].DateTime - segments[0].DateTime);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    private static HLSExtractor CreateExtractor() => new(new ParserConfig { Url = "https://example.com/live.m3u8" });
}
