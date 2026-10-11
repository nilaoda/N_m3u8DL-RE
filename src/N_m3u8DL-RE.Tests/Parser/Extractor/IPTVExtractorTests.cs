using System.Text;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using Spectre.Console;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class IPTVExtractorTests
{
    [Fact]
    public async Task ParsesNamesGroupsAndQuotedAttributesWithoutConnectingChannels()
    {
        var extractor = new IPTVExtractor(new ParserConfig
        {
            Url = "https://example.test/channels/list.m3u", OriginalUrl = "https://example.test/channels/list.m3u"
        });
        var streams = await extractor.ExtractStreamsAsync("""
            #EXTM3U x-tvg-url="https://example.test/epg.xml"
            #EXTINF:-1 tvg-id="channel-1" tvg-name="别名,备用" group-title="Group A" tvg-language="chi",频道 A[高清]
            ../live/1
            # #EXTINF:-1,停用频道
            # https://example.test/disabled
            #EXTINF:-1 tvg-name='频道 B',
            #EXTGRP:Group B
            https://example.test/live/2
            """);
        Assert.Equal(2, streams.Count);
        Assert.Equal("频道 A[高清]", streams[0].Name);
        Assert.Equal("channel-1", streams[0].GroupId);
        Assert.Equal("Group A", streams[0].GroupTitle);
        Assert.Equal("chi", streams[0].Language);
        Assert.Equal("https://example.test/live/1", streams[0].Url);
        Assert.Equal("频道 B", streams[1].Name);
        Assert.Equal("Group B", streams[1].GroupTitle);
        Assert.All(streams, stream => Assert.Null(stream.Playlist));
        foreach (var stream in streams)
        {
            Assert.Contains(stream.Name!, stream.ToString().RemoveMarkup());
            Assert.Contains(stream.Name!, stream.ToShortString().RemoveMarkup());
            Assert.Contains(stream.Name!, stream.ToShortShortString().RemoveMarkup());
            Assert.DoesNotContain("Kbps", stream.ToString());
        }
    }

    [Theory]
    [InlineData("#EXTINF:-1,频道\nhttp://example.test/live", true)]
    [InlineData("#EXTINF:0 tvg-name=Channel,频道\nhttp://example.test/live", true)]
    [InlineData("#EXT-X-TARGETDURATION:2\n#EXTINF:-1,\nsegment.ts", false)]
    [InlineData("#EXT-X-STREAM-INF:BANDWIDTH=4000000\nhttp://example.test/live", false)]
    public async Task ClassifiesLocalUtf8ListsWithoutChangingHlsDetection(string entries, bool iptv)
    {
        var path = Path.Combine(Path.GetTempPath(), $"re-iptv-{Guid.NewGuid():N}.m3u8");
        try
        {
            await File.WriteAllTextAsync(path, "#EXTM3U\n" + entries, new UTF8Encoding(true));
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(path);
            Assert.Equal(iptv ? ExtractorType.IPTV_M3U : ExtractorType.HLS, extractor.ExtractorType);
            Assert.Single(await extractor.ExtractStreamsAsync());
            Assert.Null(extractor.DirectSource);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("#EXTINF:-1,Missing URL")]
    [InlineData("#EXTINF:-1 tvg-name=Channel\nhttp://example.test/live")]
    public async Task IncompleteEntriesFailClearly(string entry)
    {
        var extractor = new IPTVExtractor(new ParserConfig { Url = "https://example.test/list.m3u" });
        await Assert.ThrowsAsync<FormatException>(() => extractor.ExtractStreamsAsync("#EXTM3U\n" + entry));
    }
}
