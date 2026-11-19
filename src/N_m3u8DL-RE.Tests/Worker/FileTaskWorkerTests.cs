using System.Text.Json;
using N_m3u8DL_RE.ConsoleUI;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Worker;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Worker;

[Collection("Download console")]
public sealed class FileTaskWorkerTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("worker-files-").FullName;

    [Fact]
    public async Task ConcatSupportsDirectoryPatternsAndProducesCleanProtocol()
    {
        var inputs = Directory.CreateDirectory(Path.Combine(root, "inputs")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(inputs, "10.bin"), [3, 4]);
        await File.WriteAllBytesAsync(Path.Combine(inputs, "2.bin"), [1, 2]);
        await File.WriteAllTextAsync(Path.Combine(inputs, "ignored.txt"), "ignore");
        var output = Path.Combine(root, "--config.bin");
        await using var worker = new StdioWorkerClient();
        var ready = await worker.Read("ready");
        Assert.Contains(ready.GetProperty("capabilities").EnumerateArray(), value => value.GetString() == "concat");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1, operation = "concat",
            fileRequest = new { inputs = new[] { new { path = inputs } }, output, pattern = "*.bin" } });
        var result = (await worker.Read("result")).GetProperty("result");
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Equal(output, Assert.Single(result.GetProperty("outputs").EnumerateArray()).GetProperty("path").GetString());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(output));
        Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "progress");
        await worker.Exit(0);
        Assert.Empty(await worker.Diagnostics);
    }

    [Fact]
    public async Task InvalidStartRequestsDoNotConsumeTheWorkerTask()
    {
        var source = Path.Combine(root, "input.bin");
        await File.WriteAllBytesAsync(source, [1, 2]);
        var output = Path.Combine(root, "output.bin");
        var fileRequest = new { inputs = new[] { new { path = source } }, output };
        await using var worker = new StdioWorkerClient();
        await worker.Read("ready");
        object[] commands =
        [
            new { type = "start", id = "doctor", protocolVersion = 1, operation = "doctor", fileRequest },
            new { type = "start", id = "missing", protocolVersion = 1, operation = "concat" },
            new { type = "start", id = "both", protocolVersion = 1, operation = "concat", fileRequest,
                request = new { input = "https://example.test/video.m3u8" } },
            new { type = "start", id = "empty", protocolVersion = 1, operation = "concat",
                fileRequest = new { inputs = Array.Empty<object>(), output } }
        ];
        foreach (var command in commands)
        {
            await worker.Send(command);
            Assert.Equal("invalid_command", (await worker.Read("error")).GetProperty("error").GetProperty("code").GetString());
        }
        await worker.Send(new { type = "start", id = "valid", protocolVersion = 1, operation = "concat", fileRequest });
        Assert.Equal("succeeded", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
        await worker.Exit(0);
    }

    [Fact]
    public async Task PreviewDoesNotPublishArtifactsOrModifyExistingFiles()
    {
        var source = Path.Combine(root, "input.bin");
        var output = Path.Combine(root, "output.bin");
        await File.WriteAllBytesAsync(source, [1, 2]);
        await File.WriteAllBytesAsync(output, [9]);
        await using var worker = new StdioWorkerClient();
        await worker.Read("ready");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1, operation = "concat",
            fileRequest = new { inputs = new[] { new { path = source } }, output, overwrite = true, dryRun = true } });
        var result = (await worker.Read("result")).GetProperty("result");
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.Empty(result.GetProperty("outputs").EnumerateArray());
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(output));
        Assert.Equal(new byte[] { 1, 2 }, await File.ReadAllBytesAsync(source));
        Assert.Empty(Directory.GetFiles(root, ".re-*"));
        await worker.Exit(0);
    }

    [Fact]
    public async Task MergeUsesFfmpegAndProducesValidMedia()
    {
        if (!HasTool("ffmpeg"))
            return;
        var source = await Video();
        var output = Path.Combine(root, "merged.mp4");
        await using var worker = new StdioWorkerClient();
        await worker.Read("ready");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1, operation = "merge",
            fileRequest = new { inputs = new[] { new { path = source }, new { path = source } }, output, concatMode = "DEMUXER" } });
        Assert.Equal("succeeded", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
        await worker.Exit(0);
        await AssertVideo(output, 4, 100);
        Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "state" &&
            message.GetProperty("phase").GetString() == "merging");
        Assert.Empty(await worker.Diagnostics);
    }

    [Theory]
    [InlineData("ffmpeg")]
    [InlineData("mkvmerge")]
    public async Task MuxPreservesStructuredTrackMetadata(string muxer)
    {
        if (!HasTool("ffmpeg") || muxer == "mkvmerge" && !OnPath("mkvmerge"))
            return;
        var source = await Video();
        var subtitle = Path.Combine(root, "english.srt");
        await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
        var output = Path.Combine(root, "muxed.mkv");
        await using var worker = new StdioWorkerClient();
        await worker.Read("ready");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1, operation = "mux",
            fileRequest = new { inputs = new[] { new { path = source, language = (string?)null, name = (string?)null },
                new { path = subtitle, language = (string?)"en", name = (string?)"English subtitles" } },
                output, muxer, title = "Worker title" } });
        Assert.Equal("succeeded", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
        await worker.Exit(0);
        using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", output));
        var subtitles = Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray(),
            stream => stream.GetProperty("codec_type").GetString() == "subtitle");
        Assert.Equal("eng", subtitles.GetProperty("tags").GetProperty("language").GetString());
        Assert.Equal("English subtitles", subtitles.GetProperty("tags").GetProperty("title").GetString());
        Assert.Equal("Worker title", probe.RootElement.GetProperty("format").GetProperty("tags").GetProperty("title").GetString());
        Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "progress" &&
            message.GetProperty("progress").GetProperty("phase").GetString() == "muxing");
        Assert.Empty(await worker.Diagnostics);
    }

    [Fact]
    public async Task ToolFailureReturnsLogsAndPreservesTheExistingOutput()
    {
        if (!HasTool("ffmpeg"))
            return;
        var source = Path.Combine(root, "invalid.ts");
        var output = Path.Combine(root, "output.ts");
        await File.WriteAllTextAsync(source, "invalid media");
        await File.WriteAllBytesAsync(output, [9]);
        await using var worker = new StdioWorkerClient();
        await worker.Read("ready");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1, operation = "merge",
            fileRequest = new { inputs = new[] { new { path = source } }, output, overwrite = true } });
        var result = (await worker.Read("result")).GetProperty("result");
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.NotEmpty(result.GetProperty("error").GetProperty("message").GetString()!);
        Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "log" &&
            message.GetProperty("level").GetString() == "warn");
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(root, ".re-*"));
        await worker.Exit(1);
        Assert.Empty(await worker.Diagnostics);
    }

    [Fact]
    public async Task CancellationDuringCopyPreservesOutputAndRestoresAmbientContext()
    {
        var source = Path.Combine(root, "input.bin");
        var output = Path.Combine(root, "output.bin");
        await File.WriteAllBytesAsync(source, new byte[1024 * 1024]);
        await File.WriteAllBytesAsync(output, [9]);
        using var control = new TaskControl();
        var observer = new CancelOnProgress(control);
        var context = new TaskExecutionContext(observer, new ConsoleUserInteraction(), control, isWorker: true);
        var previous = TaskExecutionContext.Current;
        var result = await FileTaskRunner.RunAsync(new FileTaskOptions
        {
            Operation = "concat", Inputs = [new OutputFile { Index = 0, FilePath = source }], Output = output, Overwrite = true
        }, context);
        Assert.Equal("cancelled", result.Status);
        Assert.Same(previous, TaskExecutionContext.Current);
        Assert.Empty(result.Outputs);
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(output));
        Assert.Empty(Directory.GetFiles(root, ".re-*"));
    }

    [Theory]
    [InlineData("concat", "muxer")]
    [InlineData("mux", "concatMode")]
    [InlineData("merge", "concatMode")]
    public void RequestRejectsIrrelevantOrInvalidOptions(string operation, string field)
    {
        var request = new FileTaskRequest
        {
            Inputs = [new FileTaskInput { Path = "input.ts" }], Output = "output.mp4",
            Muxer = field == "muxer" ? "ffmpeg" : null, ConcatMode = field == "concatMode" ? "invalid" : null
        };
        Assert.Throws<ArgumentException>(() => ProtocolMapper.ToFileOptions(operation, request));
    }

    private async Task<string> Video()
    {
        var path = Path.Combine(root, "video.mp4");
        await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
            "-t", "2", "-c:v", "libx264", "-bf", "0", path);
        return path;
    }

    private sealed class CancelOnProgress(TaskControl control) : ITaskObserver
    {
        public void State(string status, string phase) { }
        public void Tracks(TrackInfo[] tracks) { }
        public void Progress(TrackSnapshot progress)
        {
            if (progress.ProcessedBytes > 0)
                control.Cancel();
        }
    }

    public void Dispose() => Directory.Delete(root, recursive: true);
}
