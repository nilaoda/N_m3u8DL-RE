using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using System.Diagnostics;
using System.Globalization;

namespace N_m3u8DL_RE.Util;

internal sealed record MediaProgress(double? Fraction = null, long? Bytes = null, long? TotalBytes = null, double? Seconds = null);

// 处理进度与下载计数独立，同一轨道切换阶段时不改动累计分片数或网络速度统计。
internal sealed class MediaProcessingProgress(ProgressTask task) : IDisposable
{
    private volatile bool active;
    private readonly string description = task.Description;
    private readonly bool plainOutput = IsPlainOutput();
    private readonly Stopwatch elapsed = new();
    private MediaProgress progress = new();
    private Timer? logTimer;
    private string stage = "";
    public ProgressTask DisplayTask { get; private set; } = null!;
    public MediaProgress Progress => Volatile.Read(ref progress);
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    public TimeSpan Elapsed => elapsed.Elapsed;
    public string? Part { get; set; }

    // 主命令可能强制启用交互能力；纯文本和重定向输出仍应使用日志，避免重复绘制进度行。
    private static bool IsPlainOutput() => !CustomAnsiConsole.Console.Profile.Capabilities.Interactive ||
        !CustomAnsiConsole.Console.Profile.Capabilities.Ansi || CustomAnsiConsole.Console.Profile.Out.Writer is NonAnsiWriter ||
        Console.IsOutputRedirected || Console.IsErrorRedirected;

    public static MediaProcessingProgress? Get(ProgressTask task) => task.Tag is MediaProcessingProgress { active: true } progress ? progress : null;

    public void Begin(string name, bool logStart = true)
    {
        Dispose();
        stage = name;
        Finished = Failed = false;
        Volatile.Write(ref progress, new());
        elapsed.Restart();
        logTimer = new Timer(_ => LogProgress(), null, 10000, 10000);
        // 只用于渲染，不能加入 context，否则会多出一行常驻任务。
        DisplayTask = new ProgressTask(task.Id, description, 100) { IsIndeterminate = true };
        task.Tag = this;
        active = true;
        task.Description = $"{description} [cyan]{Part}{name.EscapeMarkup()}[/]";
        // 短暂的准备/保存阶段只更新进度行；已有开始日志的调用方也不重复打印。
        if (logStart)
            WriteLog($"{description.RemoveMarkup()} {Part}{name}",
                plainOutput && name != ResString.processingPreparing && name != ResString.processingFinishing);
    }

    public void Download()
    {
        Dispose();
        active = false;
        task.Description = Part == null ? description : $"{description} [cyan]{Part}{ResString.processingDownload.EscapeMarkup()}[/]";
    }

    public void Report(MediaProgress value)
    {
        Volatile.Write(ref progress, value);
        DisplayTask.IsIndeterminate = value.Fraction == null;
        if (value.Fraction is { } fraction)
            // 工具汇报结束后仍需等待退出码、文件提交和清理，不能提前显示成功。
            DisplayTask.Value = Math.Clamp(fraction * 100, 0, 99.99);
    }

    public void Complete(bool success, bool skippedMerge = false, long? downloadedBytes = null, bool logCompletion = false)
    {
        if (Get(task) != this)
        {
            DisplayTask = new ProgressTask(task.Id, description, 100);
            task.Tag = this;
            active = true;
        }
        Volatile.Write(ref progress, Progress with { Bytes = Progress.Bytes ?? downloadedBytes });
        Finished = true;
        Failed = !success;
        elapsed.Stop();
        Dispose();
        DisplayTask.IsIndeterminate = false;
        if (success)
            DisplayTask.Value = 100;
        else
            DisplayTask.StopTask();
        var status = success ? skippedMerge ? ResString.processingDownloaded : ResString.toolsCompleted : ResString.processingFailed;
        task.Description = $"{description} [{(success ? "green" : "red")}]{status.EscapeMarkup()}[/]";
        if (!success)
            Logger.Error($"{description.RemoveMarkup()} {status}");
        else if (plainOutput || logCompletion)
            Logger.InfoMarkUp($"[green]{description} {status.EscapeMarkup()}[/]");
        else
            WriteLog($"{description.RemoveMarkup()} {status}", display: false);
    }

    private void LogProgress()
    {
        if (!Finished && Get(task) == this)
            WriteLog($"{description.RemoveMarkup()} {Part}{stage}: {Details(Progress)} ({string.Format(ResString.processingElapsed, Elapsed.ToString(@"hh\:mm\:ss"))})", plainOutput);
    }

    private static void WriteLog(string message, bool display)
    {
        if (Logger.LogLevel < LogLevel.INFO)
            return;
        if (display) Logger.Info(message);
        else Logger.Extra(message);
    }

    public void Dispose()
    {
        logTimer?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        logTimer = null;
    }

    internal static string Details(MediaProgress value)
    {
        var percent = value.Fraction is { } fraction ? $"{Math.Clamp(fraction * 100, 0, 99.99):F2}% " : "";
        var size = value.Bytes is { } bytes ? GlobalUtil.FormatFileSize(bytes) +
            (value.TotalBytes is { } total ? "/" + GlobalUtil.FormatFileSize(total) : "") : "";
        var time = value.Seconds is { } seconds
            ? string.Format(ResString.processingMediaTime, TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss")) : "";
        return (percent + size + " " + time).Trim();
    }

    internal static async Task<bool> RunAsync(string description, Func<MediaProcessingProgress, Task<bool>> action)
    {
        async Task<bool> Run(ProgressTask task)
        {
            using var progress = new MediaProcessingProgress(task);
            progress.Begin(ResString.processingPreparing);
            try
            {
                var success = await action(progress);
                progress.Complete(success, logCompletion: true);
                return success;
            }
            catch
            {
                progress.Complete(false);
                throw;
            }
        }
        var console = CustomAnsiConsole.Console;
        if (IsPlainOutput() || Logger.LogLevel == LogLevel.OFF)
            return await Run(new ProgressTask(0, description, 100));
        return await console.Progress().AutoClear(true).Columns(
            new TaskDescriptionColumn(),
            new MediaProcessingColumn(new ProgressBarColumn { Width = 30 }),
            new MediaProcessingColumn(new PercentageColumn()),
            new MediaProcessingColumn(new DownloadedColumn()),
            new MediaProcessingColumn(new TransferSpeedColumn()),
            new MediaProcessingColumn(new RemainingTimeColumn()),
            new MediaProcessingColumn(new SpinnerColumn()))
            .StartAsync(ctx => Run(ctx.AddTask(description)));
    }
}

// FFmpeg 的 stdout 是 key=value；mkvmerge 的 GUI 消息固定使用英文，与系统语言无关。
internal sealed class MediaToolProgress(Action<MediaProgress> report, double? duration = null, bool preserveTimestamp = false)
{
    private long? bytes;
    private double? seconds;

    public void ReadFFmpeg(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0)
            return;
        var value = line[(separator + 1)..];
        switch (line[..separator])
        {
            case "total_size":
                if (long.TryParse(value, CultureInfo.InvariantCulture, out var size) && size >= 0)
                    bytes = size;
                break;
            case "out_time_us":
                if (double.TryParse(value, CultureInfo.InvariantCulture, out var microseconds) && double.IsFinite(microseconds) && microseconds >= 0)
                    seconds = microseconds / 1000000 <= TimeSpan.MaxValue.TotalSeconds - 1 ? microseconds / 1000000 : null;
                else
                    seconds = null;
                break;
            case "progress":
                // copyts 等模式可能输出源时间戳，不能把绝对时钟当成已处理时长。
                var reliableTime = (value is "continue" or "end") && !preserveTimestamp && seconds != null &&
                    (duration == null || duration > 0 && seconds <= duration + 1);
                var fraction = reliableTime && duration > 0 ? seconds / duration : null;
                report(new(fraction, bytes, Seconds: reliableTime ? seconds : null));
                // 下次汇报缺少时间字段时，不能继续沿用上一轮的百分比。
                seconds = null;
                break;
        }
    }

    public bool ReadMkvmerge(string line)
    {
        const string prefix = "#GUI#progress";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        var value = line[prefix.Length..];
        // 无效汇报切换为不定进度，不能停留在上一次的百分比。
        var valid = double.TryParse(value.TrimEnd('%'), CultureInfo.InvariantCulture, out var percent) &&
            double.IsFinite(percent) && percent is >= 0 and <= 100;
        report(new(valid ? percent / 100 : null));
        return true;
    }
}
