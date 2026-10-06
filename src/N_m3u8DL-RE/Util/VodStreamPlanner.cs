using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util;

internal static partial class VodStreamPlanner
{
    // Representation ID 仅在所在 Period 内有效；按所选轨道的类型、编码和语言寻找后续媒体。
    public static List<StreamSpec> Build(List<StreamSpec> streams, List<StreamSpec> selected,
        StreamFilter? videoFilter = null, StreamFilter? audioFilter = null, StreamFilter? subtitleFilter = null,
        List<StreamSpec>? sourceStreams = null)
    {
        var periods = (sourceStreams ?? streams).Where(s => s.Playlist?.IsLive == false)
            .GroupBy(s => s.Playlist!.MediaParts.FirstOrDefault()?.PeriodIndex)
            .Where(g => g.Key != null).OrderBy(g => g.Key).ToList();
        if (periods.Count < 2 && sourceStreams == null)
            return selected;

        var consumed = new HashSet<StreamSpec>(ReferenceEqualityComparer.Instance);
        var result = new List<StreamSpec>();
        foreach (var seed in selected)
        {
            if (!consumed.Add(seed))
                continue;
            var filter = seed.MediaType switch
            {
                MediaType.AUDIO => audioFilter,
                MediaType.SUBTITLES => subtitleFilter,
                _ => videoFilter,
            };
            var parts = new List<MediaPart>();
            var seedPeriod = periods.FirstOrDefault(p => p.Key == seed.Playlist?.MediaParts.FirstOrDefault()?.PeriodIndex)?.ToList() ?? [seed];
            var trackIdentity = seed;
            long index = 0;
            double sourceElapsed = 0;
            foreach (var period in periods)
            {
                // best/worst 必须在当前逻辑轨道内比较，不能让高码率音频挤掉视频。
                // 使用完整源 Period 判断命名是否有歧义，不能让去广告/删轨改变身份判断。
                var periodTracks = MatchingTracks(trackIdentity, period, seedPeriod);
                var candidates = streams.Where(s => s.Playlist?.MediaParts.FirstOrDefault()?.PeriodIndex == period.Key &&
                    periodTracks.Any(source => SameTrack(source, s))).ToList();
                var available = candidates.Where(s => CompatibleTrack(seed, s)).ToList();
                // 先约束编码/声道，再选 best/worst，避免另一编码的高码率流抢占名额。
                if (filter != null)
                    available = FilterUtil.DoFilterKeep(available, filter);
                var match = available.Where(s => !consumed.Contains(s) || ReferenceEquals(s, seed))
                    .Where(s => CompatibleTrack(seed, s))
                    .OrderByDescending(s => ReferenceEquals(s, seed))
                    .ThenByDescending(s => s.Resolution == seed.Resolution)
                    .ThenBy(s => Math.Abs(Pixels(s.Resolution) - Pixels(seed.Resolution)))
                    .ThenBy(s => Math.Abs((long)(s.Bandwidth ?? 0) - (seed.Bandwidth ?? 0)))
                    .ThenByDescending(s => s.GroupId == seed.GroupId)
                    .FirstOrDefault();
                // 源编号要计入被排除的 Period 和段内广告。广告编码可以与正文不同，
                // 它只参与源位置计算，不参与兼容性检查或下载。
                var source = periodTracks
                    .OrderByDescending(s => ReferenceEquals(s, match))
                    .ThenByDescending(s => match != null && s.GroupId == match.GroupId)
                    .ThenByDescending(s => CompatibleTrack(seed, s))
                    .ThenByDescending(s => s.Resolution == seed.Resolution)
                    .ThenBy(s => Math.Abs(Pixels(s.Resolution) - Pixels(seed.Resolution)))
                    .ThenBy(s => Math.Abs((long)(s.Bandwidth ?? 0) - (seed.Bandwidth ?? 0)))
                    .FirstOrDefault();
                var positions = new Dictionary<MediaSegment, (long Index, double Time)>(ReferenceEqualityComparer.Instance);
                if (source != null)
                {
                    foreach (var part in source.Playlist!.MediaParts)
                    {
                        var start = part.PeriodStart ?? sourceElapsed;
                        double elapsed = 0;
                        foreach (var segment in part.MediaSegments)
                        {
                            positions[segment] = (index++, start +
                                (segment.PresentationTime is { } pts ? pts - (part.PresentationTimeOffset ?? 0) : elapsed));
                            elapsed += segment.Duration;
                        }
                        sourceElapsed = start + (part.PeriodDuration ?? elapsed);
                    }
                }
                if (match == null)
                {
                    // 不同编码不能靠 stream copy 安全拼接，明确报错，避免下载成功却丢掉正文。
                    if ((filter == null ? candidates : FilterUtil.DoFilterKeep(candidates, filter))
                        .Any(s => !CompatibleTrack(seed, s)))
                        throw new NotSupportedException(string.Format(ResString.vodPeriodIncompatible, period.Key, seed.ToShortShortString()));
                    var ambiguousLabel = seed.MediaType == MediaType.AUDIO && candidates.Count == 0 &&
                        streams.Any(s => s.Playlist?.MediaParts.FirstOrDefault()?.PeriodIndex == period.Key &&
                            SameTrack(trackIdentity, s, ignoreName: true) && (string.IsNullOrEmpty(trackIdentity.Name) || string.IsNullOrEmpty(s.Name)));
                    if ((candidates.Count > 0 || ambiguousLabel) && filter?.PeriodIdReg == null)
                        Logger.Warn(string.Format(ResString.vodPeriodNoMatch, period.Key, seed.ToShortShortString()));
                    continue;
                }
                consumed.Add(match);
                // 种子没有 Label 时，记住所匹配音轨首次出现的名称；不能把后续不同增强级别都视为缺失名称。
                if (seed.MediaType == MediaType.AUDIO && string.IsNullOrEmpty(trackIdentity.Name) && !string.IsNullOrEmpty(match.Name))
                    trackIdentity = match;
                // 点播，同一逻辑轨道的各 Period 作为新的 part 出现，init 和时间轴随 part 保留。
                foreach (var part in match.Playlist!.MediaParts)
                {
                    parts.Add(new MediaPart
                    {
                        MediaInit = part.MediaInit,
                        PeriodIndex = part.PeriodIndex,
                        PeriodId = part.PeriodId,
                        PeriodStart = part.PeriodStart,
                        PeriodDuration = part.PeriodDuration,
                        PresentationTimeOffset = part.PresentationTimeOffset,
                        RepresentationId = part.RepresentationId ?? match.GroupId,
                        Codecs = match.Codecs,
                        MediaSegments = part.MediaSegments.Select(segment =>
                        {
                            var position = positions[segment];
                            var copy = segment.WithIndex(position.Index);
                            copy.SourceTime = position.Time;
                            return copy;
                        }).ToList(),
                    });
                }
            }
            if (parts.Count == 0)
            {
                result.Add(seed);
                continue;
            }
            var playlist = new Playlist { MediaParts = parts };
            var plan = seed.WithPlaylist(playlist);
            result.Add(plan);
            if (parts.Count > 1)
                Logger.InfoMarkUp($"[grey]{string.Format(ResString.vodPeriodsPlanned, parts.Count, seed.ToShortShortString())}[/]");
        }
        return result;
    }

    internal static bool SameTrack(StreamSpec a, StreamSpec b, bool ignoreName = false) =>
        (a.MediaType ?? MediaType.VIDEO) == (b.MediaType ?? MediaType.VIDEO) &&
        string.Equals(a.Language ?? "und", b.Language ?? "und", StringComparison.OrdinalIgnoreCase) &&
        a.GetRoleKey() == b.GetRoleKey() &&
        // 同语言、同编码的增强音轨也可能有不同用途，不能按码率接到另一条音轨上。
        (a.MediaType != MediaType.AUDIO ||
         (ignoreName || string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase)) &&
         string.Equals(a.VolumeAdjust, b.VolumeAdjust, StringComparison.OrdinalIgnoreCase));

    private static List<StreamSpec> MatchingTracks(StreamSpec seed, IEnumerable<StreamSpec> tracks, List<StreamSpec> seedPeriod)
    {
        var candidates = tracks.Where(s => SameTrack(seed, s, ignoreName: true)).ToList();
        var named = candidates.Where(s => SameTrack(seed, s)).ToList();
        if (named.Count > 0 || seed.MediaType != MediaType.AUDIO)
            return named;

        // Label 可能只在某个 Period 出现。仅在两端都只有一种命名音轨时允许缺失，
        // 避免把缺少 Label 的普通音轨误接到 High/Medium 等增强音轨上。
        if (candidates.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1 ||
            seedPeriod.Where(s => SameTrack(seed, s, ignoreName: true))
                .Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return [];
        return candidates.Where(s => string.IsNullOrEmpty(seed.Name) || string.IsNullOrEmpty(s.Name)).ToList();
    }

    private static bool CompatibleTrack(StreamSpec a, StreamSpec b) =>
        SameTrack(a, b, ignoreName: true) && (a.Codecs == null || b.Codecs == null || CodecFamily(a.Codecs) == CodecFamily(b.Codecs)) &&
        (a.Channels == null || b.Channels == null || a.Channels == b.Channels) &&
        (a.VideoRange == null || b.VideoRange == null || a.VideoRange == b.VideoRange);

    public static void AlignPeriods(List<StreamSpec> streams)
    {
        var periods = streams.SelectMany(s => (s.Playlist?.MediaParts ?? []).Select(p => (Stream: s, Part: p)))
            .Where(x => x.Part.PeriodIndex != null).GroupBy(x => x.Part.PeriodIndex).OrderBy(g => g.Key).ToList();
        var hasMedia = streams.Any(s => s.MediaType != MediaType.SUBTITLES);
        double start = 0;
        foreach (var period in periods)
        {
            var spans = period.Select(entry =>
            {
                var part = entry.Part;
                var sourceStart = (part.MediaSegments.FirstOrDefault()?.PresentationTime ?? part.PresentationTimeOffset ?? 0) -
                    (part.PresentationTimeOffset ?? 0);
                var sourceEnd = Math.Min(part.PeriodDuration ?? double.MaxValue,
                    sourceStart + part.MediaSegments.Sum(s => s.Duration));
                return (entry.Stream, Part: part, Start: Math.Max(0, sourceStart), End: sourceEnd);
            }).OrderBy(span => span.Start).ToList();
            var intervals = new List<(double Start, double End, double Offset)>();
            double duration = 0;
            // 用音视频的保留区间定义共同时间轴，空字幕或覆盖整段的 VTT 不能把已删广告补回来。
            // 仅下载字幕时，才采用字幕自身的区间；单条音视频缺失仍保留其它媒体覆盖的空白。
            foreach (var span in spans.Where(s => !hasMedia || s.Stream.MediaType != MediaType.SUBTITLES))
            {
                if (span.End <= span.Start)
                    continue;
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
                if (span.Stream.MediaType == MediaType.SUBTITLES)
                {
                    var replacements = new List<MediaPart>();
                    foreach (var interval in intervals.Where(i => i.End > span.Start && i.Start < span.End))
                    {
                        var clipStart = Math.Max(interval.Start, span.Start);
                        var clipEnd = Math.Min(interval.End, span.End);
                        var copy = span.Part.WithSegments(span.Part.MediaSegments.Where(segment =>
                        {
                            var time = (segment.PresentationTime ?? span.Part.PresentationTimeOffset ?? 0) -
                                (span.Part.PresentationTimeOffset ?? 0);
                            return time < clipEnd && time + segment.Duration > clipStart;
                        }).ToList());
                        if (copy.MediaSegments.Count == 0)
                            continue;
                        copy.OutputStart = start + interval.Offset;
                        copy.OutputInpoint = (copy.PresentationTimeOffset ?? 0) + interval.Start;
                        copy.OutputDuration = clipEnd - interval.Start;
                        replacements.Add(copy);
                    }
                    var parts = span.Stream.Playlist!.MediaParts;
                    var index = parts.IndexOf(span.Part);
                    parts.RemoveAt(index);
                    parts.InsertRange(index, replacements);
                    continue;
                }
                if (span.End <= span.Start)
                    throw new InvalidOperationException(string.Format(ResString.vodMediaOutsidePeriod, period.Key));
                var intervalForPart = intervals.First(i => span.Start >= i.Start && span.End <= i.End);
                span.Part.OutputStart = start + intervalForPart.Offset;
                span.Part.OutputDuration = span.End - intervalForPart.Start;
                span.Part.OutputInpoint = (span.Part.PresentationTimeOffset ?? 0) + intervalForPart.Start;
            }
            start += duration;
        }
    }

    public static void CaptureHlsTimeline(List<StreamSpec> streams)
    {
        foreach (var stream in streams)
        {
            double sourceTime = 0;
            foreach (var group in stream.Playlist!.MediaParts.GroupBy(p => p.DiscontinuitySequence))
            {
                double time = 0;
                foreach (var part in group)
                {
                    foreach (var segment in part.MediaSegments)
                    {
                        segment.HlsTime = time;
                        segment.SourceTime = sourceTime;
                        time += segment.Duration;
                        sourceTime += segment.Duration;
                    }
                }
            }
        }
    }

    public static void AlignHlsDiscontinuities(List<StreamSpec> streams)
    {
        if (streams.SelectMany(s => s.Playlist?.MediaParts ?? []).SelectMany(p => p.MediaSegments).Any(s => s.HlsTime != null))
        {
            AlignHlsTimeline(streams);
            return;
        }
        var groups = streams.SelectMany(s => s.Playlist?.MediaParts ?? [])
            .Where(p => p.DiscontinuitySequence != null)
            .GroupBy(p => p.DiscontinuitySequence).OrderBy(g => g.Key);
        double start = 0;
        foreach (var group in groups)
        {
            var tracks = streams.Select(s => s.Playlist?.MediaParts
                .Where(p => p.DiscontinuitySequence == group.Key).ToList() ?? []).Where(parts => parts.Count > 0).ToList();
            var duration = tracks.Max(parts => parts.Sum(p => p.MediaSegments.Sum(s => s.Duration)));
            foreach (var parts in tracks)
            {
                double offset = 0;
                for (var i = 0; i < parts.Count; i++)
                {
                    var part = parts[i];
                    part.OutputStart = start + offset;
                    var ownDuration = part.MediaSegments.Sum(s => s.Duration);
                    // AAC 和视频帧的边界可能差几毫秒，每个 discontinuity 用共同
                    // 时长衔接，避免广告切换次数增加后音视频偏差不断累积。
                    part.OutputDuration = i == parts.Count - 1 ? duration - offset : ownDuration;
                    offset += ownDuration;
                }
            }
            start += duration;
        }
    }

    private static string? CodecFamily(string? codec) => codec?.Split('.')[0] switch
    {
        "avc1" or "avc3" => "h264",
        "hev1" or "hvc1" => "hevc",
        "mp4a" => codec, // AAC 的不同音频对象类型不能任意拼接。
        var family => family,
    };

    private static long Pixels(string? resolution)
    {
        var values = resolution?.Split('x');
        return values is { Length: 2 } && long.TryParse(values[0], out var width) &&
            long.TryParse(values[1], out var height) ? width * height : 0;
    }
}
