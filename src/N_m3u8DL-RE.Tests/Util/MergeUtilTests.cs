using System.Diagnostics;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class MergeUtilTests
{
    // Issue #338 / #89: ffmpeg's concat protocol opens every segment at once and
    // fails with "Too many open files" when the OS file-handle limit is low.
    // The merge should detect that specific error and fall back to the concat demuxer.
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

    [Fact]
    public void MuxInputsByFFmpeg_KeepsAudioFromACombinedVideoAudioInput()
    {
        var ffmpeg = FfmpegPath();
        if (ffmpeg == null) return; // no ffmpeg on PATH - skip rather than fail the run

        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var combined = Path.Combine(dir, "combined.mp4");
            MakeCombined(ffmpeg, combined);

            var files = new[]
            {
                new OutputFile { Index = 0, FilePath = combined, MediaType = MediaType.VIDEO },
            };
            var outPath = Path.Combine(dir, "out");
            Assert.True(MergeUtil.MuxInputsByFFmpeg(ffmpeg, files, outPath, MuxFormat.MKV, dateinfo: false));

            var (video, audio) = CountStreams(ffmpeg, outPath + ".mkv");
            Assert.Equal(1, video);
            Assert.Equal(1, audio);
        }
        finally { Directory.Delete(dir, true); }
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
