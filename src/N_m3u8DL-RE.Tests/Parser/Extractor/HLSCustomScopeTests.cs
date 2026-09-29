using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSCustomScopeTests
{
    private static readonly byte[] CustomKey = Enumerable.Repeat((byte)0x11, 16).ToArray();
    private static readonly byte[] CustomIV = Enumerable.Repeat((byte)0x22, 16).ToArray();
    private static readonly byte[] PlaylistKey = Enumerable.Repeat((byte)0x33, 16).ToArray();

    [Theory]
    [InlineData(CustomHlsScope.ALL, EncryptMethod.AES_128, EncryptMethod.AES_128, EncryptMethod.AES_128)]
    [InlineData(CustomHlsScope.VIDEO, EncryptMethod.AES_128, EncryptMethod.NONE, EncryptMethod.NONE)]
    [InlineData(CustomHlsScope.AUDIO, EncryptMethod.NONE, EncryptMethod.AES_128, EncryptMethod.NONE)]
    public async Task ScopeAppliesToSelectedMasterRendition(
        CustomHlsScope scope, EncryptMethod videoMethod, EncryptMethod audioMethod, EncryptMethod subtitleMethod)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hls-custom-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "video.m3u8"), Playlist("video.ts"));
            await File.WriteAllTextAsync(Path.Combine(directory, "audio.m3u8"), Playlist("audio.ts"));
            await File.WriteAllTextAsync(Path.Combine(directory, "subtitle.m3u8"), Playlist("subtitle.vtt"));

            var masterUrl = new Uri(Path.Combine(directory, "master.m3u8")).AbsoluteUri;
            var config = new ParserConfig
            {
                Url = masterUrl,
                OriginalUrl = masterUrl,
                CustomHLSScope = scope,
                CustomMethod = EncryptMethod.AES_128,
                CustomeKey = CustomKey,
                CustomeIV = CustomIV
            };
            var extractor = new HLSExtractor(config);
            var streams = await extractor.ExtractStreamsAsync("""
                #EXTM3U
                #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="Audio",URI="audio.m3u8"
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="subs",NAME="Subs",URI="subtitle.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=1000000,AUDIO="audio",SUBTITLES="subs"
                video.m3u8
                """);

            await extractor.FetchPlayListAsync(streams);
            AssertSegment(streams.Single(s => s.MediaType is null), videoMethod);
            AssertSegment(streams.Single(s => s.MediaType == MediaType.AUDIO), audioMethod);
            AssertSegment(streams.Single(s => s.MediaType == MediaType.SUBTITLES), subtitleMethod);

            // 直播刷新再次解析相同的流时，范围不能受前一条流的状态影响。
            await extractor.RefreshPlayListAsync(streams);
            AssertSegment(streams.Single(s => s.MediaType == MediaType.AUDIO), audioMethod);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task VideoScopeKeepsAudioKeyTagsAndMethodNone()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hls-custom-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var keyUri = "data:;base64," + Convert.ToBase64String(PlaylistKey);
            await File.WriteAllTextAsync(Path.Combine(directory, "video.m3u8"), Playlist("video.ts"));
            await File.WriteAllTextAsync(Path.Combine(directory, "audio.m3u8"), $"""
                #EXTM3U
                #EXT-X-TARGETDURATION:4
                #EXT-X-KEY:METHOD=NONE
                #EXTINF:4,
                clear.ts
                #EXT-X-KEY:METHOD=AES-128,URI="{keyUri}"
                #EXTINF:4,
                encrypted.ts
                #EXT-X-ENDLIST
                """);

            var masterUrl = new Uri(Path.Combine(directory, "master.m3u8")).AbsoluteUri;
            var config = new ParserConfig
            {
                Url = masterUrl,
                OriginalUrl = masterUrl,
                CustomHLSScope = CustomHlsScope.VIDEO,
                CustomMethod = EncryptMethod.AES_128_ECB,
                CustomeKey = CustomKey,
                CustomeIV = CustomIV
            };
            var extractor = new HLSExtractor(config);
            var streams = await extractor.ExtractStreamsAsync("""
                #EXTM3U
                #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="audio",NAME="Audio",URI="audio.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=1000000,AUDIO="audio"
                video.m3u8
                """);
            await extractor.FetchPlayListAsync(streams);

            var video = Segments(streams.Single(s => s.MediaType is null));
            video[0].EncryptInfo.Method.ShouldBe(EncryptMethod.AES_128_ECB);
            video[0].EncryptInfo.Key.ShouldBe(CustomKey);

            var audio = Segments(streams.Single(s => s.MediaType == MediaType.AUDIO));
            audio[0].EncryptInfo.Method.ShouldBe(EncryptMethod.NONE);
            audio[1].EncryptInfo.Method.ShouldBe(EncryptMethod.AES_128);
            audio[1].EncryptInfo.Key.ShouldBe(PlaylistKey);
            audio[1].EncryptInfo.IV.ShouldNotBe(CustomIV);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(CustomHlsScope.VIDEO, EncryptMethod.NONE, EncryptMethod.AES_128, EncryptMethod.AES_128)]
    [InlineData(CustomHlsScope.AUDIO, EncryptMethod.AES_128, EncryptMethod.NONE, EncryptMethod.NONE)]
    public async Task AudioOnlyVariantUsesAudioScope(
        CustomHlsScope scope, EncryptMethod audioMethod, EncryptMethod videoMethod, EncryptMethod unknownMethod)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hls-custom-scope-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var name in new[] { "audio-aac", "audio-ec3", "video", "unknown" })
                await File.WriteAllTextAsync(Path.Combine(directory, name + ".m3u8"), Playlist(name + ".ts"));

            var masterUrl = new Uri(Path.Combine(directory, "master.m3u8")).AbsoluteUri;
            var config = new ParserConfig
            {
                Url = masterUrl,
                OriginalUrl = masterUrl,
                CustomHLSScope = scope,
                CustomMethod = EncryptMethod.AES_128,
                CustomeKey = CustomKey,
                CustomeIV = CustomIV
            };
            var extractor = new HLSExtractor(config);
            var streams = await extractor.ExtractStreamsAsync("""
                #EXTM3U
                #EXT-X-STREAM-INF:BANDWIDTH=128000,CODECS="mp4a.40.2"
                audio-aac.m3u8
                #EXT-X-STREAM-INF:BANDWIDTH=256000,CODECS="ec-3"
                audio-ec3.m3u8
                #EXT-X-STREAM-INF:BANDWIDTH=1000000,CODECS="mp4a.40.2,avc1.640028"
                video.m3u8
                #EXT-X-STREAM-INF:BANDWIDTH=800000
                unknown.m3u8
                """);
            await extractor.FetchPlayListAsync(streams);

            StreamSpec Find(string name) => streams.Single(s => Path.GetFileName(new Uri(s.Url).LocalPath) == name + ".m3u8");
            AssertSegment(Find("audio-aac"), audioMethod);
            AssertSegment(Find("audio-ec3"), audioMethod);
            AssertSegment(Find("video"), videoMethod);
            AssertSegment(Find("unknown"), unknownMethod);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task ScopeAppliesToStandaloneMediaPlaylist()
    {
        var config = new ParserConfig
        {
            Url = "https://example.com/audio.m3u8",
            CustomHLSScope = CustomHlsScope.AUDIO,
            CustomMethod = EncryptMethod.AES_128,
            CustomeKey = CustomKey,
            CustomeIV = CustomIV
        };
        var extractor = new HLSExtractor(config);
        var streams = await extractor.ExtractStreamsAsync(Playlist("audio.ts"));

        AssertSegment(streams.Single(), EncryptMethod.AES_128);
    }

    private static string Playlist(string segmentName) => $"""
        #EXTM3U
        #EXT-X-TARGETDURATION:4
        #EXTINF:4,
        {segmentName}
        #EXT-X-ENDLIST
        """;

    private static List<MediaSegment> Segments(StreamSpec stream) =>
        stream.Playlist!.MediaParts.SelectMany(p => p.MediaSegments).ToList();

    private static void AssertSegment(StreamSpec stream, EncryptMethod method)
    {
        var segment = Segments(stream).Single();
        segment.EncryptInfo.Method.ShouldBe(method);
        if (method == EncryptMethod.AES_128)
        {
            segment.EncryptInfo.Key.ShouldBe(CustomKey);
            segment.EncryptInfo.IV.ShouldBe(CustomIV);
        }
    }
}
