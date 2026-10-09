using System.CommandLine;
using System.Globalization;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public sealed class MuxSubtitleTimelineIntegrationTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-mux-timeline-").FullName;

    [Theory]
    [InlineData(true, "ffmpeg")]
    [InlineData(false, "ffmpeg")]
    [InlineData(true, "mkvmerge")]
    [InlineData(false, "mkvmerge")]
    public async Task BroadcastSubtitleRepairIsEnabledByDefaultAndCanBeDisabled(bool repair, string muxer)
    {
        if (!HasTool("ffmpeg"))
            return;
        if (muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var media = await Video("video.mp4", 1000);
        var subtitle = Path.Combine(directory, "subtitles.srt");
        var original = "1\n00:16:40,500 --> 00:16:41,000\nHello\n\n";
        File.WriteAllText(subtitle, original);
        var output = Path.Combine(directory, muxer == "mkvmerge" ? "output.mkv" : "output.mp4");
        List<string> args = ["mux", "-i", media, "--mux-import", $"path={subtitle}:lang=eng:name=English", "-o", output, "--muxer", muxer];
        if (!repair)
            args.AddRange(["--auto-subtitle-fix", "false"]);
        Assert.Equal(0, await Invoke(args.ToArray()));
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains(repair ? "00:00:00,500 --> 00:00:01,000" : "00:16:40,500 --> 00:16:41,000", text);
        Assert.Equal(original, File.ReadAllText(subtitle));
        Assert.True(File.Exists(media));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("mkvmerge")]
    public async Task RelativeSubtitleKeepsItsOpeningGapAndAudioKeepsItsDelay(string muxer)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        if (muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var media = await Video("video.mp4", 1000);
        var audio = Path.Combine(directory, "audio.m4a");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=3", "-c:a", "aac",
            "-output_ts_offset", "1000.2", audio);
        var subtitle = Path.Combine(directory, "relative.srt");
        var original = "1\n00:00:00,500 --> 00:00:01,000\nHello\n\n";
        File.WriteAllText(subtitle, original);
        var output = Path.Combine(directory, muxer == "mkvmerge" ? "output.mkv" : "output.mp4");
        Assert.Equal(0, await Invoke("mux", "-i", media, "-i", audio, "-i", subtitle, "-o", output, "--muxer", muxer));
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains("00:00:00,500 --> 00:00:01,000", text);
        var start = await Run("ffprobe", "-v", "error", "-select_streams", "a:0", "-show_entries", "stream=start_time", "-of", "csv=p=0", output);
        Assert.InRange(double.Parse(start.Trim(), CultureInfo.InvariantCulture), 0.15, 0.21);
        Assert.Equal(original, File.ReadAllText(subtitle));
    }

    [Fact]
    public async Task TimestampMappedVttIsAlignedBeforeMuxing()
    {
        if (!HasTool("ffmpeg"))
            return;
        var media = await Video("video.mp4", 1000);
        var subtitle = Path.Combine(directory, "mapped.vtt");
        File.WriteAllText(subtitle, "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:05.000,MPEGTS:90000000\n\n" +
            "00:00:05.500 --> 00:00:06.000\nHello\n\n");
        var output = Path.Combine(directory, "output.mp4");
        Assert.Equal(0, await Invoke("mux", "-i", media, "-i", subtitle, "-o", output));
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains("00:00:00,500 --> 00:00:01,000", text);
        Assert.Contains("X-TIMESTAMP-MAP", File.ReadAllText(subtitle));
    }

    [Fact]
    public async Task MatroskaDurationExcludesTheBroadcastClockPrefix()
    {
        if (!HasTool("ffmpeg"))
            return;
        var media = await Video("video.mkv", 1000);
        var subtitle = Path.Combine(directory, "subtitles.srt");
        File.WriteAllText(subtitle, "1\n00:16:40,500 --> 00:16:41,000\nHello\n\n");
        var output = Path.Combine(directory, "output.mp4");
        Assert.Equal(0, await Invoke("mux", "-i", media, "--mux-import", $"path={subtitle}:lang=eng:name=English", "-o", output));
        var text = await Run("ffmpeg", "-v", "error", "-i", output, "-map", "0:s:0", "-f", "srt", "-");
        Assert.Contains("00:00:00,500 --> 00:00:01,000", text);
    }

    [Fact]
    public async Task Mp4RepairNeedsNoProbeProcessAndTemporaryFilesAreRemoved()
    {
        if (!HasTool("ffmpeg"))
            return;
        var media = await Video("video.mp4", 1000);
        var subtitle = Path.Combine(directory, "subtitles.srt");
        File.WriteAllText(subtitle, "1\n00:16:40,500 --> 00:16:41,000\nHello\n\n");
        string repaired;
        using (var timeline = await MuxSubtitleTimeline.CreateAsync([media, subtitle], "/missing/ffmpeg", default))
        {
            repaired = timeline.Inputs[1];
            Assert.NotEqual(subtitle, repaired);
            Assert.True(File.Exists(repaired));
        }
        Assert.False(File.Exists(repaired));
    }

    [Fact]
    public async Task FailedMuxPreservesOutputAndRemovesRepairedSubtitle()
    {
        if (!HasTool("ffmpeg"))
            return;
        var media = await Video("video.mp4", 1000);
        var subtitle = Path.Combine(directory, "subtitles.srt");
        var original = "1\n00:16:40,500 --> 00:16:41,000\nHello\n\n";
        File.WriteAllText(subtitle, original);
        var output = Path.Combine(directory, "old.m4a");
        File.WriteAllBytes(output, [9, 8]);
        var temporaryFiles = Directory.GetFiles(Path.GetTempPath(), "re-sub-*").ToHashSet();
        // 输入只有视频和字幕，无法写出纯音频容器，触发修复后的混流失败。
        Assert.Equal(1, await Invoke("mux", "-i", media, "-i", subtitle, "-o", output, "--overwrite"));
        Assert.Equal(new byte[] { 9, 8 }, File.ReadAllBytes(output));
        Assert.Equal(original, File.ReadAllText(subtitle));
        Assert.Empty(Directory.GetFiles(Path.GetTempPath(), "re-sub-*").Except(temporaryFiles));
    }

    [Fact]
    public void ConfigurationCanDisableAutomaticSubtitleRepair()
    {
        var command = CommandInvoker.CreateRootCommand();
        var config = new ConfigFile(["mux", "-i", "video.mp4", "-o", "output.mp4"], ["--auto-subtitle-fix", "false"]);
        var result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        Assert.False(result.GetValue<bool>("--auto-subtitle-fix"));
        config = new ConfigFile(["mux", "-i", "video.mp4", "-o", "output.mp4", "--auto-subtitle-fix", "true"], config.Defaults);
        result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        Assert.True(result.GetValue<bool>("--auto-subtitle-fix"));
    }

    private async Task<string> Video(string name, double origin)
    {
        var path = Path.Combine(directory, name);
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25", "-t", "3",
            "-c:v", "libx264", "-preset", "ultrafast", "-output_ts_offset", origin.ToString(CultureInfo.InvariantCulture), path);
        return path;
    }

    private static async Task<int> Invoke(params string[] args) => await CommandInvoker.CreateRootCommand().Parse(args,
        new ParserConfiguration { EnablePosixBundling = false, ResponseFileTokenReplacer = null }).InvokeAsync();
    public void Dispose() => Directory.Delete(directory, true);
}
