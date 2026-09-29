using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.DownloadManager;

internal sealed class LiveSegmentTracker
{
    private MediaSegment? lastSegment;
    private long nextRecordingIndex;

    public List<MediaSegment> Filter(List<MediaSegment> segments, bool isHls, Func<MediaSegment, string> getSourceName)
    {
        if (lastSegment == null || segments.Count == 0) return segments;

        int lastIndex;
        if (lastSegment.DateTime is { } lastDateTime && segments.All(s => s.DateTime != null))
        {
            var timestamp = GetUnixTimestamp(lastDateTime);
            bool sameTime(MediaSegment s) => GetUnixTimestamp(s.DateTime!.Value) == timestamp;
            lastIndex = segments.FindLastIndex(s => sameTime(s) && s.Index == lastSegment.Index &&
                SameSource(s, lastSegment));
            if (lastIndex < 0 && isHls)
                lastIndex = segments.FindLastIndex(s => sameTime(s) && s.Index == lastSegment.Index &&
                    SamePathAndRange(s, lastSegment));
            if (lastIndex < 0)
                lastIndex = FindUniqueIndex(segments, s => sameTime(s) && SameSource(s, lastSegment));
            if (lastIndex < 0)
                lastIndex = FindUniqueIndex(segments, sameTime);
        }
        else if (isHls)
        {
            // 循环复用 URL 的播放列表必须先用源序号定位重叠片段。
            lastIndex = segments.FindLastIndex(s => s.Index == lastSegment.Index &&
                SameSource(s, lastSegment));
            // 签名查询参数可能随刷新变化，此时比较路径和源序号。
            if (lastIndex < 0)
                lastIndex = segments.FindLastIndex(s => s.Index == lastSegment.Index &&
                    SamePathAndRange(s, lastSegment));
            // 源序号重置时，只接受唯一的 URL 匹配；多个候选无法确定哪个是旧片段。
            if (lastIndex < 0)
                lastIndex = FindUniqueIndex(segments, s => SameSource(s, lastSegment));
        }
        else
        {
            var lastName = getSourceName(lastSegment);
            lastIndex = segments.FindLastIndex(s => getSourceName(s) == lastName);
        }

        return lastIndex < 0 ? segments : segments.Skip(lastIndex + 1).ToList();
    }

    public void Record(List<MediaSegment> segments)
    {
        foreach (var segment in segments)
            segment.RecordingIndex = ++nextRecordingIndex;

        if (segments.Count > 0)
            lastSegment = segments[^1];
    }

    public static string GetFileName(MediaSegment segment, string sourceName)
    {
        return segment.RecordingIndex is { } index
            ? $"{index:D10}_{OtherUtil.TruncateFileName(sourceName, 200)}"
            : sourceName;
    }

    private static bool SameSource(MediaSegment left, MediaSegment right)
    {
        return left.Url == right.Url && left.StartRange == right.StartRange && left.ExpectLength == right.ExpectLength;
    }

    private static int FindUniqueIndex(List<MediaSegment> segments, Predicate<MediaSegment> matches)
    {
        var matchIndex = -1;
        for (var i = 0; i < segments.Count; i++)
        {
            if (!matches(segments[i])) continue;
            if (matchIndex >= 0) return -1;
            matchIndex = i;
        }
        return matchIndex;
    }

    private static bool SamePathAndRange(MediaSegment left, MediaSegment right)
    {
        return left.Url.Split('?')[0] == right.Url.Split('?')[0] &&
            left.StartRange == right.StartRange && left.ExpectLength == right.ExpectLength;
    }

    // 文件命名和重叠判断共用毫秒精度，避免低延迟直播中同一秒多个片段混淆。
    public static long GetUnixTimestamp(DateTime dateTime)
    {
        return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeMilliseconds();
    }
}
