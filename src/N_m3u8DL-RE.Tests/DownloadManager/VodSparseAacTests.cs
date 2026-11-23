using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodSparseAacTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData(@"noise=drop=between(n\,40\,44)", 5)]
    [InlineData("setts=ts='TS+if(eq(N,40),1024,if(eq(N,41),512,0))'", 0)]
    [InlineData(@"noise=drop=between(n\,40\,44),setts=ts='TS+if(eq(N,74),1024,if(eq(N,75),512,0))'", 5)]
    public async Task TsDownloadPreservesAccessUnitsAndFillsOnlyMissingAudio(string? packetFilter, int missingPackets)
    {
        if (!HasTool("ffmpeg")) return;
        var root = Directory.CreateTempSubdirectory("vod-aac-gap-").FullName;
        try
        {
            var source = Path.Combine(root, "source.mp4");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=160x90:rate=25",
                "-f", "lavfi", "-i", "sine=frequency=997:sample_rate=48000", "-t", "3",
                "-c:v", "libx264", "-bf", "0", "-c:a", "aac", "-ac", "2", source);
            var ts = Path.Combine(root, "source.ts");
            string[] filter = packetFilter == null ? [] : ["-bsf:a", packetFilter];
            await Run("ffmpeg", ["-v", "error", "-i", source, "-c", "copy", .. filter, "-pes_payload_size", "1", ts]);
            var manifest = Path.Combine(root, "index.m3u8");
            await File.WriteAllTextAsync(manifest, $"#EXTM3U\n#EXT-X-TARGETDURATION:3\n#EXTINF:3,\n{new Uri(ts).AbsoluteUri}\n#EXT-X-ENDLIST\n");
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                DirPrefix = Path.Combine(root, "tmp"), MyOptions = CreateOptions(root)
            }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var output = Path.Combine(root, "out", "result.mp4");

            var expected = await AacHashes(ts);
            var actual = await AacHashes(output);
            if (missingPackets > 0)
            {
                Assert.Equal(expected.Length + missingPackets, actual.Length);
                Assert.Single(actual.Skip(40).Take(missingPackets).Distinct());
                actual = [.. actual.Take(40), .. actual.Skip(40 + missingPackets)];
            }
            Assert.Equal(expected, actual);
            Assert.Equal(await FrameHashes(ts), await FrameHashes(output));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<string[]> AacHashes(string file)
    {
        var hashes = await Run("ffmpeg", "-v", "error", "-i", file, "-map", "0:a", "-c:a", "copy",
            "-bsf:a", "aac_adtstoasc", "-f", "framehash", "-");
        return hashes.Split('\n').Where(line => line.StartsWith("0,"))
            .Select(line => line.Split(',')[5].Trim()).ToArray();
    }
}
