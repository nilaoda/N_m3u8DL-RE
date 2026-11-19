using System.Globalization;
using System.Collections.Concurrent;
using System.Diagnostics;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Runtime;

// 现有管理器暂时保留 ProgressTask 计数模型；Worker 使用脱离终端的对象，
// 只有 CLI 路径进入 Spectre 渲染循环。
internal sealed class TaskProgressContext(ProgressContext? console)
{
    internal ConcurrentDictionary<int, ProgressTask> Tasks { get; } = new();
    public ProgressTask AddTask(string description, bool autoStart = true, double maxValue = 100)
    {
        var task = console?.AddTask(description, autoStart, maxValue) ?? new ProgressTask(Tasks.Count, description, maxValue);
        if (console == null && autoStart)
            task.StartTask();
        Tasks[task.Id] = task;
        return task;
    }
}

internal static class ProgressSession
{
    internal static async Task RunAsync(Progress display, ConcurrentDictionary<int, SpeedContainer> speeds,
        Func<TaskProgressContext, Task> action, ConcurrentDictionary<int, TimeSpan>? durations = null)
    {
        var execution = TaskExecutionContext.Current;
        async Task Run(ProgressContext? console)
        {
            var context = new TaskProgressContext(console);
            var previous = Stopwatch.GetTimestamp();
            void Sample()
            {
                var now = Stopwatch.GetTimestamp();
                var elapsed = Stopwatch.GetElapsedTime(previous, now);
                previous = now;
                foreach (var (id, task) in context.Tasks)
                {
                    if (!speeds.TryGetValue(id, out var speed))
                        continue;
                    var processing = MediaProcessingProgress.Get(task);
                    var downloading = task.IsStarted && task.StopTime == null && processing == null;
                    // 即使关闭日志也要采样，限速和零速检测不能依赖终端是否渲染。
                    speed.Sample(elapsed, downloading && !task.IsFinished);
                    if (execution?.IsWorker != true || !task.IsStarted)
                        continue;
                    var taskValue = task.Value;
                    var taskMax = task.MaxValue;
                    var downloaded = speed.RDownloaded;
                    if (downloading && taskValue < taskMax && speed.SingleSegment && speed.ResponseLength is { } length)
                    {
                        // 估算仅用于上报，避免采样与下载完成后的计数提交互相覆盖。
                        taskMax = Math.Max(1, length);
                        taskValue = Math.Min(downloaded, Math.Floor(taskMax * 0.9999));
                    }
                    var media = processing?.Progress;
                    execution.Observer.Progress(new TrackSnapshot(id.ToString(CultureInfo.InvariantCulture),
                        processing?.Phase ?? "downloading", downloaded, speed.Received,
                        speed.SingleSegment ? speed.ResponseLength : null, downloading ? speed.NowSpeed : 0,
                        speed.SingleSegment ? null : (long)taskValue,
                        execution.IsLive || speed.SingleSegment ? null : (long)taskMax,
                        durations?.TryGetValue(id, out var duration) == true ? duration.TotalSeconds : null,
                        processing != null ? media?.Fraction : !execution.IsLive && taskMax > 0 ? taskValue / taskMax : null,
                        media?.Bytes, media?.Seconds));
                }
            }
            using var stop = new CancellationTokenSource();
            var sampling = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                try
                {
                    while (await timer.WaitForNextTickAsync(stop.Token))
                        Sample();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
            try
            {
                await action(context);
            }
            finally
            {
                stop.Cancel();
                await sampling;
                Sample();
            }
        }
        if (execution?.IsWorker == true)
            await Run(null);
        else
            await display.StartAsync(ctx => Run(ctx));
    }
}
