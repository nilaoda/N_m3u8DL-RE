using Mp4SubtitleParser;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Util;

internal sealed partial class MuxSubtitleTimeline : IDisposable
{
    private readonly List<string> _temporaryFiles = [];
    internal string[] Inputs { get; }
    internal double?[] MediaStarts { get; }
    internal double? Origin { get; private set; }

    private MuxSubtitleTimeline(string[] inputs)
    {
        Inputs = inputs.ToArray();
        MediaStarts = new double?[inputs.Length];
    }

    [GeneratedRegex(@"(?m)^(?<start>\d+:\d{2}(?::\d{2})?[.,]\d{1,3})[ \t]+-->[ \t]+(?<end>\d+:\d{2}(?::\d{2})?[.,]\d{1,3})(?<settings>[^\r\n]*)$")]
    private static partial Regex TimingRegex();
    [GeneratedRegex(@"(?m)^X-TIMESTAMP-MAP[^\r\n]*\n?")]
    private static partial Regex TimestampMapRegex();
    [GeneratedRegex(@"\n[ \t]*\n")]
    private static partial Regex BlockRegex();

    private sealed record MediaTiming(double Start, double End);
    internal sealed record SubtitleResult(string Text, bool Changed, bool Unresolved, double Offset);

    internal static async Task<MuxSubtitleTimeline> CreateAsync(string[] inputs, string? ffmpegPath, CancellationToken token)
    {
        var result = new MuxSubtitleTimeline(inputs);
        try
        {
            var subtitles = Enumerable.Range(0, inputs.Length).Where(i =>
                Path.GetExtension(inputs[i]).ToLowerInvariant() is ".srt" or ".vtt").ToArray();
            if (subtitles.Length == 0)
                return result;
            var media = new List<MediaTiming>();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (subtitles.Contains(i))
                    continue;
                if (Path.GetExtension(inputs[i]).ToLowerInvariant() is ".ass" or ".ssa" or ".sup" or ".sub" or ".idx")
                    continue;
                var timing = await ProbeAsync(ffmpegPath, inputs[i], token);
                if (timing == null)
                {
                    Array.Clear(result.MediaStarts);
                    Logger.Warn(ResString.toolsSubtitleClockUnknown);
                    return result;
                }
                result.MediaStarts[i] = timing.Start;
                media.Add(timing);
            }
            // 不同媒体时钟区间不重叠时，无法确认公共原点，保留原有混流行为。
            if (media.Count == 0 || media.Max(m => m.Start) >= media.Min(m => m.End))
            {
                Array.Clear(result.MediaStarts);
                Logger.Warn(ResString.toolsSubtitleClockUnknown);
                return result;
            }
            var origin = media.Min(m => m.Start);
            var duration = media.Max(m => m.End) - origin;
            result.Origin = origin;
            foreach (var i in subtitles)
            {
                var text = await File.ReadAllTextAsync(inputs[i], token);
                var repaired = Repair(text, origin, duration);
                if (repaired.Unresolved)
                    Logger.Warn($"{ResString.toolsSubtitleClockUnknown}: {inputs[i]}");
                if (!repaired.Changed)
                    continue;
                var temporary = Path.Combine(Path.GetTempPath(), $"re-sub-{Guid.NewGuid():N}{Path.GetExtension(inputs[i])}");
                result._temporaryFiles.Add(temporary);
                await File.WriteAllTextAsync(temporary, repaired.Text, token);
                result.Inputs[i] = temporary;
                Logger.Info(string.Format(CultureInfo.CurrentCulture, ResString.toolsSubtitleFixed,
                    inputs[i], repaired.Offset.ToString("0.######", CultureInfo.InvariantCulture)));
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static async Task<MediaTiming?> ProbeAsync(string? ffmpegPath, string input, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var extension = Path.GetExtension(input).ToLowerInvariant();
            if (extension is ".mp4" or ".m4a" or ".m4v" or ".mov" or ".m4s")
            {
                var mp4 = MP4MediaInfoUtil.ReadTiming(input, token);
                return mp4 == null ? null : new MediaTiming(mp4.Value.Start, mp4.Value.Start + mp4.Value.Duration);
            }
            // 非 MP4 输入沿用现有 FFmpeg 媒体探测。
            var binary = BinaryToolUtil.Resolve("ffmpeg", ffmpegPath);
            if (binary == null)
            {
                Logger.Warn(ResString.toolsSubtitleProbeMissing);
                return null;
            }
            var timing = await MediainfoUtil.ReadTimingAsync(binary, input, limit.Token);
            return timing == null ? null : new MediaTiming(timing.Value.Start, timing.Value.Start + timing.Value.Duration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or OverflowException ||
                                   ex is OperationCanceledException && !token.IsCancellationRequested)
        {
            Logger.Warn($"{ResString.toolsSubtitleProbeFailed}: {input}: {ex.Message}");
            return null;
        }
    }

    internal static SubtitleResult Repair(string text, double origin, double duration)
    {
        var normalized = text.TrimStart('\uFEFF').ReplaceLineEndings("\n");
        var matches = TimingRegex().Matches(normalized);
        if (matches.Count == 0)
            return new SubtitleResult(text, false, false, 0);
        var sub = new WebVttSub();
        foreach (Match match in matches)
        {
            sub.Cues.Add(new SubCue
            {
                StartTime = WebVttSub.ParseTimestamp(match.Groups["start"].Value),
                EndTime = WebVttSub.ParseTimestamp(match.Groups["end"].Value),
                Payload = "", Settings = ""
            });
        }
        var relativeOverlap = Overlap(sub, duration);
        var firstStart = sub.Cues[0].StartTime;
        var hasMap = HlsSubtitleTimeline.HasTimestampMap(normalized);
        HlsSubtitleTimeline.Normalize(sub, normalized, origin);
        var mappedOverlap = Overlap(sub, duration);
        var offset = (sub.Cues[0].StartTime - firstStart).TotalSeconds;
        // 无映射的 SRT/VTT 可能已经归零。只有原时间不落在媒体区间、
        // 平移后存在重叠时才修复；不能把第一句对白当作节目起点。
        if (mappedOverlap <= 0)
            return new SubtitleResult(text, false, relativeOverlap <= 0 || hasMap, 0);
        if (!hasMap && relativeOverlap > 0)
            return new SubtitleResult(text, false, Math.Abs(offset) >= 0.000001, 0);
        if (!hasMap && Math.Abs(offset) < 0.000001)
            return new SubtitleResult(text, false, false, 0);

        var cueIndex = 0;
        var blocks = BlockRegex().Split(normalized);
        var output = new List<string>();
        foreach (var block in blocks)
        {
            var match = TimingRegex().Match(block);
            if (!match.Success)
            {
                output.Add(TimestampMapRegex().Replace(block, ""));
                continue;
            }
            var cue = sub.Cues[cueIndex++];
            if (cue.EndTime.TotalSeconds <= 0 || cue.StartTime.TotalSeconds >= duration)
                continue;
            var start = TimeSpan.FromSeconds(Math.Max(0, cue.StartTime.TotalSeconds));
            var end = TimeSpan.FromSeconds(Math.Min(duration, cue.EndTime.TotalSeconds));
            var separator = match.Groups["start"].Value.Contains(',') ? ',' : '.';
            var timing = $"{Format(start, separator)} --> {Format(end, separator)}{match.Groups["settings"].Value}";
            // 只替换时间行，保留原来的 cue ID、样式块和字幕正文。
            output.Add(block.Remove(match.Index, match.Length).Insert(match.Index, timing));
        }
        return new SubtitleResult(string.Join("\n\n", output), true, false, offset);
    }

    private static double Overlap(WebVttSub sub, double duration) => sub.Cues.Sum(c => Math.Max(0,
        Math.Min(c.EndTime.TotalSeconds, duration) - Math.Max(c.StartTime.TotalSeconds, 0)));

    private static string Format(TimeSpan time, char separator) => string.Create(CultureInfo.InvariantCulture,
        $"{time.Ticks / TimeSpan.TicksPerHour:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}");

    public void Dispose()
    {
        foreach (var file in _temporaryFiles)
            File.Delete(file);
    }
}
