using System.Globalization;
using System.Text.Json;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodOutputCollisionTests
{
    [Fact]
    public async Task ConcurrentTsTracksReserveTheirActualOutputExtensions()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-output-extensions-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "1", "-c:v", "libx264", "-g", "25", "-bf", "0", "-f", "hls", "-hls_time", "10",
                "-hls_segment_filename", Path.Combine(root, "video-%d.ts"), Path.Combine(root, "video.m3u8"));
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=48000",
                "-t", "1", "-c:a", "aac", "-f", "hls", "-hls_time", "10",
                "-hls_segment_filename", Path.Combine(root, "audio-%d.ts"), Path.Combine(root, "audio.m3u8"));
            var manifest = Path.Combine(root, "master.m3u8");
            await File.WriteAllTextAsync(manifest, """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="a",NAME="Audio",URI="audio.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e,mp4a.40.2",AUDIO="a"
                video.m3u8
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            await extractor.FetchPlayListAsync(streams);
            var options = CreateOptions(root);
            options.ConcurrentDownload = true;
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            Assert.Equal(["result.m4a", "result.mp4"], Directory.GetFiles(options.SaveDir!)
                .Select(path => Path.GetFileName(path)!).Order().ToArray());
            foreach (var output in Directory.GetFiles(options.SaveDir!))
                await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DecryptionDoesNotOverwriteAnExistingDecOutput()
    {
        if (!HasTool("ffmpeg") || !OnPath("mp4encrypt") || !OnPath("mp4decrypt"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-output-decryption-").FullName;
        try
        {
            var plain = Path.Combine(root, "plain.mp4");
            var encrypted = Path.Combine(root, "encrypted.mp4");
            var kid = new string('1', 32);
            var key = new string('a', 32);
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=48000",
                "-t", "1", "-c:a", "aac", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", plain);
            await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random",
                "--property", $"1:KID:{kid}", plain, encrypted);
            var manifest = Path.Combine(root, "audio.mpd");
            await File.WriteAllTextAsync(manifest, $"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="static" mediaPresentationDuration="PT1S">
                  <Period duration="PT1S"><AdaptationSet mimeType="audio/mp4">
                    <ContentProtection cenc:default_KID="{kid}"/>
                    <Representation id="audio" codecs="mp4a.40.2" bandwidth="128000">
                      <SegmentList duration="1"><SegmentURL media="encrypted.mp4"/></SegmentList>
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            var options = CreateOptions(root);
            options.DecryptionEngine = DecryptEngine.MP4DECRYPT;
            options.DecryptionBinaryPath = "mp4decrypt";
            options.Keys = [$"{kid}:{key}"];
            options.SaveDir = Path.Combine(root, "中文输出目录");
            options.SaveName = "测试音轨";
            Directory.CreateDirectory(options.SaveDir!);
            var existing = Path.Combine(options.SaveDir!, "测试音轨_dec.m4a");
            await File.WriteAllTextAsync(existing, "another track");
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            Assert.Equal("another track", await File.ReadAllTextAsync(existing));
            var output = Path.Combine(options.SaveDir!, "测试音轨.m4a");
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(output));
            Assert.Equal(await Run("ffmpeg", "-v", "error", "-i", plain, "-f", "hash", "-hash", "sha256", "-"),
                await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "hash", "-hash", "sha256", "-"));
            var temporary = Path.Combine(root, "tmp");
            Assert.Empty(Directory.Exists(temporary)
                ? Directory.GetFiles(temporary, "*", SearchOption.AllDirectories) : []);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true, false, "eac3")]
    [InlineData(false, false, "alac")]
    [InlineData(false, true, "alac")]
    public async Task ConcurrentAudioTracksKeepBothCodecs(bool binaryMerge, bool multiPeriod, string secondCodec)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-output-collision-").FullName;
        try
        {
            // 二进制合并覆盖日志中的 AAC/E-AC-3；FFmpeg 的 M4A 容器使用 AAC/ALAC。
            string[] codecs = ["aac", secondCodec];
            foreach (var codec in codecs)
            {
                string[] outputArgs = binaryMerge
                    ? ["-f", "hls", "-hls_time", "10", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", $"{codec}-init.mp4",
                        "-hls_segment_filename", Path.Combine(root, $"{codec}-%d.m4s"), Path.Combine(root, $"{codec}.m3u8")]
                    : [Path.Combine(root, $"{codec}.mp4")];
                await Run("ffmpeg", ["-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                    "-t", "2", "-c:a", codec, "-b:a", "128k", ..outputArgs]);
            }
            var adaptations = string.Join('\n', codecs.Select((codec, i) => $"""
                <AdaptationSet id="{i}" mimeType="audio/mp4" lang="en-US">
                  <Representation id="{codec}" codecs="{(codec == "aac" ? "mp4a.40.2" : codec == "eac3" ? "ec-3" : "alac")}" bandwidth="128000">
                    <SegmentList duration="2">
                      {(binaryMerge ? $"<Initialization sourceURL=\"{codec}-init.mp4\"/><SegmentURL media=\"{codec}-0.m4s\"/>" : $"<SegmentURL media=\"{codec}.mp4\"/>")}
                    </SegmentList>
                  </Representation>
                </AdaptationSet>
                """));
            var periods = string.Join('\n', Enumerable.Range(0, multiPeriod ? 2 : 1)
                .Select(i => $"<Period id=\"{i}\" duration=\"PT2S\">{adaptations}</Period>"));
            var manifest = Path.Combine(root, "audio.mpd");
            await File.WriteAllTextAsync(manifest, $"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT{(multiPeriod ? 4 : 2)}S">
                  {periods}
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var sources = await extractor.ExtractStreamsAsync();
            var streams = VodStreamPlanner.Build(sources, sources);
            VodStreamPlanner.AlignPeriods(streams);
            Assert.Equal(2, streams.Count);
            var options = CreateOptions(root);
            options.ConcurrentDownload = true;
            options.BinaryMerge = binaryMerge;
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var outputs = Directory.GetFiles(options.SaveDir!, "*.m4a");
            Assert.Equal(2, outputs.Length);
            Assert.Contains(Path.Combine(options.SaveDir!, "result.en-US.m4a"), outputs);
            var outputCodecs = new List<string>();
            foreach (var output in outputs)
            {
                using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries",
                    "format=duration:stream=codec_name", "-of", "json", output));
                outputCodecs.Add(Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray())
                    .GetProperty("codec_name").GetString()!);
                var duration = double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
                    CultureInfo.InvariantCulture);
                Assert.InRange(duration, multiPeriod ? 3.9 : 1.9, multiPeriod ? 4.1 : 2.1);
                await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
            }
            Assert.Equal(codecs, outputCodecs.Order().ToArray());
        }
        finally { Directory.Delete(root, true); }
    }
}
