using System.Net;
using System.Text;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Parser.Processor;
using N_m3u8DL_RE.Parser.Util;
using N_m3u8DL_RE.Tests.TestSupport;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSVariableTests
{
    [Theory]
    [InlineData("# comment containing #EXT-X-STREAM-INF:BANDWIDTH=1000000")]
    [InlineData("#EXT-X-DATERANGE:ID=\"#EXT-X-STREAM-INF:literal\",START-DATE=\"2026-01-01T00:00:00Z\"")]
    [InlineData("#EXT-X-STREAM-INF-UNKNOWN:BANDWIDTH=1000000")]
    public async Task MasterDetectionOnlyMatchesActualTagLines(string extraLine)
    {
        var extractor = new HLSExtractor(new ParserConfig { Url = "https://example.com/media.m3u8" });
        var streams = await extractor.ExtractStreamsAsync(
            $"#EXTM3U\n{extraLine}\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\nsegment.ts\n#EXT-X-ENDLIST");
        var playlist = Assert.Single(streams).Playlist!;
        Assert.False(playlist.IsLive);
        Assert.Equal("https://example.com/segment.ts", Assert.Single(Assert.Single(playlist.MediaParts).MediaSegments).Url);
    }

    [Fact]
    public async Task CustomContentProcessorRunsBeforeVariableExpansionAndMasterDetection()
    {
        var config = new ParserConfig { Url = "https://example.com/master.m3u8" };
        config.ContentProcessors.Add(new DefineMasterProcessor());
        var extractor = new HLSExtractor(config);
        var streams = await extractor.ExtractStreamsAsync("#EXTM3U\n#CUSTOM-MASTER");
        Assert.Equal("https://example.com/media/video.m3u8", Assert.Single(streams).Url);
    }

    [Fact]
    public async Task MasterVariablesExpandRenditionsImportsKeysAndInit()
    {
        await using var server = new MediaFixtureServer("", (path, _) => path == "master.m3u8" ? """
            #EXTM3U
            #EXT-X-DEFINE:NAME="prefix",VALUE="/media/"
            #EXT-X-DEFINE:NAME="label",VALUE="English"
            #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="{$label}",URI="{$prefix}audio.m3u8"
            #EXT-X-STREAM-INF:BANDWIDTH=1000000,AUDIO="audio"
            {$prefix}video.m3u8
            """ : """
            #EXTM3U
            #EXT-X-DEFINE:IMPORT="prefix"
            #EXT-X-DEFINE:NAME="segment",VALUE="{$prefix}segment.m4s"
            #EXT-X-DEFINE:NAME="key",VALUE="EREREREREREREREREREREQ=="
            #EXT-X-DEFINE:NAME="iv",VALUE="00000000000000000000000000000001"
            #EXT-X-TARGETDURATION:4
            #EXT-X-MAP:URI="{$prefix}init.mp4"
            #EXT-X-KEY:METHOD=AES-128,URI="data:;base64,{$key}",IV=0x{$iv}
            #EXTINF:4,
            {$segment}
            #EXT-X-ENDLIST
            """);
        var config = new ParserConfig();
        config.UrlProcessors.Add(new ExpandedUrlProcessor());
        using var extractor = new StreamExtractor(config);
        await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
        var streams = await extractor.ExtractStreamsAsync();
        Assert.Equal(2, streams.Count);
        Assert.Equal("English", streams.Single(stream => stream.MediaType == MediaType.AUDIO).Name);
        await extractor.FetchPlayListAsync(streams);
        Assert.All(streams, stream =>
        {
            Assert.False(stream.Playlist!.IsLive);
            var part = Assert.Single(stream.Playlist.MediaParts);
            Assert.Equal(server.Url + "media/init.mp4", part.MediaInit!.Url);
            var segment = Assert.Single(part.MediaSegments);
            Assert.Equal(server.Url + "media/segment.m4s", segment.Url);
            Assert.Equal(Enumerable.Repeat((byte)0x11, 16), segment.EncryptInfo.Key!);
            Assert.Equal(15, segment.EncryptInfo.IV!.Count(value => value == 0));
            Assert.Equal(1, segment.EncryptInfo.IV![^1]);
        });
    }

    [Fact]
    public async Task QueryVariablesUseRedirectedPlaylistUrlAndPercentDecoding()
    {
        await using var server = new MediaFixtureServer("", (_, _) => """
            #EXTM3U
            #EXT-X-DEFINE:QUERYPARAM="folder"
            #EXT-X-DEFINE:QUERYPARAM="token"
            #EXTINF:4,
            {$folder}segment.ts?token={$token}
            #EXT-X-ENDLIST
            """, responseWriter: async (path, _, stream, token) =>
            {
                if (path != "redirect.m3u8")
                    return false;
                await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: /final.m3u8?folder=segments%2F&token=a%2Bb+\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
                return true;
            });
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(server.Url + "redirect.m3u8?token=old");
        var info = Assert.Single(await extractor.ExtractStreamsAsync());
        var segment = Assert.Single(Assert.Single(info.Playlist!.MediaParts).MediaSegments);
        Assert.Equal(server.Url + "segments/segment.ts?token=a+b+", segment.Url);
    }

    [Fact]
    public void SubstitutionIsSinglePassAndIgnoresComments()
    {
        var content = HLSVariableSubstitution.Process("""
            #EXTM3U
            #EXT-X-DEFINE:QUERYPARAM="literal"
            # a comment containing {$undefined}
            #EXTINF:4,TITLE="{$undefined}"
            segment.ts?value={$literal}
            """, "https://example.com/media.m3u8?literal=%7B%24undefined%7D", false,
            new Dictionary<string, string>(), out _);
        Assert.Contains("# a comment containing {$undefined}", content);
        Assert.Contains("#EXTINF:4,TITLE=\"{$undefined}\"", content);
        Assert.Contains("segment.ts?value={$undefined}", content);
        Assert.DoesNotContain("#EXT-X-DEFINE", content);
    }

    [Fact]
    public async Task ReextractingMasterUsesItsOwnUrlAfterFetchingChild()
    {
        await using var server = new MediaFixtureServer("", (path, _) => path == "master.m3u8" ? """
            #EXTM3U
            #EXT-X-DEFINE:QUERYPARAM="token"
            #EXT-X-STREAM-INF:BANDWIDTH=1000000
            child/media.m3u8
            """ : """
            #EXTM3U
            #EXT-X-DEFINE:IMPORT="token"
            #EXT-X-TARGETDURATION:4
            #EXTINF:4,
            segment.ts?token={$token}
            #EXT-X-ENDLIST
            """);
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8?token=master");
        var streams = await extractor.ExtractStreamsAsync();
        await extractor.FetchPlayListAsync(streams);
        streams = await extractor.ExtractStreamsAsync();
        await extractor.FetchPlayListAsync(streams);
        var segment = Assert.Single(Assert.Single(Assert.Single(streams).Playlist!.MediaParts).MediaSegments);
        Assert.Equal(server.Url + "child/segment.ts?token=master", segment.Url);
        Assert.Equal(1, server.RequestCount("master.m3u8"));
        Assert.Equal(2, server.RequestCount("child/media.m3u8"));
    }

    [Theory]
    [InlineData("", "{$missing}.ts")]
    [InlineData("", "{$parent}.ts")]
    [InlineData("#EXT-X-DEFINE:IMPORT=\"missing\"", "segment.ts")]
    [InlineData("#EXT-X-DEFINE:NAME=\"x\",VALUE=\"a\"\n#EXT-X-DEFINE:NAME=\"x\",VALUE=\"b\"", "segment.ts")]
    [InlineData("#EXT-X-DEFINE:NAME=\"x\"", "segment.ts")]
    [InlineData("#EXT-X-DEFINE:NAME=\"x\",VALUE=\"a\",IMPORT=\"parent\"", "segment.ts")]
    [InlineData("#EXT-X-DEFINE:QUERYPARAM=\"missing\"", "segment.ts")]
    [InlineData("#EXT-X-DEFINE:QUERYPARAM=\"unsafe\"", "segment.ts")]
    public void InvalidVariablesFailBeforeMakingRequests(string definitions, string uri)
    {
        const string url = "https://example.com/media.m3u8?unsafe=%22";
        var error = Assert.Throws<FormatException>(() => HLSVariableSubstitution.Process(
            $"#EXTM3U\n{definitions}\n#EXTINF:4,\n{uri}\n#EXT-X-ENDLIST", url, false,
            new Dictionary<string, string> { ["parent"] = "not-imported" }, out _));
        Assert.Contains(url, error.Message);
    }

    [Fact]
    public async Task RefreshDoesNotReusePreviousMediaDefinitions()
    {
        await using var server = new MediaFixtureServer("", (_, count) =>
            $"#EXTM3U\n{(count == 0 ? "#EXT-X-DEFINE:NAME=\"local\",VALUE=\"first.ts\"" : "")}\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\n{{$local}}\n");
        var url = server.Url + "media.m3u8";
        var extractor = new HLSExtractor(new ParserConfig { Url = url, OriginalUrl = url });
        var streams = await extractor.ExtractStreamsAsync("#EXTM3U\n#EXT-X-TARGETDURATION:4");
        await extractor.FetchPlayListAsync(streams);
        await Assert.ThrowsAsync<FormatException>(() => extractor.RefreshPlayListAsync(streams));
        Assert.Equal(2, server.RequestCount("media.m3u8"));
    }

    [Fact]
    public async Task FailedChildRefreshesMasterVariablesAndRetriesMatchingRendition()
    {
        await using var server = new MediaFixtureServer("", (path, count) => path == "master.m3u8"
            ? $"#EXTM3U\n#EXT-X-DEFINE:NAME=\"prefix\",VALUE=\"/{(count == 0 ? "old" : "new")}/\"\n#EXT-X-STREAM-INF:BANDWIDTH=1000000\n{{$prefix}}media.m3u8"
            : "#EXTM3U\n#EXT-X-DEFINE:IMPORT=\"prefix\"\n#EXTINF:4,\n{$prefix}segment.ts\n#EXT-X-ENDLIST",
            responseStatus: (path, _) => path == "old/media.m3u8" ? HttpStatusCode.Forbidden : null);
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
        var streams = await extractor.ExtractStreamsAsync();
        await extractor.FetchPlayListAsync(streams);
        var playlist = Assert.Single(streams).Playlist!;
        Assert.False(playlist.IsLive);
        Assert.Equal(server.Url + "new/segment.ts", Assert.Single(Assert.Single(playlist.MediaParts).MediaSegments).Url);
        Assert.Equal(2, server.RequestCount("master.m3u8"));
        Assert.Equal(1, server.RequestCount("old/media.m3u8"));
        Assert.Equal(1, server.RequestCount("new/media.m3u8"));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task PersistentChildFailureKeepsHttpError(HttpStatusCode status)
    {
        await using var server = new MediaFixtureServer("", (_, _) =>
            "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000000\nmedia.m3u8",
            responseStatus: (path, _) => path == "media.m3u8" ? status : null);
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
        var streams = await extractor.ExtractStreamsAsync();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => extractor.FetchPlayListAsync(streams));
        Assert.Equal(status, error.StatusCode);
        Assert.Null(Assert.Single(streams).Playlist);
        Assert.Equal(2, server.RequestCount("media.m3u8"));
    }

    [Fact]
    public async Task ExpiredRedirectedMasterReloadsOriginalEntryDuringLiveRefresh()
    {
        await using var server = new MediaFixtureServer("", (path, _) => path.EndsWith("master.m3u8")
            ? $"#EXTM3U\n#EXT-X-DEFINE:NAME=\"prefix\",VALUE=\"/{path.Split('/')[0]}/\"\n#EXT-X-STREAM-INF:BANDWIDTH=1000000\n{{$prefix}}media.m3u8"
            : "#EXTM3U\n#EXT-X-DEFINE:IMPORT=\"prefix\"\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\n{$prefix}segment.ts",
            responseStatus: (path, count) => count > 1 && path.StartsWith("old/") ? HttpStatusCode.Forbidden : null,
            responseWriter: async (path, count, stream, token) =>
            {
                if (path != "entry.m3u8")
                    return false;
                var folder = count == 1 ? "old" : "new";
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: /{folder}/master.m3u8\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
                return true;
            });
        using var extractor = new StreamExtractor(new ParserConfig());
        await extractor.LoadSourceFromUrlAsync(server.Url + "entry.m3u8");
        var streams = await extractor.ExtractStreamsAsync();
        await extractor.FetchPlayListAsync(streams);
        await extractor.RefreshPlayListAsync(streams);
        var playlist = Assert.Single(streams).Playlist!;
        Assert.True(playlist.IsLive);
        Assert.Equal(server.Url + "new/segment.ts", Assert.Single(Assert.Single(playlist.MediaParts).MediaSegments).Url);
        Assert.Equal(2, server.RequestCount("entry.m3u8"));
        Assert.Equal(2, server.RequestCount("old/master.m3u8"));
        Assert.Equal(1, server.RequestCount("new/master.m3u8"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MasterResponseIsRejectedWhileEmptyLivePlaylistRemainsValid(bool returnsMaster)
    {
        const string master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=1000000\nmedia.m3u8";
        await using var server = new MediaFixtureServer("", (_, _) => returnsMaster
            ? master : "#EXTM3U\n#EXT-X-TARGETDURATION:4");
        var url = server.Url + "master.m3u8";
        var extractor = new HLSExtractor(new ParserConfig { Url = url, OriginalUrl = url });
        var streams = await extractor.ExtractStreamsAsync(master);
        if (returnsMaster)
        {
            var error = await Assert.ThrowsAsync<FormatException>(() => extractor.FetchPlayListAsync(streams));
            Assert.Contains("media.m3u8", error.Message);
        }
        else
        {
            await extractor.FetchPlayListAsync(streams);
            var playlist = Assert.Single(streams).Playlist!;
            Assert.True(playlist.IsLive);
            Assert.Empty(playlist.MediaParts.SelectMany(part => part.MediaSegments));
        }
    }

    private sealed class ExpandedUrlProcessor : UrlProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig) => true;
        public override string Process(string oriUrl, ParserConfig parserConfig)
        {
            Assert.DoesNotContain("{$", oriUrl);
            return oriUrl;
        }
    }

    private sealed class DefineMasterProcessor : ContentProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig) =>
            extractorType == ExtractorType.HLS && rawText.Contains("#CUSTOM-MASTER");

        public override string Process(string rawText, ParserConfig parserConfig) => rawText.Replace("#CUSTOM-MASTER", """
            #EXT-X-DEFINE:NAME="prefix",VALUE="media/"
            #EXT-X-STREAM-INF:BANDWIDTH=1000000
            {$prefix}video.m3u8
            """);
    }
}
