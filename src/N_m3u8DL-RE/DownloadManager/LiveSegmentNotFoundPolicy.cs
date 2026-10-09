using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.DownloadManager;

internal sealed class LiveSegmentNotFoundPolicy(bool isHls)
{
    private sealed record Window(List<MediaSegment> Segments, double RefreshSeconds);
    private volatile Window? window;

    public void Update(List<MediaSegment> segments, double refreshSeconds)
    {
        // 保存过滤、入队前的完整窗口；空清单不能证明旧分片已过期。
        // 只复制匹配所需的字段，快照不持有会被刷新线程修改的原对象。
        if (segments.Count == 0)
            return;
        window = new Window(segments.Select(segment => new MediaSegment
        {
            Url = segment.Url, Index = segment.Index, DateTime = segment.DateTime,
            NameFromVar = segment.NameFromVar, StartRange = segment.StartRange, ExpectLength = segment.ExpectLength
        }).ToList(), refreshSeconds);
    }

    public TimeSpan GetPublicationWait(MediaSegment segment)
    {
        var current = window;
        if (current == null)
            return TimeSpan.Zero;
        var index = LiveSegmentTracker.FindMatchingIndex(current.Segments, segment, isHls);
        if (index < 0 || index < current.Segments.Count - 2)
            return TimeSpan.Zero;
        var duration = double.IsFinite(segment.Duration) && segment.Duration > 0 ? segment.Duration : 5;
        // 尾部 404 可能是提前推算或 CDN 尚未同步，最多等待两片时长加一次刷新。
        // 固定清单、错误 URL 和异常长分片也必须有上限，不能无限拖住后续下载。
        return TimeSpan.FromSeconds(Math.Clamp(duration * 2 + current.RefreshSeconds, 3, 30));
    }

    public bool ShouldRetry(MediaSegment segment, int failures, int retryCount, TimeSpan elapsed, TimeSpan publicationWait)
    {
        // 普通 404 仍遵守已有重试次数；尾部额外等待不受 HTTP 请求超时参数影响。
        if (failures <= retryCount)
            return true;
        // 清单已向前滑动且移除了目标，就停止额外等待。预算从首次 404 起算，不因刷新续期。
        var current = window;
        return elapsed < publicationWait && current != null &&
            LiveSegmentTracker.FindMatchingIndex(current.Segments, segment, isHls) >= 0;
    }
}
