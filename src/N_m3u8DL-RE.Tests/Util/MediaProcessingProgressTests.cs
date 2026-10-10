using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

[Collection("Download console")]
public class MediaProcessingProgressTests
{
    [Theory]
    [InlineData(10d, "5000000", 0.5)]
    [InlineData(10d, "2783012232000", null)]
    [InlineData(0d, "5000000", null)]
    [InlineData(null, "5000000", null)]
    public void FfmpegUsesMediaTimeOnlyWhenTheDurationIsReliable(double? duration, string timestamp, double? expected)
    {
        MediaProgress? result = null;
        var reader = new MediaToolProgress(value => result = value, duration);
        reader.ReadFFmpeg("total_size=1024");
        reader.ReadFFmpeg("out_time_us=" + timestamp);
        reader.ReadFFmpeg("progress=continue");
        Assert.NotNull(result);
        Assert.Equal(expected, result.Fraction);
        Assert.Equal(1024, result.Bytes);
        reader.ReadFFmpeg("progress=continue");
        Assert.Null(result.Fraction);
        Assert.Null(result.Seconds);
        foreach (var invalid in new[] { "N/A", "NaN", "Infinity", "-1", "1e100" })
        {
            reader.ReadFFmpeg("out_time_us=" + invalid);
            reader.ReadFFmpeg("progress=end");
            Assert.Null(result.Fraction);
            Assert.Null(result.Seconds);
        }
        reader.ReadFFmpeg("out_time_us=5000000");
        reader.ReadFFmpeg("progress=unexpected");
        Assert.Null(result.Fraction);
        Assert.Null(result.Seconds);
        reader.ReadFFmpeg("out_time_us=" + timestamp);
        reader.ReadFFmpeg("progress=continue");
        Assert.Equal(expected, result.Fraction);
        reader = new MediaToolProgress(value => result = value, duration: 10, preserveTimestamp: true);
        reader.ReadFFmpeg("out_time_us=2783012232000");
        reader.ReadFFmpeg("progress=end");
        Assert.Null(result.Fraction);
        Assert.Null(result.Seconds);
    }

    [Fact]
    public void MkvmergeParsesGuiProgressAndIgnoresOtherMessages()
    {
        var updates = new List<MediaProgress>();
        var reader = new MediaToolProgress(updates.Add);
        Assert.False(reader.ReadMkvmerge("Progress: 50%"));
        Assert.False(reader.ReadMkvmerge("#GUI#warning warning"));
        Assert.True(reader.ReadMkvmerge("#GUI#progress 50%"));
        Assert.Equal(0.5, Assert.Single(updates).Fraction);
        foreach (var invalid in new[] { "101%", "N/A", "NaN", "Infinity", "-1%", "" })
        {
            Assert.True(reader.ReadMkvmerge("#GUI#progress " + invalid));
            Assert.Null(updates[^1].Fraction);
        }
        Assert.True(reader.ReadMkvmerge("#GUI#progress"));
        Assert.Null(updates[^1].Fraction);
        Assert.True(reader.ReadMkvmerge("#GUI#progress=unknown"));
        Assert.Null(updates[^1].Fraction);
        Assert.True(reader.ReadMkvmerge("#GUI#progress 100%"));
        Assert.Equal(1, updates[^1].Fraction);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessingKeepsDownloadCountersAndDoesNotCompleteBeforePublication(bool success)
    {
        var task = new ProgressTask(0, "video", 10) { Value = 5 };
        var speed = new SpeedContainer { SingleSegment = true, ResponseLength = 100000 };
        speed.Add(100000);
        var column = new MediaProcessingColumn(new DownloadSpeedColumn(new ConcurrentDictionary<int, SpeedContainer>(
            [new KeyValuePair<int, SpeedContainer>(task.Id, speed)])));
        using var processing = new MediaProcessingProgress(task);
        processing.Begin(ResString.processingMerge);
        processing.Report(new(1, 100000, 100000));
        column.Render(new RenderOptions(AnsiConsole.Console.Profile.Capabilities, new Size(100, 30)), task, TimeSpan.Zero);
        Assert.Equal(5, task.Value);
        Assert.Equal(10, task.MaxValue);
        Assert.False(processing.DisplayTask.IsFinished);
        processing.Part = "(2/2) ";
        processing.Download();
        Assert.Null(MediaProcessingProgress.Get(task));
        Assert.Contains("(2/2)", task.Description);
        task.Increment(1);
        Assert.Equal(6, task.Value);
        processing.Begin(ResString.processingDecrypt);
        Assert.True(processing.DisplayTask.IsIndeterminate);
        processing.Complete(success);
        Assert.Equal(!success, processing.Failed);
        Assert.Equal(success ? 100 : 0, processing.DisplayTask.Value);
        Assert.Equal(6, task.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProcessingPercentageFitsOnOneLine(bool downloadPercentage)
    {
        var task = new ProgressTask(0, "video", 100);
        var column = new MediaProcessingColumn(downloadPercentage ? new MyPercentageColumn() : new PercentageColumn());
        var options = new RenderOptions(AnsiConsole.Console.Profile.Capabilities, new Size(80, 25));
        using var processing = new MediaProcessingProgress(task);
        processing.Begin(ResString.processingMux);
        processing.Report(new(0.713));
        void AssertPercentage(string expected)
        {
            var renderable = column.Render(options, task, TimeSpan.Zero);
            var width = column.GetColumnWidth(options) ?? renderable.Measure(options, 80).Max;
            var segments = renderable.Render(options, width).ToArray();
            Assert.DoesNotContain(segments, segment => segment.IsLineBreak);
            Assert.Equal(expected, string.Concat(segments.Select(segment => segment.Text)).Trim());
        }
        AssertPercentage("71.30%");
        processing.Report(new());
        Assert.True(processing.DisplayTask.IsIndeterminate);
        AssertPercentage("-");
        processing.Complete(true);
        AssertPercentage("100.00%");
    }

    [Fact]
    public async Task PlainOutputUsesLogsEvenWhenTheConsoleIsMarkedInteractive()
    {
        var originalConsole = CustomAnsiConsole.Console;
        using var output = new StringWriter();
        try
        {
            CustomAnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Yes,
                Interactive = InteractionSupport.Yes,
                Out = new AnsiConsoleOutput(new NonAnsiWriter(output))
            });
            CustomAnsiConsole.Console.Profile.Width = 80;
            CustomAnsiConsole.Console.Profile.Height = 25;
            Assert.True(await MediaProcessingProgress.RunAsync("mux", processing =>
            {
                processing.Begin(ResString.processingMux);
                processing.Report(new(0.713, 1024));
                return Task.FromResult(true);
            }));
            var text = output.ToString();
            Assert.Contains(ResString.processingMux, text);
            Assert.Contains(ResString.toolsCompleted, text);
            Assert.DoesNotContain('━', text);
            Assert.DoesNotContain('\u001b', text);
        }
        finally { CustomAnsiConsole.Console = originalConsole; }
    }

    [Fact]
    public async Task BinaryMergeReportsInsideSingleFilesAndHonorsCancellation()
    {
        var root = Directory.CreateTempSubdirectory("re-copy-progress-").FullName;
        try
        {
            var input = Path.Combine(root, "input.bin");
            var output = Path.Combine(root, "output.bin");
            var data = new byte[1024 * 1024];
            Random.Shared.NextBytes(data);
            await File.WriteAllBytesAsync(input, data);
            var updates = new List<MediaProgress>();
            await MergeUtil.CombineMultipleFilesIntoSingleFileAsync([input, "", input], output, progress: updates.Add);
            Assert.Equal(data.Concat(data), await File.ReadAllBytesAsync(output));
            Assert.Contains(updates, update => update.Bytes > 0 && update.Bytes < data.Length);
            Assert.Equal(2 * data.Length, updates[^1].Bytes);
            Assert.Equal(1, updates[^1].Fraction);
            Assert.Equal(updates.Select(value => value.Bytes).Order(), updates.Select(value => value.Bytes));
            using var cancel = new CancellationTokenSource();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MergeUtil.CombineMultipleFilesIntoSingleFileAsync(
                [input], output, token: cancel.Token, progress: value => { if (value.Bytes > 0) cancel.Cancel(); }));
            Assert.Equal(data, await File.ReadAllBytesAsync(input));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SkippedMergeAndFilteredTracksDoNotStartMuxing(bool skipMerge)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("re-progress-skip-").FullName;
        var originalConsole = CustomAnsiConsole.Console;
        using var log = new StringWriter();
        try
        {
            CustomAnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(log) });
            var source = Path.Combine(root, "subs.m3u8");
            await File.WriteAllTextAsync(Path.Combine(root, "sub.vtt"), "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nHello\n");
            await File.WriteAllTextAsync(source, "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nsub.vtt\n#EXT-X-ENDLIST\n");
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(source);
            var streams = await extractor.ExtractStreamsAsync();
            streams[0].MediaType = MediaType.SUBTITLES;
            var options = CreateOptions(root);
            options.SkipMerge = skipMerge;
            options.SubtitleFormat = SubtitleFormat.SRT;
            options.MuxAfterDone = true;
            options.MuxOptions = new MuxOptions { SkipSubtitle = true };
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { MyOptions = options, DirPrefix = Path.Combine(root, "tmp") }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            Assert.DoesNotContain("MUX", string.Join("\n", Directory.GetFiles(root, "*", SearchOption.AllDirectories)));
            if (skipMerge)
            {
                Assert.Contains(ResString.processingDownloaded, log.ToString());
                Assert.Empty(Directory.GetFiles(options.SaveDir!));
            }
            else
            {
                Assert.Contains(ResString.processingMuxNoInputs, log.ToString());
                Assert.EndsWith(".srt", Assert.Single(Directory.GetFiles(options.SaveDir!)));
            }
        }
        finally
        {
            CustomAnsiConsole.Console = originalConsole;
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealMuxersReportProgressAndProducePlayableOutput(bool mkvmerge)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || mkvmerge && !HasTool("mkvmerge"))
            return;
        var root = Directory.CreateTempSubdirectory("re-mux-progress-").FullName;
        try
        {
            var input = Path.Combine(root, "input.mp4");
            var output = Path.Combine(root, mkvmerge ? "output.mkv" : "output.mp4");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "2", "-c:v", "libx264", "-bf", "0", input);
            var updates = new List<MediaProgress>();
            var exit = await MergeUtil.MuxInputsAsync(mkvmerge ? "mkvmerge" : "ffmpeg",
                [new OutputFile { Index = 0, FilePath = input }], output, useMkvmerge: mkvmerge, progress: updates.Add);
            Assert.Equal(0, exit);
            Assert.NotEmpty(updates);
            Assert.Contains(updates, value => value.Fraction > 0);
            await AssertVideo(output, 2, 50);
            if (!mkvmerge)
            {
                updates.Clear();
                Assert.Equal(0, await MergeUtil.MergeByFFmpegAsync("ffmpeg", [input], Path.Combine(root, "merged.mp4"),
                    FFmpegConcatMode.LOCAL_HTTP, progress: updates.Add, duration: 2));
                Assert.Contains(updates, value => value.Fraction > 0);
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
