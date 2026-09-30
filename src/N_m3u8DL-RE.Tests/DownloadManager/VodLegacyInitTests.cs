using N_m3u8DL_RE.Common.Enum;
using System.Buffers.Binary;
using System.Security.Cryptography;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodLegacyInitTests
{
    [Theory]
    [InlineData("hls-ts", "all", false)]
    [InlineData("hls-ts", "range", false)]
    [InlineData("hls-ts", "ad", false)]
    [InlineData("hls-ts", "discontinuity", false)]
    [InlineData("hls-ts", "binary", false)]
    [InlineData("hls-ts", "all", true)]
    [InlineData("hls-ts", "ad", true)]
    [InlineData("hls-mp4", "all", false)]
    [InlineData("hls-mp4", "range", false)]
    [InlineData("hls-mp4", "ad", false)]
    [InlineData("hls-mp4", "discontinuity", false)]
    [InlineData("hls-mp4", "all", true)]
    [InlineData("hls-mp4", "ad", true)]
    [InlineData("dash-mp4", "all", false)]
    [InlineData("dash-mp4", "range", false)]
    [InlineData("dash-mp4", "ad", false)]
    [InlineData("dash-full", "all", false)]
    [InlineData("hls-ts", "skip", false)]
    [InlineData("hls-mp4", "skip", false)]
    [InlineData("dash-mp4", "skip", false)]
    public async Task NoInitAndSingleInitKeepOriginalMedia(string format, string mode, bool encrypted)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-legacy-init-").FullName;
        try
        {
            var ts = format == "hls-ts";
            var full = format == "dash-full";
            var source = Path.Combine(root, "source.m3u8");
            if (full)
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                    "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", Path.Combine(root, "full.mp4"));
            else
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                    "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls", "-hls_time", "2",
                    "-hls_segment_type", ts ? "mpegts" : "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                    "-hls_segment_filename", Path.Combine(root, ts ? "media-%d.ts" : "media-%d.m4s"), source);
            var manifest = Path.Combine(root, format.StartsWith("dash") ? "vod.mpd" : "vod.m3u8");
            if (format.StartsWith("dash"))
                await File.WriteAllTextAsync(manifest, $"""
                    <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6S">
                      <Period duration="PT6S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                        {(full ? "<BaseURL>full.mp4</BaseURL>" : """
                        <SegmentList duration="2"><Initialization sourceURL="init.mp4"/>
                          <SegmentURL media="media-0.m4s"/><SegmentURL media="media-1.m4s"/><SegmentURL media="media-2.m4s"/>
                        </SegmentList>
                        """)}
                      </Representation></AdaptationSet></Period>
                    </MPD>
                    """);
            else
            {
                var content = await File.ReadAllTextAsync(source);
                if (mode == "discontinuity")
                    content = content.Replace("#EXTINF:2.000000,\nmedia-2", "#EXT-X-DISCONTINUITY\n#EXTINF:2.000000,\nmedia-2");
                if (encrypted)
                {
                    // 同一 init/密钥贯穿全片，验证原有 AES 解密与合并路径。
                    var key = Enumerable.Repeat((byte)0x42, 16).ToArray();
                    await File.WriteAllBytesAsync(Path.Combine(root, "key.bin"), key);
                    using var aes = Aes.Create();
                    aes.Key = key;
                    var files = Directory.GetFiles(root, ts ? "*.ts" : "*.m4s").ToList();
                    if (!ts)
                        files.Add(Path.Combine(root, "init.mp4"));
                    foreach (var file in files)
                        await File.WriteAllBytesAsync(file, aes.EncryptCbc(await File.ReadAllBytesAsync(file), new byte[16], PaddingMode.PKCS7));
                    content = content.Replace("#EXTM3U", "#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x00000000000000000000000000000000");
                }
                await File.WriteAllTextAsync(manifest, content);
            }
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            Assert.Equal(ts || full ? 0 : 1, streams[0].Playlist!.MediaParts.Select(p => p.MediaInit)
                .OfType<MediaSegment>().Distinct(ReferenceEqualityComparer.Instance).Count());
            if (mode == "discontinuity")
                Assert.Equal(2, streams[0].Playlist!.MediaParts.Count);
            if (mode == "range")
                FilterUtil.ApplyCustomRange(streams, new CustomRange
                { StartSegIndex = 1, EndSegIndex = 2, InputStr = "1-2" });
            FilterUtil.CleanAd(streams, mode == "ad" ? ["media-1"] : null);
            if (streams[0].Playlist!.MediaParts.Count > 1)
            {
                if (format.StartsWith("dash"))
                    VodStreamPlanner.AlignPeriods(streams);
                else
                    VodStreamPlanner.AlignHlsDiscontinuities(streams);
            }
            var options = CreateOptions(root);
            options.BinaryMerge = mode == "binary";
            options.SkipMerge = mode == "skip";
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            if (options.SkipMerge)
            {
                Assert.Empty(Directory.GetFiles(Path.Combine(root, "out")));
                var files = Directory.GetFiles(Path.Combine(root, "tmp"), "*", SearchOption.AllDirectories);
                Assert.Equal(ts ? 3 : 4, files.Length);
                for (var i = 0; i < 3; i++)
                    Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, $"media-{i}.{(ts ? "ts" : "m4s")}")),
                        await File.ReadAllBytesAsync(Assert.Single(files, f => Path.GetFileNameWithoutExtension(f) == i.ToString())));
                if (!ts)
                    Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, "init.mp4")),
                    await File.ReadAllBytesAsync(Assert.Single(files, f => Path.GetFileName(f) == "_init.mp4")));
                return;
            }
            var output = Assert.Single(Directory.GetFiles(Path.Combine(root, "out")));
            await AssertVideo(output, mode is "range" or "ad" ? 4 : 6, mode is "range" or "ad" ? 100 : 150);
            Assert.Empty(Directory.Exists(Path.Combine(root, "tmp"))
                ? Directory.GetFiles(Path.Combine(root, "tmp"), "*", SearchOption.AllDirectories) : []);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public async Task SingleInitCencStillDecryptsAllFragments(int engineValue, bool realtime)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || !OnPath("mp4encrypt") || !OnPath("mp4decrypt"))
            return;
        var engine = (DecryptEngine)engineValue;
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        if (engine == DecryptEngine.SHAKA_PACKAGER && !File.Exists(packager) && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("vod-single-cenc-").FullName;
        try
        {
            var full = Path.Combine(root, "full.mp4");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-video_track_timescale", "1000",
                "-movflags", "+frag_keyframe+empty_moov+default_base_moof", full);
            var enc = Path.Combine(root, "encrypted.mp4");
            var kid = new string('1', 32); var key = new string('a', 32);
            await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random", "--property", $"1:KID:{kid}", full, enc);
            var bytes = await File.ReadAllBytesAsync(enc);
            var offsets = new List<int>();
            for (var offset = 0; offset < bytes.Length;)
            {
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                    offsets.Add(offset);
                offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            }
            Assert.Equal(3, offsets.Count);
            await File.WriteAllBytesAsync(Path.Combine(root, "init.mp4"), bytes[..offsets[0]]);
            for (var i = 0; i < offsets.Count; i++)
                await File.WriteAllBytesAsync(Path.Combine(root, $"media-{i}.m4s"), bytes[offsets[i]..(i + 1 < offsets.Count ? offsets[i + 1] : bytes.Length)]);
            var manifest = Path.Combine(root, "vod.mpd");
            await File.WriteAllTextAsync(manifest, $"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="static" mediaPresentationDuration="PT6S">
                  <Period duration="PT6S"><AdaptationSet mimeType="video/mp4"><ContentProtection cenc:default_KID="{kid}"/>
                    <Representation id="v" codecs="avc1.64001e"><SegmentList duration="2"><Initialization sourceURL="init.mp4"/>
                      <SegmentURL media="media-0.m4s"/><SegmentURL media="media-1.m4s"/><SegmentURL media="media-2.m4s"/>
                    </SegmentList></Representation>
                  </AdaptationSet></Period>
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            FilterUtil.CleanAd(streams, null);
            Assert.Single(streams[0].Playlist!.MediaParts);
            var options = CreateOptions(root);
            options.DecryptionEngine = engine;
            options.DecryptionBinaryPath = engine switch { DecryptEngine.FFMPEG => "ffmpeg", DecryptEngine.SHAKA_PACKAGER => packager, _ => "mp4decrypt" };
            options.Keys = [$"{kid}:{key}"]; options.MP4RealTimeDecryption = realtime;
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 150);
        }
        finally { Directory.Delete(root, true); }
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoInitHlsTextSubtitlesKeepContinuousCueTimes(bool range)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-legacy-vtt-").FullName;
        try
        {
            for (var i = 0; i < 3; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"),
                    $"WEBVTT\n\n00:00:0{i * 2}.250 --> 00:00:0{i * 2 + 1}.000\ncue-{i}\n\n");
            var manifest = Path.Combine(root, "vod.m3u8");
            await File.WriteAllTextAsync(manifest, """
                #EXTM3U
                #EXTINF:2,
                sub-0.vtt
                #EXTINF:2,
                sub-1.vtt
                #EXT-X-DISCONTINUITY
                #EXTINF:2,
                sub-2.vtt
                #EXT-X-ENDLIST
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            // 对应 master 中的字幕 rendition；独立媒体清单本身不声明类型。
            streams[0].MediaType = MediaType.SUBTITLES;
            streams[0].Extension = "vtt";
            Assert.All(streams[0].Playlist!.MediaParts, p => Assert.Null(p.MediaInit));
            if (range)
                FilterUtil.ApplyCustomRange(streams, new CustomRange { StartSegIndex = 1, EndSegIndex = 2, InputStr = "1-2" });
            FilterUtil.CleanAd(streams, null);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var options = CreateOptions(root);
            options.SubtitleFormat = SubtitleFormat.VTT;
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.vtt")));
            Assert.Equal(range ? 2 : 3, sub.Cues.Count);
            for (var i = 0; i < sub.Cues.Count; i++)
            {
                Assert.Equal(0.25 + i * 2, sub.Cues[i].StartTime.TotalSeconds);
                Assert.Equal(1 + i * 2, sub.Cues[i].EndTime.TotalSeconds);
                Assert.Equal($"cue-{i + (range ? 1 : 0)}", sub.Cues[i].Payload);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
