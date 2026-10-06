using System.Text.Json;
using System.Globalization;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

[Collection("Download console")]
public class FFmpegConcatTests
{
    [Theory]
    [InlineData("continuous", "mp4", false)]
    [InlineData("reset", "mp4", false)]
    [InlineData("mp4", "mp4", false)]
    [InlineData("continuous", "mp4", true)]
    [InlineData("reset", "mp4", true)]
    [InlineData("mp4", "mp4", true)]
    [InlineData("continuous", "mkv", false)]
    [InlineData("reset", "mkv", false)]
    [InlineData("continuous", "ts", false)]
    [InlineData("reset", "ts", false)]
    public async Task LocalInputPreservesLegacyPacketsAndTimestamps(string mode, string format, bool fastStart)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("ffmpeg-concat-").FullName;
        try
        {
            string[] files;
            if (mode == "mp4")
            {
                // moov 在尾部，需要先读尾部再回到 mdat；任意字节边界不能被当成媒体文件边界。
                var source = Path.Combine(root, "source.mp4");
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                    "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", source);
                var content = await File.ReadAllBytesAsync(source);
                files = Enumerable.Range(0, 20).Select(i => Path.Combine(root, $"piece{i:D2}.bin")).ToArray();
                for (var i = 0; i < files.Length; i++)
                    await File.WriteAllBytesAsync(files[i], content[(content.Length * i / files.Length)..(content.Length * (i + 1) / files.Length)]);
            }
            else
            {
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                    "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "12",
                    "-c:v", "libx264", "-preset", "ultrafast", "-g", "5", "-bf", "0", "-c:a", "aac",
                    "-f", "segment", "-segment_time", "0.2", Path.Combine(root, "segment%03d.ts"));
                files = Directory.GetFiles(root, "segment*.ts").Order().ToArray();
                if (mode == "reset")
                    files = Enumerable.Repeat(files[0], 60).ToArray();
            }
            var legacy = Path.Combine(root, "legacy");
            var local = Path.Combine(root, "local");
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", files, legacy, format, useAACFilter: mode != "mp4",
                fastStart: fastStart, writeDate: false, concatMode: FFmpegConcatMode.PROTOCOL));
            // 不传模式参数，确保实际默认分支就是本机虚拟输入。
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", files, local, format, useAACFilter: mode != "mp4",
                fastStart: fastStart, writeDate: false));
            Assert.Equal(await Packets(legacy + "." + format), await Packets(local + "." + format));
            // MKV 每次封装会生成随机标识；MP4/TS 关闭日期后还应逐字节一致。
            if (format != "mkv")
                Assert.Equal(await File.ReadAllBytesAsync(legacy + "." + format), await File.ReadAllBytesAsync(local + "." + format));
            Assert.All(files, file => Assert.True(File.Exists(file)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ThousandsOfFilesMergeWithoutIntermediateFiles()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("ffmpeg-many-files-").FullName;
        try
        {
            var source = Path.Combine(root, "source.ts");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", source);
            var data = await File.ReadAllBytesAsync(source);
            var files = Enumerable.Range(0, 2000).Select(i => Path.Combine(root, $"片段 '{i:D4}.bin")).ToArray();
            for (var i = 0; i < files.Length; i++)
                await File.WriteAllBytesAsync(files[i], data[(data.Length * i / files.Length)..(data.Length * (i + 1) / files.Length)]);
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", [source], Path.Combine(root, "reference"), "mp4",
                useAACFilter: false, writeDate: false, concatMode: FFmpegConcatMode.PROTOCOL));
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", files, Path.Combine(root, "output"), "mp4",
                useAACFilter: false, writeDate: false));
            Assert.Equal(await Packets(Path.Combine(root, "reference.mp4")), await Packets(Path.Combine(root, "output.mp4")));
            Assert.Equal(await File.ReadAllBytesAsync(Path.Combine(root, "reference.mp4")), await File.ReadAllBytesAsync(Path.Combine(root, "output.mp4")));
            Assert.Equal(files.Length + 3, Directory.GetFiles(root).Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("aac", "m4a")]
    [InlineData("eac3", "eac3")]
    public async Task AudioLocalInputPreservesLegacyBytes(string codec, string format)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("ffmpeg-audio-concat-").FullName;
        try
        {
            var source = Path.Combine(root, "source.ts");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=1",
                "-c:a", codec, source);
            var legacy = Path.Combine(root, "legacy");
            var local = Path.Combine(root, "local");
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", [source, source], legacy, format,
                useAACFilter: codec == "aac", writeDate: false, concatMode: FFmpegConcatMode.PROTOCOL));
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", [source, source], local, format,
                useAACFilter: codec == "aac", writeDate: false));
            Assert.Equal(await File.ReadAllBytesAsync(legacy + "." + format), await File.ReadAllBytesAsync(local + "." + format));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DemuxerModeStillMergesSeparateFiles()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("ffmpeg-demuxer-").FullName;
        try
        {
            var source = Path.Combine(root, "source.ts");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=duration=1",
                "-c:a", "aac", source);
            Assert.True(MergeUtil.MergeByFFmpeg("ffmpeg", [source, source], Path.Combine(root, "output"), "m4a",
                useAACFilter: true, concatMode: FFmpegConcatMode.DEMUXER));
            var result = await Run("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                Path.Combine(root, "output.m4a"));
            Assert.InRange(double.Parse(result.Trim(), CultureInfo.InvariantCulture), 1.9, 2.2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(1799, FFmpegConcatMode.PROTOCOL, false)]
    [InlineData(1800, FFmpegConcatMode.PROTOCOL, true)]
    [InlineData(100000, FFmpegConcatMode.LOCAL_HTTP, false)]
    [InlineData(100000, FFmpegConcatMode.DEMUXER, false)]
    public void OnlyDirectProtocolNeedsPartialMerge(int count, FFmpegConcatMode mode, bool expected) =>
        Assert.Equal(expected, MergeUtil.ShouldPartialMerge(count, mode));

    private static async Task<string[]> Packets(string path)
    {
        var text = await Run("ffprobe", "-v", "error", "-show_packets", "-show_data_hash", "sha256",
            "-show_entries", "packet=stream_index,pts,dts,duration,size,flags,data_hash", "-of", "json", path);
        using var json = JsonDocument.Parse(text);
        return json.RootElement.GetProperty("packets").EnumerateArray().Select(packet => packet.GetRawText()).ToArray();
    }
}