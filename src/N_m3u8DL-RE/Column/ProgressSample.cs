using Spectre.Console;

namespace N_m3u8DL_RE.Column;

// 只限制显示采样，不影响下载端的实时计数；每个任务独立计时。
internal sealed class ProgressSample<T>(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<int, (long Timestamp, T Value)> _samples = new();

    public T GetValue(ProgressTask task, Func<TimeSpan, T> sample)
    {
        var now = _timeProvider.GetTimestamp();
        var exists = _samples.TryGetValue(task.Id, out var previous);
        var elapsed = exists ? _timeProvider.GetElapsedTime(previous.Timestamp, now) : TimeSpan.Zero;
        // 不足一秒也要呈现最终值，包括未知总长度或提前停止的任务。
        if (!exists || task.IsFinished || task.StopTime != null || elapsed >= TimeSpan.FromSeconds(1))
        {
            var value = sample(elapsed);
            _samples[task.Id] = (now, value);
            return value;
        }
        return previous.Value;
    }
}
