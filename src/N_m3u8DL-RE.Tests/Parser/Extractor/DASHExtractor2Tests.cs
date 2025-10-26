using Shouldly;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;

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
}