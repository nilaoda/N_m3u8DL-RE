using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class DASHHexNumberTests
{
    [Theory]
    [InlineData(false, "x", "000059d7", "000059d8", "000059d9")]
    [InlineData(true, "x", "000059d7", "000059d8", "000059d9")]
    [InlineData(false, "X", "000059D7", "000059D8", "000059D9")]
    [InlineData(true, "X", "000059D7", "000059D8", "000059D9")]
    public async Task ExtractStreams_ExpandsHexNumber(bool timeline, string format,
        string first, string second, string third)
    {
        var content = $$"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6.144S">
              <Period>
                <AdaptationSet mimeType="audio/mp4" contentType="audio">
                  <SegmentTemplate timescale="1000" duration="2048" startNumber="22999"
                      initialization="audio/$RepresentationID$/init.mp4"
                      media="audio/$RepresentationID$/$Number%08{{format}}$.m4s">
                    {{(timeline ? "<SegmentTimeline><S t=\"0\" d=\"2048\" r=\"2\" /></SegmentTimeline>" : "")}}
                  </SegmentTemplate>
                  <Representation id="a1" bandwidth="128000" codecs="mp4a.40.2" />
                </AdaptationSet>
              </Period>
            </MPD>
            """;
        var extractor = new DASHExtractor2(new ParserConfig
        {
            Url = "https://example.com/manifest.mpd",
            OriginalUrl = "https://example.com/manifest.mpd"
        });
        var streams = await extractor.ExtractStreamsAsync(content);
        var playlist = streams.Single().Playlist!;
        playlist.MediaInit!.Url.ShouldBe("https://example.com/audio/a1/init.mp4");
        playlist.MediaParts.Single().MediaSegments.Select(s => s.Url).ShouldBe(new[]
        {
            $"https://example.com/audio/a1/{first}.m4s",
            $"https://example.com/audio/a1/{second}.m4s",
            $"https://example.com/audio/a1/{third}.m4s"
        });
        playlist.MediaParts.Single().MediaSegments.Select(s => s.Index).ShouldBe(new long[] { 0, 1, 2 });
        playlist.TotalDuration.ShouldBe(6.144);
    }
}
