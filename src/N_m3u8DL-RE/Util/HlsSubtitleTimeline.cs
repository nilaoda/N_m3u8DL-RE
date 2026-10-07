using N_m3u8DL_RE.Common.Resource;
using System.Globalization;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Util;

internal static partial class HlsSubtitleTimeline
{
    [GeneratedRegex(@"X-TIMESTAMP-MAP[^\r\n]*")]
    private static partial Regex MapRegex();
    [GeneratedRegex(@"LOCAL:(\d+:\d+:\d+(?:\.\d+)?)")]
    private static partial Regex LocalRegex();
    [GeneratedRegex(@"MPEGTS:(\d+)")]
    private static partial Regex MpegtsRegex();

    public static bool HasTimestampMap(string text) => MapRegex().IsMatch(text);

    public static void NormalizeSubtitleOnly(WebVttSub sub, string text, MediaSegment segment)
    {
        var localStart = segment.HlsTime ?? 0;
        var sourceStart = segment.SourceTime ?? localStart;
        var sourceBase = sourceStart - localStart;
        var timestamp = Normalize(sub, text, sourceBase, localStart);
        // 纯字幕没有媒体 PTS 可供探测。以分片区间的重叠量区分连续/重启时钟，
        // 以及 VTT 使用播放时间、MPEGTS 带独立起始偏移的情况；不以首句对白归零。
        // 相同得分优先采用 timestamp-map，避免在有效映射上无故切换时钟模式。
        double[] adjustments = [0d, sourceBase, -timestamp, sourceBase - timestamp];
        var adjustment = adjustments.Distinct()
            .OrderByDescending(Overlap).First();
        if (adjustment != 0)
        {
            foreach (var cue in sub.Cues)
            {
                cue.StartTime += TimeSpan.FromSeconds(adjustment);
                cue.EndTime += TimeSpan.FromSeconds(adjustment);
            }
        }

        double Overlap(double offset) => sub.Cues.Sum(c => Math.Max(0,
            Math.Min(c.EndTime.TotalSeconds + offset, localStart + segment.Duration) -
            Math.Max(c.StartTime.TotalSeconds + offset, localStart)));
    }

    public static void ClipBeforeStart(WebVttSub sub)
    {
        // 写出中间字幕前裁剪负时间，避免节目之前的 cue 被误解析到输出时间轴上。
        sub.Cues.RemoveAll(c => c.EndTime <= TimeSpan.Zero);
        foreach (var cue in sub.Cues)
            if (cue.StartTime < TimeSpan.Zero)
                cue.StartTime = TimeSpan.Zero;
    }

    /// <returns>展开回绕周期后的 MPEGTS 锚点，供纯字幕路径比较不同的时钟解释。</returns>
    public static double Normalize(WebVttSub sub, string text, double mediaOrigin, double segmentStart = 0)
    {
        var offset = -mediaOrigin;
        double timestamp = 0;
        var map = MapRegex().Match(text).Value;
        if (map.Length > 0)
        {
            var local = LocalRegex().Match(map);
            var mpegts = MpegtsRegex().Match(map);
            if (!local.Success || !mpegts.Success)
                throw new FormatException(ResString.hlsTimestampMapInvalid);
            timestamp = long.Parse(mpegts.Groups[1].Value, CultureInfo.InvariantCulture) / 90000d;
            var localTime = WebVttSub.ParseTimestamp(local.Groups[1].Value).TotalSeconds;
            // MPEGTS 是 33 位时钟。映射锚点可能比当前字幕早一天以上，
            // 必须以当前 cue 映射后的时间选择回绕周期，而不是只看锚点。
            // 分片位置用于长节目后半段，保留原点到首句对白之间的空白。
            var cueTime = sub.Cues.FirstOrDefault()?.StartTime.TotalSeconds ?? localTime;
            const double wrap = (1L << 33) / 90000d;
            timestamp += Math.Round((mediaOrigin + segmentStart - (timestamp + cueTime - localTime)) / wrap) * wrap;
            offset += timestamp - localTime;
        }
        // 先映射至源不连续段时间，再由公共时间轴裁剪/移动 cue；不能按最后一句或 cue 数推进。
        foreach (var cue in sub.Cues)
        {
            cue.StartTime += TimeSpan.FromSeconds(offset);
            cue.EndTime += TimeSpan.FromSeconds(offset);
        }
        sub.MpegtsTimestamp = 0;
        return timestamp;
    }
}
