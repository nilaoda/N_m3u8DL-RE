using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class MergeUtilTests
{
    // Issue #338 / #89: ffmpeg's concat protocol opens every segment at once and
    // fails with "Too many open files" when the OS file-handle limit is low.
    // Detect the specific failure and preserve segments for a manual retry.
    [Theory]
    [InlineData("[in#0 @ 0x7ff736704e40] Error opening input: Too many open files")]
    [InlineData("Error opening input files: Too many open files")]
    [InlineData("error opening input files: TOO MANY OPEN FILES")]
    public void IsTooManyOpenFilesError_DetectsFdExhaustion(string output)
    {
        Assert.True(MergeUtil.IsTooManyOpenFilesError(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invalid data found when processing input")]
    [InlineData("Packet duration: 4191386680 / dts: 4210142341 is out of range")]
    public void IsTooManyOpenFilesError_IgnoresUnrelatedErrors(string output)
    {
        Assert.False(MergeUtil.IsTooManyOpenFilesError(output));
    }

    // MuxInputsByFFmpeg used to "-map {i}" whole input files. Some sites' HLS
    // segments carry a data stream (timed_id3) next to the real track, and a
    // blanket map pulls that in too - Matroska/MP4 then refuse the mux outright
    // ("Only audio, video, and subtitles are supported"), even with
    // -ignore_unknown set. These exercise the real ffmpeg invocation end to end,
    // covering both shapes that matter: tracks split across separate input
    // files, and a single input file that already combines video and audio (a
    // per-file MediaType switch would misclassify that one as video-only and
    // silently drop its audio - a regression this test would have caught).
    private static string? FfmpegPath()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("ffmpeg", "-version")
            { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit();
            return p.ExitCode == 0 ? "ffmpeg" : null;
        }
        catch { return null; }
    }

    private static void MakeVideo(string ffmpeg, string path) =>
        Process.Start(ffmpeg, $"-y -f lavfi -i testsrc=size=64x64:duration=1 -c:v libx264 \"{path}\"")!.WaitForExit();

    private static void MakeAudio(string ffmpeg, string path) =>
        Process.Start(ffmpeg, $"-y -f lavfi -i sine=frequency=440:duration=1 -c:a aac \"{path}\"")!.WaitForExit();

    private static void MakeCombined(string ffmpeg, string path) =>
        Process.Start(ffmpeg,
            $"-y -f lavfi -i testsrc=size=64x64:duration=1 -f lavfi -i sine=frequency=440:duration=1 " +
            $"-c:v libx264 -c:a aac \"{path}\"")!.WaitForExit();

    private static (int video, int audio) CountStreams(string ffmpeg, string path)
    {
        using var p = Process.Start(new ProcessStartInfo(ffmpeg, $"-i \"{path}\"")
        { RedirectStandardError = true, UseShellExecute = false });
        var stderr = p!.StandardError.ReadToEnd();
        p.WaitForExit();
        return (stderr.Split("Video:").Length - 1, stderr.Split("Audio:").Length - 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MuxKeepsAudioAndMetadataFromACombinedVideoAudioInput(bool useMkvmerge)
    {
        var ffmpeg = FfmpegPath();
        if (ffmpeg == null) return; // no ffmpeg on PATH - skip rather than fail the run
        if (!HasTool("ffprobe") || useMkvmerge && !OnPath("mkvmerge")) return;

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var combined = Path.Combine(dir, "combined.mp4");
            MakeCombined(ffmpeg, combined);

            var files = new[]
            {
                new OutputFile { Index = 0, FilePath = combined, MediaType = MediaType.VIDEO,
                    LangCode = "en", Description = "Main [Track] \"Original\"" },
            };
            var outPath = Path.Combine(dir, "out");
            Assert.True(useMkvmerge ? MergeUtil.MuxInputsByMkvmerge("mkvmerge", files, outPath)
                : MergeUtil.MuxInputsByFFmpeg(ffmpeg, files, outPath, MuxFormat.MKV, dateinfo: false));

            var (video, audio) = CountStreams(ffmpeg, outPath + ".mkv");
            Assert.Equal(1, video);
            Assert.Equal(1, audio);
            using var json = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries",
                "stream_tags=language,title", "-of", "json", outPath + ".mkv"));
            Assert.All(json.RootElement.GetProperty("streams").EnumerateArray(), stream =>
            {
                Assert.Equal("eng", stream.GetProperty("tags").GetProperty("language").GetString());
                Assert.Equal("Main [Track] \"Original\"", stream.GetProperty("tags").GetProperty("title").GetString());
            });
        }
        finally { Directory.Delete(dir, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MuxRecoversTimestampFreeParameterPacketsWithoutChangingFrames(bool preserveTimestamp)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe")) return;
        var root = Directory.CreateTempSubdirectory("mux-missing-timestamps-").FullName;
        try
        {
            var clean = Path.Combine(root, "clean.ts");
            var elementary = Path.Combine(root, "video.h264");
            var source = Path.Combine(root, "source.ts");
            var output = Path.Combine(root, "output.mkv");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-f", "lavfi", "-i", "sine=sample_rate=48000", "-t", "2", "-c:v", "libx264",
                "-bf", "2", "-c:a", "aac", "-f", "mpegts", clean);
            await Run("ffmpeg", "-v", "error", "-i", clean, "-map", "0:v", "-c", "copy", "-f", "h264", elementary);
            var video = File.ReadAllBytes(elementary);
            var start = video.AsSpan().IndexOf(new byte[] { 0, 0, 0, 1, 0x68 });
            Assert.True(start >= 0);
            var end = start + 4 + video.AsSpan(start + 4).IndexOf(new byte[] { 0, 0, 1 });
            while (video[end - 1] == 0) end--;
            var pps = video[start..end];
            var bytes = File.ReadAllBytes(clean);
            var continuity = bytes.Chunk(188).Last(packet => ((packet[1] & 31) << 8 | packet[2]) == 256)[3] & 15;
            // 追加一个只有 PPS、没有 PTS/DTS 的 PES，复现腾讯 TS 的参数包。
            // TS adaptation field 填满剩余空间，避免填充字节被误当作视频数据。
            byte[] pes = [0, 0, 1, 0xe0, 0, (byte)(3 + pps.Length), 0x80, 0, 0, .. pps];
            var padding = 183 - pes.Length;
            byte[] packet = [0x47, 0x41, 0, (byte)(0x30 | ((continuity + 1) & 15)), (byte)padding, 0,
                .. Enumerable.Repeat((byte)0xff, padding - 1), .. pes];
            Assert.Equal(188, packet.Length);
            await File.WriteAllBytesAsync(source, [.. bytes, .. packet]);
            var original = await File.ReadAllBytesAsync(source);
            var baseline = await ProcessUtil.RunAsync("ffmpeg", ["-v", "warning", "-i", source, "-c", "copy", output], default);
            Assert.NotEqual(0, baseline.ExitCode);
            Assert.Contains("Can't write packet with unknown timestamp", baseline.Error);
            File.Delete(output);
            var updates = new List<MediaProgress>();
            Assert.Equal(0, await MergeUtil.MuxInputsAsync("ffmpeg",
                [new OutputFile { Index = 0, FilePath = source, PreserveTimestamp = preserveTimestamp }], output,
                downloadDefaults: preserveTimestamp, progress: updates.Add));
            Assert.NotEmpty(updates);
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            // PPS 本身没有画面；比较真实解码帧及音频，不能仅以退出码判断成功。
            async Task<string[]> Frames(string file) => (await Run("ffmpeg", "-v", "error", "-i", file,
                "-map", "0:v", "-fps_mode", "passthrough", "-f", "framemd5", "-"))
                .Split('\n').Where(line => !line.StartsWith('#') && line.Contains(','))
                .Select(line => line.Split(',')[^1].Trim()).ToArray();
            var frames = await Frames(output);
            Assert.Equal(50, frames.Length);
            Assert.Equal(await Frames(clean), frames);
            Assert.Equal(await Run("ffmpeg", "-v", "error", "-i", clean, "-map", "0:a", "-f", "hash", "-"),
                await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:a", "-f", "hash", "-"));
            if (preserveTimestamp)
            {
                async Task<double> Start(string file) => double.Parse((await Run("ffprobe", "-v", "error",
                    "-select_streams", "v", "-show_entries", "stream=start_time", "-of", "csv=p=0", file)).Split('\n')[0],
                    CultureInfo.InvariantCulture);
                Assert.InRange(await Start(output) - await Start(clean), -0.001, 0.001);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MuxInputsByFFmpeg_CombinesSeparateVideoAndAudioInputs()
    {
        var ffmpeg = FfmpegPath();
        if (ffmpeg == null) return;

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var video = Path.Combine(dir, "video.mp4");
            var audio = Path.Combine(dir, "audio.m4a");
            MakeVideo(ffmpeg, video);
            MakeAudio(ffmpeg, audio);

            var files = new[]
            {
                new OutputFile { Index = 0, FilePath = video, MediaType = MediaType.VIDEO },
                new OutputFile { Index = 1, FilePath = audio, MediaType = MediaType.AUDIO },
            };
            var outPath = Path.Combine(dir, "out");
            Assert.True(MergeUtil.MuxInputsByFFmpeg(ffmpeg, files, outPath, MuxFormat.MKV, dateinfo: false));

            var (videoCount, audioCount) = CountStreams(ffmpeg, outPath + ".mkv");
            Assert.Equal(1, videoCount);
            Assert.Equal(1, audioCount);
        }
        finally { Directory.Delete(dir, true); }
    }
}
