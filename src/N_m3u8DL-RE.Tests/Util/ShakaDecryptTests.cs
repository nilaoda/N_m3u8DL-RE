using System.Buffers.Binary;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

[Collection("Download console")]
public class ShakaDecryptTests
{
    private static readonly string Kid = new('1', 32);
    private static readonly string Key = new('a', 32);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommaPathsDecryptAndRestoreInputs(bool withInit)
    {
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        if (!HasTool("ffmpeg") || !OnPath("mp4encrypt") || !File.Exists(packager) && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("shaka-decrypt-").FullName;
        try
        {
            await CreateEncryptedMedia(root, withInit);
            var inputDir = Directory.CreateDirectory(Path.Combine(root, "input, 中文")).FullName;
            foreach (var file in Directory.GetFiles(root))
                File.Move(file, Path.Combine(inputDir, Path.GetFileName(file)));
            var source = Path.Combine(inputDir, "source, 中文.mp4");
            var original = await File.ReadAllBytesAsync(source);
            if (!withInit)
            {
                Assert.Equal(Kid, MP4DecryptUtil.ReadInitShaka(source, packager));
                Assert.Equal(original, await File.ReadAllBytesAsync(source));
                Assert.Equal(2, Directory.GetFiles(inputDir).Length);
            }
            var outputDir = Directory.CreateDirectory(Path.Combine(root, "output, 中文")).FullName;
            var dest = Path.Combine(outputDir, "Test, Name.mp4");
            await File.WriteAllTextAsync(dest, "existing output");
            var init = withInit ? Path.Combine(inputDir, "init, 中文.mp4") : "";
            var originalInit = withInit ? await File.ReadAllBytesAsync(init) : [];
            Assert.True(await MP4DecryptUtil.DecryptAsync(DecryptEngine.SHAKA_PACKAGER,
                packager, [$"{Kid}:{Key}"], source, dest, Kid, init));
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            if (withInit)
                Assert.Equal(originalInit, await File.ReadAllBytesAsync(init));
            Assert.Equal(withInit ? 3 : 2, Directory.GetFiles(inputDir).Length);
            Assert.Single(Directory.GetFiles(outputDir));
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(dest));
            Assert.Equal(await DecodedHash(Path.Combine(inputDir, "plain.mp4")), await DecodedHash(dest));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RealtimeDecryptFailurePreservesInputsAndDoesNotMux(bool withInit, bool live)
    {
        if (!HasTool("ffmpeg") || !OnPath("mp4encrypt"))
            return;
        var root = Directory.CreateTempSubdirectory("shaka-realtime-failure-").FullName;
        try
        {
            await CreateEncryptedMedia(root, withInit);
            var source = Path.Combine(root, "source, 中文.mp4");
            var original = await File.ReadAllBytesAsync(source);
            if (withInit)
                File.Copy(source, Path.Combine(root, "second.mp4"));
            var manifest = Path.Combine(root, "source.mpd");
            await File.WriteAllTextAsync(manifest, $"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="static" mediaPresentationDuration="PT{(withInit ? 2 : 1)}S">
                  <Period duration="PT{(withInit ? 2 : 1)}S"><AdaptationSet mimeType="audio/mp4">
                    <ContentProtection cenc:default_KID="{Kid}"/>
                    <Representation id="audio" codecs="mp4a.40.2" bandwidth="128000">
                      <SegmentList duration="1">
                        {(withInit ? "<Initialization sourceURL=\"init, 中文.mp4\"/>" : "")}
                        <SegmentURL media="source, 中文.mp4"/>
                        {(withInit ? "<SegmentURL media=\"second.mp4\"/>" : "")}
                      </SegmentList>
                    </Representation>
                  </AdaptationSet></Period>
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            Assert.Equal(withInit ? 2 : 1, streams.Single().Playlist!.MediaParts.Single().MediaSegments.Count);
            var options = CreateOptions(root);
            options.DecryptionEngine = DecryptEngine.SHAKA_PACKAGER;
            // ffmpeg 拒绝 Shaka 参数并返回非零退出码，模拟解密失败而非启动异常。
            options.DecryptionBinaryPath = "ffmpeg";
            options.Keys = [$"{Kid}:{Key}"];
            options.MP4RealTimeDecryption = true;
            // 保留分片时走逐片解密，覆盖 init 后的并发失败路径。
            options.SkipMerge = withInit;
            options.CheckSegmentsCount = false;
            options.MuxAfterDone = true;
            options.MuxOptions = new() { MuxFormat = MuxFormat.MP4 };
            var config = new DownloaderConfig { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options };
            if (live)
            {
                options.LiveRealTimeMerge = true;
                options.LiveWaitTime = 1;
                options.LiveTakeCount = 15;
                var manager = new SimpleLiveRecordManager2(config, streams, extractor);
                Assert.False(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(10)));
            }
            else
            {
                var manager = new SimpleDownloadManager(config, streams, extractor);
                Assert.False(await manager.StartDownloadAsync());
            }
            Assert.Empty(Directory.GetFiles(options.SaveDir!));
            var preserved = Directory.GetFiles(Path.Combine(root, "tmp"), "*", SearchOption.AllDirectories);
            Assert.Equal(withInit ? 3 : 1, preserved.Length);
            Assert.Contains(preserved, file => File.ReadAllBytes(file).SequenceEqual(original));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task CreateEncryptedMedia(string root, bool withInit)
    {
        var plain = Path.Combine(root, "plain.mp4");
        var source = Path.Combine(root, "source, 中文.mp4");
        await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=48000",
            "-t", "1", "-c:a", "aac", "-movflags", "+frag_keyframe+empty_moov+default_base_moof", plain);
        // 加密样本先使用 ASCII 路径，避免 Bento4 的 Windows 路径限制影响测试。
        var encrypted = Path.Combine(root, "encrypted.mp4");
        await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{Key}:random",
            "--property", $"1:KID:{Kid}", plain, encrypted);
        File.Move(encrypted, source);
        if (!withInit)
            return;
        var bytes = await File.ReadAllBytesAsync(source);
        var offset = 0;
        while (!bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
            offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
        await File.WriteAllBytesAsync(Path.Combine(root, "init, 中文.mp4"), bytes[..offset]);
        await File.WriteAllBytesAsync(source, bytes[offset..]);
    }

    private static Task<string> DecodedHash(string path) => Run("ffmpeg", "-v", "error", "-xerror", "-i", path,
        "-f", "hash", "-hash", "sha256", "-");
}
