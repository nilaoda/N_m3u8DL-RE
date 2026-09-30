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

    public static void NormalizeSubtitleOnly(WebVttSub sub, string text, MediaSegment segment)
    {
        var localStart = segment.HlsTime ?? 0;
        var sourceStart = segment.SourceTime ?? localStart;
        var sourceBase = sourceStart - localStart;
        Normalize(sub, text, sourceBase);
        var mpegts = MpegtsRegex().Match(MapRegex().Match(text).Value);
        var timestamp = mpegts.Success ? long.Parse(mpegts.Groups[1].Value, CultureInfo.InvariantCulture) / 90000d : 0;
        const double wrap = (1L << 33) / 90000d;
        timestamp += Math.Round((sourceBase - timestamp) / wrap) * wrap;
        // 纯字幕没有媒体 PTS 可供探测。以分片区间的重叠量区分连续/重启时钟，
        // 以及 VTT 使用播放时间、MPEGTS 带独立起始偏移的情况；不以首句对白归零。
        // 相同得分优先采用 timestamp-map，避免在有效映射上无故切换时钟模式。
        var adjustment = new[] { 0d, sourceBase, -timestamp, sourceBase - timestamp }.Distinct()
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

    public static void Normalize(WebVttSub sub, string text, double mediaOrigin)
    {
        var offset = -mediaOrigin;
        var map = MapRegex().Match(text).Value;
        if (map.Length > 0)
        {
            var local = LocalRegex().Match(map);
            var mpegts = MpegtsRegex().Match(map);
            if (!local.Success || !mpegts.Success)
                throw new FormatException(ResString.hlsTimestampMapInvalid);
            var timestamp = long.Parse(mpegts.Groups[1].Value, CultureInfo.InvariantCulture) / 90000d;
            // MPEGTS 是 33 位时钟。选择与媒体原点最近的周期，避免回绕后偏移约 26.5 小时。
            const double wrap = (1L << 33) / 90000d;
            timestamp += Math.Round((mediaOrigin - timestamp) / wrap) * wrap;
            offset += timestamp - TimeSpan.Parse(local.Groups[1].Value, CultureInfo.InvariantCulture).TotalSeconds;
        }
        // 先映射至源不连续段时间，再由公共时间轴裁剪/移动 cue；不能按最后一句或 cue 数推进。
        foreach (var cue in sub.Cues)
        {
            cue.StartTime += TimeSpan.FromSeconds(offset);
            cue.EndTime += TimeSpan.FromSeconds(offset);
        }
        sub.MpegtsTimestamp = 0;
    }
}
