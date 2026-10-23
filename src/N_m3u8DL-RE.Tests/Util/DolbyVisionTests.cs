using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

[Collection("Download console")]
public class DolbyVisionTests
{
    [Theory]
    [InlineData("ffmpeg version 8.0\nlibavformat    62.  3.100 / 62.  3.100", false)]
    [InlineData("ffmpeg version 9.0\nlibavformat    63.  1.100 / 63.  1.100", true)]
    [InlineData("ffmpeg version N-12345\nlibavformat    63.  2.100 / 63.  2.100", true)]
    [InlineData("libavformat 64.0.100", true)]
    [InlineData("unknown", false)]
    public void LibraryVersionDeterminesCompleteDolbyVisionSupport(string banner, bool supported)
    {
        Assert.Equal(supported, BinaryToolUtil.FFmpegSupportsDolbyVision(banner));
    }

    [Theory]
    [InlineData(false, "modern")]
    [InlineData(true, "modern")]
    [InlineData(false, "legacy")]
    [InlineData(true, "legacy")]
    [InlineData(false, "mkvmerge")]
    [InlineData(true, "mkvmerge")]
    [InlineData(false, "binary")]
    [InlineData(true, "binary")]
    public async Task DownloadAndLiveRespectMuxerAndBinaryMergeChoices(bool live, string scenario)
    {
        var legacy = scenario is "legacy" or "mkvmerge";
        if (!legacy && (!HasTool("ffmpeg") || !await BinaryToolUtil.FFmpegSupportsDolbyVisionAsync("ffmpeg")))
            return;
        var root = Directory.CreateTempSubdirectory("dovi-options-").FullName;
        try
        {
            var options = new MyOption
            {
                FFmpegBinaryPath = legacy ? Path.Combine(root, "missing-ffmpeg") : "ffmpeg",
                BinaryMerge = scenario == "binary", MuxAfterDone = true,
                MuxOptions = new() { UseMkvmerge = scenario == "mkvmerge" }
            };
            var config = new DownloaderConfig { MyOptions = options, DirPrefix = root };
            using var extractor = new StreamExtractor(new ParserConfig());
            var stream = new StreamSpec();
            List<Mediainfo> infos = [new() { Type = "Video", BaseInfo = "hevc", DolbyVison = true }];
            if (live)
                await new SimpleLiveRecordManager2(config, [], extractor).ChangeSpecInfoAsync(stream, infos, false);
            else
                await new SimpleDownloadManager(config, [], extractor).ChangeSpecInfoAsync(stream, infos, false);

            Assert.Equal(legacy || scenario == "binary", options.BinaryMerge);
            Assert.Equal(scenario != "legacy", options.MuxAfterDone);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("parts")]
    [InlineData("mux")]
    [InlineData("decrypt")]
    [InlineData("merge-mkv")]
    [InlineData("mux-mkv")]
    [InlineData("parts-mkv")]
    public async Task RemuxPathsPreserveDolbyVisionConfigurationAndVideoPackets(string operation)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || operation == "decrypt" && !OnPath("mp4encrypt"))
            return;
        var root = Directory.CreateTempSubdirectory("dovi-remux-").FullName;
        try
        {
            var source = await WriteSampleAsync(root);
            var matroska = operation.EndsWith("-mkv", StringComparison.Ordinal);
            if (matroska)
            {
                // 复用小型样本覆盖 MP4 的 dvh1 与 Matroska 的 DV FourCC 冲突。
                var tagged = Path.Combine(root, "tagged.mp4");
                await Run("ffmpeg", "-v", "error", "-i", source, "-map", "0:v", "-c", "copy",
                    "-strict", "unofficial", "-tag:v:1", "dvh1", tagged);
                source = tagged;
            }
            var output = Path.Combine(root, matroska ? "output.mkv" : "output.mp4");
            switch (operation)
            {
                case "merge":
                case "merge-mkv":
                    Assert.Equal(0, await MergeUtil.MergeByFFmpegAsync("ffmpeg", [source], output, FFmpegConcatMode.LOCAL_HTTP));
                    break;
                case "parts":
                case "parts-mkv":
                    Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", [source, source],
                        [new() { MediaSegments = [new() { Duration = 1 }] }, new() { MediaSegments = [new() { Duration = 1 }] }], output));
                    break;
                case "mux":
                case "mux-mkv":
                    Assert.Equal(0, await MergeUtil.MuxInputsAsync("ffmpeg", [new OutputFile { Index = 0, FilePath = source }], output));
                    break;
                case "decrypt":
                    var encrypted = Path.Combine(root, "encrypted.mp4");
                    var kid = new string('1', 32);
                    var key = new string('a', 32);
                    var fragmented = Path.Combine(root, "fragmented.mp4");
                    await Run("ffmpeg", "-v", "error", "-i", source, "-map", "0:v", "-c", "copy", "-strict", "unofficial",
                        "-movflags", "+frag_keyframe+empty_moov+default_base_moof", fragmented);
                    await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random", "--property", $"1:KID:{kid}",
                        "--key", $"2:{key}:random", "--property", $"2:KID:{kid}", fragmented, encrypted);
                    Assert.True(MP4DecryptUtil.HasEncryptedTracks(encrypted));
                    Assert.True(await MP4DecryptUtil.DecryptAsync(DecryptEngine.FFMPEG, "ffmpeg", [$"{kid}:{key}"], encrypted, output, kid));
                    break;
            }
            async Task<JsonDocument> Probe(string path) => JsonDocument.Parse(await Run("ffprobe", "-v", "error",
                "-select_streams", "v", "-show_streams", "-show_packets", "-show_data_hash", "sha256", "-of", "json", path));
            using var original = await Probe(source);
            using var result = await Probe(output);
            // Profile 7 样本的基础层与增强层分别占用一条视频流，不能只检查第一条流。
            static string[] Configurations(JsonDocument document) => document.RootElement.GetProperty("streams").EnumerateArray()
                .Where(stream => stream.TryGetProperty("side_data_list", out _))
                .SelectMany(stream => stream.GetProperty("side_data_list").EnumerateArray())
                .Where(item => item.GetProperty("side_data_type").GetString() == "DOVI configuration record")
                .Select(item => item.ToString()).ToArray();
            var expected = Configurations(original);
            Assert.NotEmpty(expected);
            Assert.Equal(expected, Configurations(result));
            var hashes = original.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()).ToArray();
            Assert.Equal(operation.StartsWith("parts", StringComparison.Ordinal) ? hashes.Concat(hashes) : hashes,
                result.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetProperty("data_hash").GetString()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ProfileMetadataBelongsToItsOwnVideoStream()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("dovi-track-info-").FullName;
        try
        {
            var source = await WriteSampleAsync(root);
            var file = new OutputFile { Index = 0, FilePath = source };
            var fromBoxes = await MediainfoUtil.ReadTrackInfoAsync("ffmpeg", file, default);
            var fromProbe = await MediainfoUtil.ReadInfoAsync("ffmpeg", source);
            Assert.Equal(new int?[] { null, 7 }, fromBoxes.Select(info => info.DolbyVisionProfile));
            Assert.Equal(fromBoxes.Select(info => info.DolbyVisionProfile), fromProbe.Select(info => info.DolbyVisionProfile));
            Assert.False(fromProbe[0].DolbyVison);
            Assert.True(fromProbe[1].DolbyVison);
            Assert.Same(fromBoxes, await MediainfoUtil.ReadTrackInfoAsync("missing-ffmpeg", file, default));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MatroskaTagsFollowUniqueVideoStreamsAcrossProgramsAndInputs()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("dovi-programs-").FullName;
        try
        {
            var shared = Path.Combine(root, "shared.ts");
            // 两个节目共用一条 H.264 视频流，随后混入双层杜比视界视频。
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-t", "1",
                "-c:v", "libx264", "-program", "program_num=1:st=0", "-program", "program_num=2:st=0", shared);
            var source = await WriteSampleAsync(root);
            var tagged = Path.Combine(root, "tagged.mp4");
            await Run("ffmpeg", "-v", "error", "-i", source, "-map", "0:v", "-c", "copy",
                "-strict", "unofficial", "-tag:v:1", "dvh1", tagged);
            var output = Path.Combine(root, "mixed.mkv");
            Assert.Equal(0, await MergeUtil.MuxInputsAsync("ffmpeg",
                [new OutputFile { Index = 0, FilePath = shared }, new OutputFile { Index = 1, FilePath = tagged }], output));
            async Task<JsonDocument> Probe(string path) => JsonDocument.Parse(await Run("ffprobe", "-v", "error",
                "-select_streams", "v", "-show_streams", "-show_packets", "-show_data_hash", "sha256", "-of", "json", path));
            using var second = await Probe(tagged);
            using var result = await Probe(output);
            static IEnumerable<string[]> VideoPackets(JsonDocument document) => document.RootElement.GetProperty("streams").EnumerateArray()
                .Select(stream => document.RootElement.GetProperty("packets").EnumerateArray()
                    .Where(packet => packet.GetProperty("stream_index").GetInt32() == stream.GetProperty("index").GetInt32())
                    .Select(packet => packet.GetProperty("data_hash").GetString()!).ToArray());
            Assert.Equal(new[] { "h264", "hevc", "hevc" }, result.RootElement.GetProperty("streams").EnumerateArray()
                .Select(stream => stream.GetProperty("codec_name").GetString()));
            Assert.Equal(VideoPackets(second).ToArray(), VideoPackets(result).Skip(1).ToArray());
            async Task<string> DecodedHash(string path) => await Run("ffmpeg", "-v", "error", "-i", path,
                "-map", "0:v:0", "-f", "hash", "-hash", "sha256", "-");
            // TS 与 MKV 中 H.264 的包封装不同，比较解码结果；HEVC 则逐包比较。
            Assert.Equal(await DecodedHash(shared), await DecodedHash(output));
            Assert.Contains(result.RootElement.GetProperty("streams")[2].GetProperty("side_data_list").EnumerateArray(),
                item => item.GetProperty("side_data_type").GetString() == "DOVI configuration record" && item.GetProperty("dv_profile").GetInt32() == 7);
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<string> WriteSampleAsync(string root)
    {
        var source = Path.Combine(root, "source.mp4");
        // FFmpeg 的公开 FATE Profile 7 样本，约 9 KiB：
        // https://fate-suite.ffmpeg.org/mov/dovi-p7.mp4
        await using (var resource = typeof(DolbyVisionTests).Assembly.GetManifestResourceStream(
            "N_m3u8DL_RE.Tests.Resources.Media.DolbyVisionProfile7.mp4")!)
        await using (var file = File.Create(source))
            await resource.CopyToAsync(file);
        return source;
    }
}
