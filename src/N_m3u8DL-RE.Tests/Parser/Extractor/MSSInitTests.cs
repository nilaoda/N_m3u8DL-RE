using System.Text;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class MSSInitTests
{
    [Fact]
    public async Task QualityLevelsOwnTheirGeneratedInitAndFilteringRemovesOrphans()
    {
        var streams = await new MSSExtractor(new ParserConfig { Url = "https://example.com/vod.ism/Manifest" })
            .ExtractStreamsAsync("""
            <SmoothStreamingMedia MajorVersion="2" MinorVersion="1" Duration="40000000" TimeScale="10000000">
              <StreamIndex Type="audio" Name="audio" Url="QualityLevels({bitrate})/Fragments(audio={start time})">
                <QualityLevel Index="0" Bitrate="128000" FourCC="AACL" SamplingRate="48000" Channels="2" CodecPrivateData="1190"/>
                <QualityLevel Index="1" Bitrate="64000" FourCC="AACL" SamplingRate="44100" Channels="2" CodecPrivateData="1210"/>
                <c t="0" d="20000000" r="2" />
              </StreamIndex>
            </SmoothStreamingMedia>
            """);
        Assert.Equal(2, streams.Count);
        var first = Assert.Single(streams[0].Playlist!.MediaParts).MediaInit!;
        var second = Assert.Single(streams[1].Playlist!.MediaParts).MediaInit!;
        Assert.StartsWith("base64://", first.Url);
        Assert.NotEqual(first.Url, second.Url);
        Assert.Contains("ftyp", Encoding.ASCII.GetString(Convert.FromBase64String(first.Url[9..])));
        Assert.All(streams, stream => Assert.Equal(4, stream.Playlist!.TotalDuration));
        FilterUtil.CleanAd(streams, ["QualityLevels"]);
        Assert.All(streams, stream => Assert.Empty(stream.Playlist!.MediaParts));
    }
}
