using System.Globalization;
using System.CommandLine;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public sealed class ToolCommandTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-tools-").FullName;

    [Fact]
    public async Task RepeatedInputsKeepOrderAndPreserveSources()
    {
        var first = Write("first.ts", [1, 2]);
        var second = Write("second.ts", [3, 4]);
        var output = Path.Combine(directory, "joined.ts");
        Assert.Equal(0, await Invoke("concat", "-i", second, "-i", first, "-o", output));
        Assert.Equal(new byte[] { 3, 4, 1, 2 }, File.ReadAllBytes(output));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(first));
        Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(second));
    }

    [Fact]
    public async Task DirectoryUsesNaturalOrderAndPattern()
    {
        Write("10.ts", [10]);
        Write("2.ts", [2]);
        Write("1.ts", [1]);
        Write("other.bin", [99]);
        var output = Path.Combine(directory, "result.bin");
        Assert.Equal(0, await Invoke("concat", "--input-dir", directory, "-o", output));
        Assert.Equal(new byte[] { 1, 2, 10 }, File.ReadAllBytes(output));
    }

    [Fact]
    public async Task OutputRequiresOverwriteAndCannotBeAnInput()
    {
        var source = Write("input.ts", [1, 2]);
        var output = Write("output.ts", [9]);
        Assert.Equal(1, await Invoke("concat", "-i", source, "-o", output));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(output));
        Assert.Equal(1, await Invoke("concat", "-i", source, "-o", source, "--overwrite"));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(source));
        Assert.Equal(0, await Invoke("concat", "-i", source, "-o", output, "--overwrite"));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(output));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SymbolicLinksCannotBypassInputProtection(bool directoryLink)
    {
        var source = Write("input.ts", [1, 2]);
        var extra = Write("extra.ts", [3]);
        string alias;
        if (directoryLink)
        {
            var link = Path.Combine(directory, "linked-directory");
            Directory.CreateSymbolicLink(link, directory);
            alias = Path.Combine(link, "input.ts");
        }
        else
        {
            alias = Path.Combine(directory, "linked-input.ts");
            File.CreateSymbolicLink(alias, "input.ts");
        }
        Assert.Equal(1, await Invoke("concat", "-i", alias, "-i", extra, "-o", source, "--overwrite"));
        Assert.Equal(1, await Invoke("concat", "-i", source, "-i", extra, "-o", alias, "--overwrite"));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(source));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(alias));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(extra));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
        if (directoryLink)
        {
            var output = Path.Combine(Path.GetDirectoryName(alias)!, "new", "output.ts");
            Assert.Equal(0, await Invoke("concat", "-i", source, "-i", extra, "-o", output));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output));
        }
    }

    [Fact]
    public async Task WindowsFileAliasesCannotBypassInputProtection()
    {
        if (!OperatingSystem.IsWindows())
            return;
        var source = Write("input.ts", [1, 2]);
        var extra = Write("extra.ts", [3]);
        var link = Path.Combine(directory, "junction");
        // 目录联接无需启用开发者模式，也无需创建符号链接的权限。
        await Run("cmd.exe", "/c", "mklink", "/J", link, directory);
        var alias = Path.Combine(link, "input.ts");
        var extended = @"\\?\" + source;
        var hardLink = Path.Combine(directory, "hard-link.ts");
        await Run("cmd.exe", "/c", "mklink", "/H", hardLink, source);
        var aliases = new List<string> { alias, extended, source.ToUpperInvariant(), hardLink };
        // 本机管理共享可访问时，同时覆盖盘符路径与 UNC 路径的文件身份比较。
        var unc = @"\\localhost\" + Path.GetPathRoot(source)![0] + "$" + source[2..];
        if (File.Exists(unc))
        {
            aliases.Add(unc);
            aliases.Add(@"\\?\UNC\" + unc[2..]);
        }
        foreach (var input in aliases)
        {
            Assert.Equal(1, await Invoke("concat", "-i", input, "-i", extra, "-o", source, "--overwrite"));
            Assert.Equal(1, await Invoke("concat", "-i", source, "-i", extra, "-o", input, "--overwrite"));
        }
        var output = Path.Combine(link, "new", "output.ts");
        Assert.Equal(0, await Invoke("concat", "-i", source, "-i", extra, "-o", output));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(output));
        // 身份检查也要允许覆盖已有的其他文件，不能将普通覆盖误判为输入冲突。
        Assert.Equal(0, await Invoke("concat", "-i", source, "-i", extra, "-o", output, "--overwrite"));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(source));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(extra));
    }

    [Fact]
    public async Task DryRunDoesNotCreateOutputOrRequireFfmpeg()
    {
        var source = Write("video '中文'.ts", [1]);
        var output = Path.Combine(directory, "new", "output.mp4");
        Assert.Equal(0, await Invoke("mux", "-i", source, "-o", output,
            "--dry-run", "--ffmpeg-binary-path", "/missing/ffmpeg"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(output)));
    }

    [Fact]
    public async Task FileToolStatusUsesStderrAndPreservesPlainResultAndPaths()
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var source = Write("input [red] 中文.ts", [1, 2]);
        var output = Path.Combine(directory, "output [blue].ts");
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(0, await Invoke("concat", "-i", source, "-o", output, "--force-ansi-console", "--no-ansi-color"));
            Assert.Equal(output + Environment.NewLine, stdout.ToString());
            Assert.Contains("INFO", stderr.ToString());
            Assert.Contains(ResString.toolsCompleted, stderr.ToString());
            Assert.Contains(output, stderr.ToString());
            Assert.DoesNotContain('\u001b', stderr.ToString());
            stdout.GetStringBuilder().Clear();
            stderr.GetStringBuilder().Clear();
            Assert.Equal(0, await Invoke("concat", "-i", source, "-o", output, "--overwrite", "--dry-run", "--force-ansi-console"));
            Assert.Equal($"concat => {output}{Environment.NewLine}", stdout.ToString());
            Assert.Contains($"[0] {source}", stderr.ToString());
            Assert.Contains(ResString.toolsDryRun, stderr.ToString());
            Assert.DoesNotContain('\u001b', stderr.ToString());
            stdout.GetStringBuilder().Clear();
            stderr.GetStringBuilder().Clear();
            Assert.Equal(1, await Invoke("concat", "-i", source, "-o", output, "--no-ansi-color"));
            Assert.Empty(stdout.ToString());
            Assert.Contains("ERROR", stderr.ToString());
            Assert.Contains(ResString.toolsOutputExists, stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    [Theory]
    [InlineData("concat", "-o", "output.ts")]
    [InlineData("mux", "-o", "output.mp4")]
    [InlineData("mux", "-i", "missing.mp4", "-o", "output.mp4")]
    [InlineData("mux", "--mux-import", "path=missing.srt:lang=eng", "-o", "output.mp4")]
    [InlineData("merge", "-i", "missing.ts", "--input-dir", ".", "-o", "output.mp4")]
    public async Task InvalidRequestsFail(params string[] args) => Assert.Equal(1, await Invoke(args));

    [Fact]
    public void SharedOptionsParseConsistentlyAcrossDownloadAndTools()
    {
        foreach (var prefix in new[] { new[] { "https://example.test/live" }, ["doctor"],
            ["merge", "-i", "input.ts", "-o", "output.mp4"], ["mux", "-i", "input.mp4", "-o", "output.mkv"] })
        {
            var root = CommandInvoker.CreateRootCommand();
            var result = root.Parse([.. prefix, "--ffmpeg-binary-path", "a path/ffmpeg", "--force-ansi-console", "--no-ansi-color"]);
            Assert.Empty(result.Errors);
            Assert.Equal("a path/ffmpeg", result.GetValue(CommandInvoker.FFmpegBinaryPath));
            Assert.True(result.GetValue(CommandInvoker.ForceAnsiConsole));
            Assert.True(result.GetValue(CommandInvoker.NoAnsiColor));
            Assert.False(root.Parse(prefix).GetValue(CommandInvoker.ForceAnsiConsole));
            Assert.NotEmpty(root.Parse([.. prefix, "--ffmpeg-binary-path"]).Errors);
        }
    }

    [Fact]
    public void ToolsInheritOnlyApplicableDefaultsAndCommandLineWins()
    {
        var config = new ConfigFile(["merge", "-i", "1.ts", "-i", "2.ts", "-o", "out.mp4",
            "--ffmpeg-binary-path", "new ffmpeg", "--ffmpeg-concat-mode", "demuxer"],
            ["--ffmpeg-binary-path", "old ffmpeg", "--ffmpeg-concat-mode", "protocol", "--thread-count", "invalid",
                "--custom-hls-key", "/missing/key", "--mux-import", "/missing/import.srt", "--ui-language", "en-US", "--no-ansi-color"]);
        var root = CommandInvoker.CreateRootCommand();
        var merged = config.Merge(root);
        Assert.DoesNotContain("--thread-count", merged);
        Assert.DoesNotContain("--custom-hls-key", merged);
        Assert.DoesNotContain("--mux-import", merged);
        var result = root.Parse(merged);
        Assert.Empty(result.Errors);
        Assert.Equal("new ffmpeg", result.GetValue<string>("--ffmpeg-binary-path"));
        Assert.Equal(FFmpegConcatMode.DEMUXER, result.GetValue<FFmpegConcatMode>("--ffmpeg-concat-mode"));
        Assert.Equal("en-US", result.GetValue<string>("--ui-language"));
        Assert.True(result.GetValue<bool>("--no-ansi-color"));
        Assert.Equal(new[] { "1.ts", "2.ts" }, result.GetValue<string[]>("-i"));
    }

    [Fact]
    public void DoctorLoadsExplicitConfigAndHasNoDownloadInput()
    {
        var path = Path.Combine(directory, "config.conf");
        File.WriteAllText(path, "--ffmpeg-binary-path \"a path/ffmpeg\"\n--key-text-file /missing/key\n--no-log");
        var config = ConfigFile.Load(["doctor", "--config", path]);
        var command = CommandInvoker.CreateRootCommand();
        var result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        Assert.Equal("a path/ffmpeg", result.GetValue<string>("--ffmpeg-binary-path"));
        Assert.Equal(30, result.GetValue<int>("--tool-timeout"));
        Assert.True(ConfigFile.IsUtilityRequest(config.Arguments));
        Assert.False(ConfigFile.IsUtilityRequest(["input.m3u8"]));
    }

    [Theory]
    [InlineData("0", true)]
    [InlineData("120", true)]
    [InlineData("-1", false)]
    [InlineData("2147483647", false)]
    public void DoctorValidatesToolTimeout(string value, bool valid)
    {
        var config = new ConfigFile(["doctor", "--tool-timeout", value], ["--ffmpeg-binary-path", "ffmpeg"]);
        var root = CommandInvoker.CreateRootCommand();
        var result = root.Parse(config.Merge(root));
        Assert.Equal(valid, result.Errors.Count == 0);
        if (valid)
            Assert.Equal(int.Parse(value), result.GetValue<int>("--tool-timeout"));
    }

    [Theory]
    [InlineData("--generate-completion", "powershell")]
    [InlineData("--config", "other.conf")]
    [InlineData("--help")]
    [InlineData("doctor")]
    public void UtilityConfigurationStillRejectsActionsAndCommands(params string[] defaults)
    {
        var config = new ConfigFile(["doctor"], defaults);
        Assert.Throws<ArgumentException>(() => config.Merge(CommandInvoker.CreateRootCommand()));
    }

    [Theory]
    [InlineData("LOCAL_HTTP")]
    [InlineData("PROTOCOL")]
    [InlineData("DEMUXER")]
    public async Task MergeUsesRealFfmpegAndPreservesInputs(string mode)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var source = Path.Combine(directory, "输入 'one'.ts");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=1", "-c:a", "aac", source);
        var output = Path.Combine(directory, "merged audio.m4a");
        Assert.Equal(0, await Invoke("merge", "-i", source, "-i", source, "-o", output, "--ffmpeg-concat-mode", mode));
        var duration = await Run("ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", output);
        Assert.InRange(double.Parse(duration.Trim(), CultureInfo.InvariantCulture), 1.9, 2.2);
        Assert.True(File.Exists(source));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    [Theory]
    [InlineData("mp4", "ffmpeg")]
    [InlineData("mkv", "ffmpeg")]
    [InlineData("m4a", "ffmpeg")]
    [InlineData("mkv", "mkvmerge")]
    public async Task MuxPreservesCombinedInputAndSetsImportedTrackMetadata(string format, string muxer)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        if (muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var media = Path.Combine(directory, "video '中文'.mp4");
        var audio = Path.Combine(directory, "audio two.m4a");
        var subtitles = Path.Combine(directory, "subtitles.srt");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
            "-f", "lavfi", "-i", "sine=duration=1", "-t", "1", "-c:v", "libx264", "-preset", "ultrafast", "-c:a", "aac",
            "-metadata:s:a:0", "language=jpn", media);
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=440:duration=1", "-c:a", "aac", audio);
        File.WriteAllText(subtitles, "1\n00:00:00,100 --> 00:00:00,800\nHello\n\n");
        var output = Path.Combine(directory, "muxed." + format);
        var audioTitle = "English \"Audio\": Commentary";
        Assert.Equal(0, await Invoke("mux", "-i", media, "-i", $"path={audio}:lang=en:name=English \"Audio\"\\: Commentary",
            "--mux-import", $"path={subtitles}:lang=zh-Hans:name=简体中文", "--title", "Movie '标题'", "-o", output, "--muxer", muxer));
        using var json = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries",
            "format_tags=title:stream=codec_name,codec_type:stream_tags=language,title,handler_name", "-of", "json", output));
        var streams = json.RootElement.GetProperty("streams").EnumerateArray().ToArray();
        Assert.Equal(format == "m4a" ? new[] { "aac", "aac" } : new[] { "h264", "aac", "aac", format == "mp4" ? "mov_text" : "subrip" },
            streams.Select(stream => stream.GetProperty("codec_name").GetString()));
        var audios = streams.Where(stream => stream.GetProperty("codec_type").GetString() == "audio").ToArray();
        Assert.Equal("jpn", audios[0].GetProperty("tags").GetProperty("language").GetString());
        Assert.Equal("eng", audios[1].GetProperty("tags").GetProperty("language").GetString());
        var nameTag = format == "mkv" ? "title" : "handler_name";
        Assert.Equal(audioTitle, audios[1].GetProperty("tags").GetProperty(nameTag).GetString());
        if (format != "m4a")
        {
            Assert.Equal("chi", streams[^1].GetProperty("tags").GetProperty("language").GetString());
            Assert.Equal("简体中文", streams[^1].GetProperty("tags").GetProperty(nameTag).GetString());
        }
        Assert.Equal("Movie '标题'", json.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString());
        Assert.All(new[] { media, audio, subtitles }, path => Assert.True(File.Exists(path)));
    }

    [Fact]
    public async Task MuxAcceptsImportsAloneAndCommandLineOverridesConfiguredImports()
    {
        var configured = Write("configured.srt", [1]);
        var supplied = Write("supplied.srt", [2]);
        var command = CommandInvoker.CreateRootCommand();
        var defaults = new[] { "--mux-import", $"path={configured}:lang=eng:name=English" };
        var config = new ConfigFile(["mux", "-o", Path.Combine(directory, "out.mkv"), "--dry-run"], defaults);
        var result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        Assert.Equal(configured, Assert.Single(result.GetValue<List<OutputFile>>("--mux-import")!).FilePath);
        Assert.Equal(0, await result.InvokeAsync());
        config = new ConfigFile([.. config.Arguments, "--mux-import", supplied], defaults);
        result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        Assert.Equal(supplied, Assert.Single(result.GetValue<List<OutputFile>>("--mux-import")!).FilePath);
        Assert.Equal(0, await result.InvokeAsync());
    }

    [Fact]
    public async Task FfmpegFailurePreservesExistingOutputAndRemovesTemporaryFiles()
    {
        if (!HasTool("ffmpeg"))
            return;
        var source = Write("invalid.ts", [1, 2, 3]);
        var output = Write("old.mp4", [9, 8]);
        Assert.Equal(1, await Invoke("mux", "-i", source, "-o", output, "--overwrite"));
        Assert.Equal(new byte[] { 9, 8 }, File.ReadAllBytes(output));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(source));
        Assert.Empty(Directory.GetFiles(directory, ".re-*"));
    }

    private static async Task<int> Invoke(params string[] args) => await CommandInvoker.CreateRootCommand().Parse(args,
        new ParserConfiguration { EnablePosixBundling = false, ResponseFileTokenReplacer = null }).InvokeAsync();
    private string Write(string name, byte[] contents)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, contents);
        return path;
    }
    public void Dispose() => Directory.Delete(directory, true);
}
