using System.CommandLine;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Resource;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public sealed class ToolDirectoryTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-tool-directories-").FullName;

    [Theory]
    [InlineData("-i")]
    [InlineData("--input-dir")]
    public async Task DirectoryDiscoveryUsesNaturalOrderAndExplicitPatterns(string option)
    {
        var clips = Directory.CreateDirectory(Path.Combine(directory, "clips")).FullName;
        foreach (var index in new[] { 10, 2, 1 })
            File.WriteAllBytes(Path.Combine(clips, $"clip{index}.MP4"), [(byte)index]);
        File.WriteAllBytes(Path.Combine(clips, "notes.txt"), [99]);
        File.WriteAllBytes(Path.Combine(clips, "clip3.mp4.tmp"), [99]);
        File.WriteAllBytes(Path.Combine(clips, ".re-partial.mp4"), [99]);
        File.WriteAllBytes(Path.Combine(clips, "_init.mp4"), [99]);
        File.WriteAllBytes(Path.Combine(clips, "_initial.mp4"), [4]);
        var nested = Directory.CreateDirectory(Path.Combine(clips, "nested")).FullName;
        File.WriteAllBytes(Path.Combine(nested, "other.mp4"), [99]);
        var output = Path.Combine(directory, "joined.bin");
        Assert.Equal(0, await Invoke("concat", option, clips, "-o", output));
        Assert.Equal(new byte[] { 1, 2, 10, 4 }, File.ReadAllBytes(output));
        Assert.Equal(0, await Invoke("concat", option, clips, "--pattern", "notes.*", "-o", output, "--overwrite"));
        Assert.Equal(new byte[] { 99 }, File.ReadAllBytes(output));
        Assert.Equal(8, Directory.GetFiles(clips).Length);
    }

    [Theory]
    [InlineData("-i", "named")]
    [InlineData("--input-dir", "named")]
    [InlineData("-i", "numeric")]
    [InlineData("-i", "mixed")]
    public async Task CompleteMp4FilesMergeWithoutManualModeAndKeepFrameOrder(string option, string names)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var clips = Directory.CreateDirectory(Path.Combine(directory, "clips")).FullName;
        var expected = new List<string>();
        foreach (var (index, color) in new[] { (1, "red"), (2, "green"), (10, "blue") })
        {
            var prefix = names == "numeric" || names == "mixed" && index != 2 ? "" : "clip";
            var clip = Path.Combine(clips, names == "mixed" && index == 2 ? "2_clip.mp4" : $"{prefix}{index}.mp4");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", $"color=c={color}:s=160x90:r=25:d=1",
                "-c:v", "libx264", "-preset", "ultrafast", "-bf", "0", clip);
            expected.AddRange(await FrameHashes(clip));
        }
        var originals = Directory.GetFiles(clips).ToDictionary(path => path, File.ReadAllBytes);
        var output = Path.Combine(directory, "merged.mp4");
        Assert.Equal(0, await Invoke("merge", option, clips, "-o", output));
        await AssertVideo(output, 3, 75);
        Assert.Equal(expected, await FrameHashes(output));
        foreach (var (path, bytes) in originals)
            Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    [Fact]
    public async Task MuxDiscoversMediaAndSubtitlesAndKeepsDirectoryMetadata()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var clips = Directory.CreateDirectory(Path.Combine(directory, "tracks")).FullName;
        var audio = Path.Combine(clips, "1.m4a");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=1", "-c:a", "aac", audio);
        var subtitle = Path.Combine(clips, "subtitle.srt");
        File.WriteAllText(subtitle, "1\n00:00:00,100 --> 00:00:00,800\nHello\n\n");
        File.WriteAllText(Path.Combine(clips, "notes.txt"), "ignore");
        var output = Path.Combine(directory, "muxed.mkv");
        var escaped = clips.Replace(":", "\\:");
        Assert.Equal(0, await Invoke("mux", "-i", $"path={escaped}:lang=eng:name=English", "-o", output));
        using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries",
            "stream=codec_type:stream_tags=language,title", "-of", "json", output));
        var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Equal(new[] { "audio", "subtitle" }, streams.Select(stream => stream.GetProperty("codec_type").GetString()));
        Assert.All(streams, stream =>
        {
            Assert.Equal("eng", stream.GetProperty("tags").GetProperty("language").GetString());
            Assert.Equal("English", stream.GetProperty("tags").GetProperty("title").GetString());
        });
        Assert.True(File.Exists(audio));
        Assert.Contains("Hello", File.ReadAllText(subtitle));
        Assert.Equal(0, await Invoke("mux", "--input-dir", clips, "--pattern", "*.m4a", "-o", output,
            "--overwrite", "--dry-run"));
    }

    [Fact]
    public async Task PreviewExplainsEmptyDirectoriesAndHonorsConfiguredMergeMode()
    {
        var clips = Directory.CreateDirectory(Path.Combine(directory, "clips")).FullName;
        var output = Path.Combine(directory, "out", "merged.mp4");
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        try
        {
            Console.SetError(stderr);
            Assert.Equal(1, await Invoke("merge", "--input-dir", clips, "--pattern", "*.mp4", "-o", output, "--dry-run"));
            Assert.Contains(string.Format(ResString.toolsDirectoryEmpty, Path.GetFullPath(clips), "*.mp4"), stderr.ToString());
            File.WriteAllBytes(Path.Combine(clips, "clip.mp4"), [1]);
            Assert.Equal(1, await Invoke("merge", "-i", Path.Combine(clips, "clip.mp4"), "--pattern", "*.mp4", "-o", output));
            Assert.Contains(ResString.toolsPatternRequiresDirectory, stderr.ToString());
            var config = new ConfigFile(["merge", "-i", clips, "-o", output, "--dry-run"],
                ["--ffmpeg-concat-mode", "protocol"]);
            var command = CommandInvoker.CreateRootCommand();
            Assert.Equal(0, await command.Parse(config.Merge(command)).InvokeAsync());
            Assert.Contains(string.Format(ResString.toolsMergeMode, "PROTOCOL"), stderr.ToString());
            stderr.GetStringBuilder().Clear();
            Assert.Equal(0, await Invoke("merge", "-i", clips, "-o", output, "--dry-run"));
            Assert.Contains(string.Format(ResString.toolsMergeMode, "DEMUXER"), stderr.ToString());
        }
        finally { Console.SetError(originalError); }
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
    }

    [Fact]
    public async Task TaskDirectoriesRequireMuxAndPatternsCannotOverwriteSources()
    {
        var task = Directory.CreateDirectory(Path.Combine(directory, "task")).FullName;
        foreach (var track in new[] { "0_video", "1_audio" })
        {
            var path = Directory.CreateDirectory(Path.Combine(task, track)).FullName;
            File.WriteAllBytes(Path.Combine(path, "1.ts"), [1]);
        }
        var output = Path.Combine(directory, "out", "output.mp4");
        Assert.Equal(1, await Invoke("merge", "-i", task, "-o", output, "--dry-run"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
        Assert.Equal(0, await Invoke("mux", "--input-dir", task, "-o", output, "--dry-run"));
        var source = Path.Combine(task, "clip.mp4");
        File.WriteAllBytes(source, [1, 2]);
        var originalError = Console.Error;
        using var stderr = new StringWriter();
        try
        {
            Console.SetError(stderr);
            Assert.Equal(1, await Invoke("concat", "-i", task, "--pattern", "*.mp4", "-o", source, "--overwrite"));
            Assert.Contains(string.Format(ResString.toolsOutputIsInput, source, source, Environment.NewLine), stderr.ToString());
        }
        finally { Console.SetError(originalError); }
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(source));
    }

    private static Task<int> Invoke(params string[] args) => CommandInvoker.CreateRootCommand().Parse(args).InvokeAsync();
    public void Dispose() => Directory.Delete(directory, true);
}
