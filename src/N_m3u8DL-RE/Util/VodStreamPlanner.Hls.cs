using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.Util;

internal static partial class VodStreamPlanner
{
    private static void AlignHlsTimeline(List<StreamSpec> streams)
    {
        var groups = streams.SelectMany(s => (s.Playlist?.MediaParts ?? []).Select(p => (Stream: s, Part: p)))
            .GroupBy(x => x.Part.DiscontinuitySequence).OrderBy(g => g.Key).ToList();
        double outputStart = 0;
        var hasMedia = streams.Any(s => s.MediaType != MediaType.SUBTITLES);
        foreach (var group in groups)
        {
            // 纯字幕下载由字幕分片定义保留区间，同样需要压缩被删段产生的空洞。
            var spans = group.Where(x => (!hasMedia || x.Stream.MediaType != MediaType.SUBTITLES) && x.Part.MediaSegments.Count > 0)
                .Select(x => (x.Part, Start: x.Part.MediaSegments[0].HlsTime ?? 0,
                    End: (x.Part.MediaSegments[^1].HlsTime ?? 0) + x.Part.MediaSegments[^1].Duration))
                .OrderBy(x => x.Start).ToList();
            var intervals = new List<(double Start, double End, double Offset)>();
            double duration = 0;
            foreach (var span in spans)
            {
                if (intervals.Count > 0 && span.Start <= intervals[^1].End + 0.001)
                {
                    var previous = intervals[^1];
                    var end = Math.Max(previous.End, span.End);
                    duration += end - previous.End;
                    intervals[^1] = (previous.Start, end, previous.Offset);
                }
                else
                {
                    intervals.Add((span.Start, span.End, duration));
                    duration += span.End - span.Start;
                }
            }
            foreach (var span in spans)
            {
                var interval = intervals.First(i => span.Start >= i.Start && span.End <= i.End);
                span.Part.OutputStart = outputStart + interval.Offset + span.Start - interval.Start;
                span.Part.OutputDuration = span.End - span.Start;
            }
            foreach (var entry in group.Where(x => x.Stream.MediaType == MediaType.SUBTITLES))
            {
                var replacements = new List<MediaPart>();
                // 字幕分片边界可能与媒体不同，选重叠文件后按 cue 裁剪；不能按分片编号同步删除。
                foreach (var interval in intervals)
                {
                    var segments = entry.Part.MediaSegments.Where(s => s.HlsTime < interval.End - 0.001 &&
                        s.HlsTime + s.Duration > interval.Start + 0.001).ToList();
                    if (segments.Count == 0)
                        continue;
                    var copy = entry.Part.WithSegments(segments);
                    copy.OutputStart = outputStart + interval.Offset;
                    copy.OutputInpoint = interval.Start;
                    copy.OutputDuration = interval.End - interval.Start;
                    replacements.Add(copy);
                }
                var parts = entry.Stream.Playlist!.MediaParts;
                var index = parts.IndexOf(entry.Part);
                parts.RemoveAt(index);
                parts.InsertRange(index, replacements);
            }
            outputStart += duration;
        }
        foreach (var stream in streams) stream.Playlist!.RemoveEmptyParts();
    }
}
