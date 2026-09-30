using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class HLSLiveEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RefreshKeepsFinalSegmentsAndDetectsEndlist(bool hasMediaInit)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hls-live-end-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var playlistPath = Path.Combine(directory, "media.m3u8");
            await File.WriteAllTextAsync(playlistPath, CreatePlaylist(hasMediaInit, ended: false));

            var masterUrl = new Uri(Path.Combine(directory, "master.m3u8")).AbsoluteUri;
            var extractor = new HLSExtractor(new ParserConfig { Url = masterUrl, OriginalUrl = masterUrl });
            var streams = await extractor.ExtractStreamsAsync("""
                #EXTM3U
                #EXT-X-STREAM-INF:BANDWIDTH=1000000
                media.m3u8
                """);
            await extractor.FetchPlayListAsync(streams);

            var stream = Assert.Single(streams);
            var originalInit = stream.Playlist!.MediaParts[0].MediaInit;
            Assert.True(stream.Playlist.IsLive);
            Assert.Equal(hasMediaInit, originalInit != null);

            await File.WriteAllTextAsync(playlistPath, CreatePlaylist(hasMediaInit, ended: true));
            await extractor.RefreshPlayListAsync(streams);

            Assert.False(stream.Playlist!.IsLive);
            Assert.Equal(2, stream.Playlist.MediaParts.Sum(part => part.MediaSegments.Count));
            if (hasMediaInit)
            {
                Assert.Same(originalInit, stream.Playlist.MediaParts[0].MediaInit);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }


    private static string CreatePlaylist(bool hasMediaInit, bool ended)
    {
        var map = hasMediaInit ? "#EXT-X-MAP:URI=\"init.mp4\"\n" : string.Empty;
        var lastSegment = ended ? "#EXTINF:1,\nsecond.m4s\n#EXT-X-ENDLIST\n" : string.Empty;
        return $"#EXTM3U\n#EXT-X-TARGETDURATION:1\n{map}#EXTINF:1,\nfirst.m4s\n{lastSegment}";
    }
}
