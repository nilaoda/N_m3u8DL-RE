using System.Web;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class LocalManifestUrlParamsTests
{
    [Theory]
    [InlineData("mpd", false, true)]
    [InlineData("mpd", true, true)]
    [InlineData("m3u8", false, true)]
    [InlineData("m3u8", true, true)]
    [InlineData("ism", false, true)]
    [InlineData("ism", true, true)]
    [InlineData("mpd", false, false)]
    [InlineData("m3u8", false, false)]
    [InlineData("ism", false, false)]
    public async Task LocalManifestUsesBaseUrlQueryWhenEnabled(string format, bool useFileUri, bool append)
    {
        var content = format switch
        {
            "mpd" => """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT4S">
                  <Period><AdaptationSet mimeType="video/mp4" contentType="video">
                    <Representation id="video" bandwidth="500000">
                      <SegmentTemplate timescale="1" duration="2"
                        initialization="init.mp4?own=1&amp;token=segment"
                        media="seg-$Number$.m4s?own=1&amp;token=segment" />
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """,
            "m3u8" => """
                #EXTM3U
                #EXT-X-TARGETDURATION:2
                #EXT-X-MAP:URI="init.mp4?own=1&token=segment"
                #EXTINF:2,
                segment.m4s?own=1&token=segment
                #EXT-X-ENDLIST
                """,
            "ism" => """
                <SmoothStreamingMedia MajorVersion="2" MinorVersion="1" Duration="40000000" TimeScale="10000000">
                  <StreamIndex Type="audio" Name="audio"
                    Url="QualityLevels({bitrate})/Fragments(audio={start time})?own=1&amp;token=segment">
                    <QualityLevel Index="0" Bitrate="128000" FourCC="AACL" SamplingRate="48000" Channels="2" />
                    <c t="0" d="40000000" />
                  </StreamIndex>
                </SmoothStreamingMedia>
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };
        var path = Path.Combine(Path.GetTempPath(), $"本地 清单-{Guid.NewGuid():N}.{format}");
        try
        {
            await File.WriteAllTextAsync(path, content);
            var config = new ParserConfig
            {
                BaseUrl = $"https://example.com/media/manifest.{format}?token=base&empty=",
                AppendUrlParams = append
            };
            using var extractor = new StreamExtractor(config);
            await extractor.LoadSourceFromUrlAsync(useFileUri ? new Uri(path).AbsoluteUri : path);
            var streams = await extractor.ExtractStreamsAsync();
            if (extractor.ExtractorType is ExtractorType.MPEG_DASH or ExtractorType.MSS)
                await extractor.FetchPlayListAsync(streams);
            var part = Assert.Single(Assert.Single(streams).Playlist!.MediaParts);
            var urls = part.MediaSegments.Select(segment => segment.Url).ToList();
            if (format != "ism")
                urls.Add(Assert.IsType<MediaSegment>(part.MediaInit).Url);
            // ISM 的 init 在本地合成；检查真正发往服务器的分片，而非 base64 数据。
            Assert.NotEmpty(urls);
            Assert.All(urls, url =>
            {
                Assert.StartsWith("https://example.com/media/", url);
                var query = HttpUtility.ParseQueryString(new Uri(url).Query);
                Assert.Equal(append ? "base" : "segment", query["token"]);
                Assert.Equal("1", query["own"]);
                Assert.Equal(append ? "" : null, query["empty"]);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
