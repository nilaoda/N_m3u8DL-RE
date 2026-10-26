using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.DownloadManager;

internal static class LiveRequestTimeoutPolicy
{
    public static TimeSpan GetSegmentTimeout(MediaSegment segment, TimeSpan requestedTimeout)
    {
        if (!double.IsFinite(segment.Duration) || segment.Duration <= 0)
            return requestedTimeout;
        if (segment.Duration >= requestedTimeout.TotalSeconds / 2)
            return requestedTimeout;
        return TimeSpan.FromTicks(Math.Max(1, (long)(segment.Duration * 2 * TimeSpan.TicksPerSecond)));
    }

    public static (TimeSpan Request, TimeSpan Read) GetTimeouts(StreamSpec stream, MyOption options)
    {
        // 保留手动设置的轨道超时；媒体分片还须按自身片长收紧，清单刷新使用原值。
        if (options.HttpRequestTimeoutSpecified)
        {
            var timeout = TimeSpan.FromSeconds(options.HttpRequestTimeout);
            return (timeout, timeout);
        }

        // 用近期正常分片的中位数，避免很短的尾片或异常时长使超时过于激进。
        var durations = stream.Playlist?.MediaParts.SelectMany(part => part.MediaSegments)
            .Select(segment => segment.Duration).Where(duration => double.IsFinite(duration) && duration > 0)
            .TakeLast(16).Order().ToArray() ?? [];
        var seconds = durations.Length == 0 ? 5 : durations[durations.Length / 2];
        return (TimeSpan.FromSeconds(Math.Clamp(seconds, 3, 10)),
            TimeSpan.FromSeconds(Math.Clamp(seconds, 5, 15)));
    }
}
