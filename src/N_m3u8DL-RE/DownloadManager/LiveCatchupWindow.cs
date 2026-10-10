using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.DownloadManager;

internal sealed class LiveCatchupWindow(DateTimeOffset start, TimeSpan? duration)
{
    private readonly Lock lockObj = new();
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset? End { get; } = duration is { } limit && limit != TimeSpan.MaxValue ? start + limit : null;
    public DateTimeOffset? ActualStart { get; private set; }
    public DateTimeOffset? ActualEnd { get; private set; }

    public void Validate(List<StreamSpec> streams, DateTimeOffset now)
    {
        if (streams.Count == 0 || streams.Any(s => s.Playlist?.IsLive != true))
            throw new ArgumentException(ResString.liveCatchupRequireLive);
        if (duration is { } limit && limit <= TimeSpan.Zero)
            throw new ArgumentException(ResString.liveCatchupInvalid);

        // 空字幕轨道可以等待刷新；窗口边界以所选音视频为准，仅字幕时则使用字幕轨道。
        var media = streams.Where(s => s.MediaType != MediaType.SUBTITLES).ToList();
        if (media.Count == 0) media = streams;
        DateTimeOffset? earliest = null;
        foreach (var stream in streams)
        {
            var segments = TimedSegments(stream).ToList();
            if (!media.Contains(stream)) continue;
            if (segments.Count == 0)
                throw new ArgumentException(ResString.liveCatchupMissingTime);
            if (End is { } end && segments.Any(s => s.Start >= end) &&
                !segments.Any(s => s.Start + TimeSpan.FromSeconds(s.Segment.Duration) > Start && s.Start < end))
                throw new ArgumentException(ResString.liveCatchupMissingTime);
            var first = segments.Min(s => s.Start);
            // 某些 MPD 仍列出已过期的 Period，不能把这些分片当成可用回看内容。
            if (stream.Playlist!.TimeShiftBufferDepth is { } depth)
                first = new[] { first, now - depth }.Max();
            earliest = earliest == null || first > earliest ? first : earliest;
        }
        if (Start < earliest || Start > now)
            throw new ArgumentException(string.Format(ResString.liveCatchupOutsideWindow,
                Start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"),
                earliest?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"),
                now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")));
    }

    public List<MediaSegment> Filter(StreamSpec stream, out bool endReached, DateTimeOffset? now = null)
    {
        var currentTime = now ?? DateTimeOffset.UtcNow;
        // 清单可能提前列出分片，尚未完整发布时继续等刷新，不能提前触发固定终点。
        var segments = TimedSegments(stream)
            .Where(s => s.Start + TimeSpan.FromSeconds(s.Segment.Duration) <= currentTime).ToList();
        // 按源时间判断结束；不能按分片时长累加，否则边界重叠或广告空档会多录内容。
        endReached = End is { } end && segments.Any(s => s.Start + TimeSpan.FromSeconds(s.Segment.Duration) >= end);
        return segments.Where(s => s.Start + TimeSpan.FromSeconds(s.Segment.Duration) > Start &&
            (End == null || s.Start < End)).Select(s => s.Segment).ToList();
    }

    public void Record(StreamSpec stream, List<MediaSegment> segments)
    {
        // 实际范围只统计去重和去广告后入队的分片。
        var kept = new HashSet<MediaSegment>(segments, ReferenceEqualityComparer.Instance);
        var selected = TimedSegments(stream).Where(s => kept.Contains(s.Segment)).ToList();
        if (selected.Count > 0)
        {
            lock (lockObj)
            {
                var first = selected.Min(s => s.Start);
                var last = selected.Max(s => s.Start + TimeSpan.FromSeconds(s.Segment.Duration));
                ActualStart = ActualStart == null || first < ActualStart ? first : ActualStart;
                ActualEnd = ActualEnd == null || last > ActualEnd ? last : ActualEnd;
            }
        }
    }

    private static IEnumerable<(MediaSegment Segment, DateTimeOffset Start)> TimedSegments(StreamSpec stream)
    {
        var playlist = stream.Playlist!;
        foreach (var part in playlist.MediaParts)
        {
            // HLS 的 PROGRAM-DATE-TIME 可只出现在某一片上，同一不连续段内按时长向前后推算。
            var anchor = part.MediaSegments.FindIndex(s => s.DateTime != null);
            DateTimeOffset? next = anchor < 0 ? null : new DateTimeOffset(part.MediaSegments[anchor].DateTime!.Value)
                - TimeSpan.FromSeconds(part.MediaSegments.Take(anchor).Sum(s => s.Duration));
            foreach (var segment in part.MediaSegments)
            {
                DateTimeOffset? time = playlist.AvailabilityStartTime is { } origin &&
                    part.PeriodStart is { } periodStart && segment.PresentationTime is { } presentationTime
                    ? origin + TimeSpan.FromSeconds(periodStart + presentationTime - (part.PresentationTimeOffset ?? 0))
                    : segment.DateTime is { } date ? new DateTimeOffset(date) : next;
                if (time == null || !double.IsFinite(segment.Duration) || segment.Duration <= 0)
                    throw new ArgumentException(ResString.liveCatchupMissingTime);
                yield return (segment, time.Value);
                next = time + TimeSpan.FromSeconds(segment.Duration);
            }
        }
    }
}
