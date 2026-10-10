using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Util;

public static class FilterUtil
{
    public static List<StreamSpec> DoFilterKeep(IEnumerable<StreamSpec> lists, StreamFilter? filter)
    {
        if (filter == null) return [];

        var inputs = lists.Where(_ => true);
        if (filter.GroupIdReg != null)
            inputs = inputs.Where(i => i.GroupId != null && filter.GroupIdReg.IsMatch(i.GroupId));
        if (filter.LanguageReg != null)
            inputs = inputs.Where(i => i.Language != null && filter.LanguageReg.IsMatch(i.Language));
        if (filter.NameReg != null)
            inputs = inputs.Where(i => i.Name != null && filter.NameReg.IsMatch(i.Name));
        if (filter.CodecsReg != null)
            inputs = inputs.Where(i => i.Codecs != null && filter.CodecsReg.IsMatch(i.Codecs));
        if (filter.ResolutionReg != null)
            inputs = inputs.Where(i => i.Resolution != null && filter.ResolutionReg.IsMatch(i.Resolution));
        if (filter.FrameRateReg != null)
            inputs = inputs.Where(i => i.FrameRate != null && filter.FrameRateReg.IsMatch($"{i.FrameRate}"));
        if (filter.ChannelsReg != null)
            inputs = inputs.Where(i => i.Channels != null && filter.ChannelsReg.IsMatch(i.Channels));
        if (filter.VideoRangeReg != null)
            inputs = inputs.Where(i => i.VideoRange != null && filter.VideoRangeReg.IsMatch(i.VideoRange));
        if (filter.UrlReg != null)
            inputs = inputs.Where(i => i.Url != null && filter.UrlReg.IsMatch(i.Url));
        if (filter.PeriodIdReg != null)
            inputs = inputs.Where(i => i.PeriodId != null && filter.PeriodIdReg.IsMatch(i.PeriodId));
        if (filter.SegmentsMaxCount != null && inputs.All(i => i.SegmentsCount > 0)) 
            inputs = inputs.Where(i => i.SegmentsCount < filter.SegmentsMaxCount);
        if (filter.SegmentsMinCount != null && inputs.All(i => i.SegmentsCount > 0))
            inputs = inputs.Where(i => i.SegmentsCount > filter.SegmentsMinCount);
        if (filter.PlaylistMinDur != null)
            inputs = inputs.Where(i => i.Playlist?.TotalDuration > filter.PlaylistMinDur);
        if (filter.PlaylistMaxDur != null)
            inputs = inputs.Where(i => i.Playlist?.TotalDuration < filter.PlaylistMaxDur);
        if (filter.BandwidthMin != null)
            inputs = inputs.Where(i => i.Bandwidth >= filter.BandwidthMin);
        if (filter.BandwidthMax != null)
            inputs = inputs.Where(i => i.Bandwidth <= filter.BandwidthMax);
        if (filter.Role.HasValue)
            inputs = inputs.Where(i => i.HasRole(filter.Role.Value));

        // Apply "for" selection.
        if (filter.For == "best")
        {
            // Best stream for each language.
            inputs = inputs
                .GroupBy(i => GetBaseLanguage(i.Language))
                .Select(g => g
                    .OrderByDescending(i => i.Bandwidth)
                    .First());
        }
        else if (filter.For == "worst")
        {
            // Worst stream for each language.
            inputs = inputs
                .GroupBy(i => GetBaseLanguage(i.Language))
                .Select(g => g
                    .OrderBy(i => i.Bandwidth)
                    .First());
        }
        else if (filter.For.StartsWith("best") &&
                 int.TryParse(filter.For["best".Length..], out int bestNumber))
        {
            // Select the N best streams for each language.
            inputs = inputs
                .GroupBy(i => GetBaseLanguage(i.Language))
                .SelectMany(g => g
                    .OrderByDescending(i => i.Bandwidth)
                    .Take(bestNumber));
        }
        else if (filter.For.StartsWith("worst") &&
                 int.TryParse(filter.For["worst".Length..], out int worstNumber))
        {
            // Select the N worst streams for each language.
            inputs = inputs
                .GroupBy(i => GetBaseLanguage(i.Language))
                .SelectMany(g => g
                    .OrderBy(i => i.Bandwidth)
                    .Take(worstNumber));
        }

        return inputs.ToList();
    }

    public static List<StreamSpec> DoFilterDrop(IEnumerable<StreamSpec> lists, StreamFilter? filter)
    {
        if (filter == null) return [..lists];

        var inputs = lists.Where(_ => true);
        var selected = DoFilterKeep(lists, filter);

        inputs = inputs.Where(i => selected.All(s => s.ToString() != i.ToString()));

        return inputs.ToList();
    }

    public static List<StreamSpec> SelectStreams(IEnumerable<StreamSpec> lists)
    {
        var streamSpecs = lists.ToList();
        if (streamSpecs.Count == 1)
            return [..streamSpecs];

        // 基本流
        var basicStreams = streamSpecs.Where(x => x.MediaType == null).ToList();
        // 可选音频轨道
        var audios = streamSpecs.Where(x => x.MediaType == MediaType.AUDIO).ToList();
        // 可选字幕轨道
        var subs = streamSpecs.Where(x => x.MediaType == MediaType.SUBTITLES).ToList();

        var prompt = new MultiSelectionPrompt<StreamSpec>()
                .Title(ResString.promptTitle)
                .UseConverter(x =>
                {
                    if (x.Name != null && x.Name.StartsWith("__"))
                        return $"[darkslategray1]{x.Name[2..]}[/]";
                    return x.ToString().EscapeMarkup().RemoveMarkup();
                })
                .Required()
                .PageSize(10)
                .MoreChoicesText(ResString.promptChoiceText)
                .InstructionsText(ResString.promptInfo)
            ;

        // 默认选中第一个
        var first = streamSpecs.First();
        prompt.Select(first);

        if (basicStreams.Count != 0)
        {
            prompt.AddChoiceGroup(new StreamSpec() { Name = "__Basic" }, basicStreams);
        }

        if (audios.Count != 0)
        {
            prompt.AddChoiceGroup(new StreamSpec() { Name = "__Audio" }, audios);
            // 默认音轨
            if (first.AudioId != null)
            {
                var audio = DefaultTrack(audios, first.AudioId);
                if (audio != null)
                    prompt.Select(audio);
            }
        }
        if (subs.Count != 0)
        {
            prompt.AddChoiceGroup(new StreamSpec() { Name = "__Subtitle" }, subs);
            // 默认字幕轨
            if (first.SubtitleId != null)
            {
                var subtitle = DefaultTrack(subs, first.SubtitleId);
                if (subtitle != null)
                    prompt.Select(subtitle);
            }
        }

        // 如果此时还是没有选中任何流，自动选择一个
        prompt.Select(basicStreams.Concat(audios).Concat(subs).First());

        // 多选
        var selectedStreams = CustomAnsiConsole.Console.Prompt(prompt);

        return selectedStreams;
    }

    /// <summary>
    /// 直播使用。对齐各个轨道的起始。
    /// </summary>
    /// <param name="selectedSteams"></param>
    /// <param name="takeLastCount"></param>
    public static void SyncStreams(List<StreamSpec> selectedSteams, int takeLastCount = 15)
    {
        // 过滤出需要处理的流
        selectedSteams = selectedSteams
            .Where(s => s.Playlist?.MediaParts?.Any(p => p.MediaSegments.Count != 0) == true)
            .ToList();
        if (selectedSteams.Count == 0)
            return;
        if (selectedSteams.All(s => s.Playlist!.IsLive && s.Playlist.MediaParts.All(p => p.PeriodIndex != null)))
        {
            // DASH 序号可能在每个 Period 重置，按完整窗口取尾片，并通过 Period/PTO 对齐音视频。
            var windows = selectedSteams.Select(stream => new
            {
                Stream = stream,
                Segments = stream.Playlist!.MediaParts.SelectMany(part => part.MediaSegments.Select(segment => new
                {
                    Segment = segment,
                    Time = part.PeriodStart is { } start && segment.PresentationTime is { } time
                        ? (double?)(start + time - (part.PresentationTimeOffset ?? 0)) : null,
                })).TakeLast(Math.Max(1, takeLastCount)).ToList(),
            }).ToList();
            foreach (var window in windows)
            {
                // 历史 Period 与当前内容可能有很长的空档，不能为了凑足数量去下载过期媒体。
                var end = window.Segments[^1];
                var earliest = end.Time + end.Segment.Duration - Math.Max(1, takeLastCount) *
                    window.Segments.Max(s => s.Segment.Duration);
                window.Segments.RemoveAll(s => s.Time < earliest);
            }
            var commonStart = windows.Select(window => window.Segments.FirstOrDefault()?.Time).Max();
            foreach (var window in windows)
            {
                var kept = new HashSet<MediaSegment>(window.Segments.Where(s => s.Time == null || commonStart == null ||
                    s.Time + s.Segment.Duration > commonStart).Select(s => s.Segment), ReferenceEqualityComparer.Instance);
                foreach (var part in window.Stream.Playlist!.MediaParts)
                    part.MediaSegments = part.MediaSegments.Where(kept.Contains).ToList();
                window.Stream.Playlist.RemoveEmptyParts();
            }
            return;
        }
        // 通过Date同步
        if (selectedSteams.All(x => x.Playlist!.MediaParts[0].MediaSegments.All(x => x.DateTime != null)))
        {
            var minDate = selectedSteams.Max(s => s.Playlist!.MediaParts[0].MediaSegments.Min(s => s.DateTime))!;
            foreach (var item in selectedSteams)
            {
                foreach (var part in item.Playlist!.MediaParts)
                {
                    // 秒级同步 忽略毫秒
                    part.MediaSegments = part.MediaSegments.Where(s => s.DateTime!.Value.Ticks / TimeSpan.TicksPerSecond >= minDate.Value.Ticks / TimeSpan.TicksPerSecond).ToList();
                }
            }
        }
        else // 通过index同步
        {
            var minIndex = selectedSteams.Max(s => s.Playlist!.MediaParts[0].MediaSegments.Min(s => s.Index));
            foreach (var item in selectedSteams)
            {
                foreach (var part in item.Playlist!.MediaParts)
                {
                    part.MediaSegments = part.MediaSegments.Where(s => s.Index >= minIndex).ToList();
                }
            }
        }

        // 取最新的N个分片
        if (selectedSteams.Any(x => x.Playlist!.MediaParts[0].MediaSegments.Count > takeLastCount))
        {
            var skipCount = selectedSteams.Min(x => x.Playlist!.MediaParts[0].MediaSegments.Count) - takeLastCount + 1;
            if (skipCount < 0) skipCount = 0;
            foreach (var item in selectedSteams)
            {
                foreach (var part in item.Playlist!.MediaParts)
                {
                    part.MediaSegments = part.MediaSegments.Skip(skipCount).ToList();
                }
            }
        }
    }

    /// <summary>
    /// 应用用户自定义的分片范围
    /// </summary>
    /// <param name="selectedSteams"></param>
    /// <param name="customRange"></param>
    public static void ApplyCustomRange(List<StreamSpec> selectedSteams, CustomRange? customRange)
    {
        if (customRange == null) return;

        Logger.InfoMarkUp($"{ResString.customRangeFound}[turquoise4 underline]{customRange.InputStr}[/]");
        Logger.WarnMarkUp($"[darkorange3_1]{ResString.customRangeWarn}[/]");

        var filterByIndex = customRange is { StartSegIndex: not null, EndSegIndex: not null };
        var filterByTime = customRange is { StartSec: not null, EndSec: not null };

        if (!filterByIndex && !filterByTime)
        {
            Logger.ErrorMarkUp(ResString.customRangeInvalid);
            return;
        }

        foreach (var stream in selectedSteams)
        {
            if (stream.Playlist == null) continue;
            // 先冻结原始时间轴；边过滤边求和会让删除前一个 part 后的后续起点归零。
            var starts = new Dictionary<MediaSegment, double>(ReferenceEqualityComparer.Instance);
            double elapsed = 0;
            foreach (var segment in stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments))
            {
                starts[segment] = segment.SourceTime ?? elapsed;
                elapsed += segment.Duration;
            }
            foreach (var part in stream.Playlist.MediaParts)
            {
                List<MediaSegment> newSegments;
                if (filterByIndex)
                    newSegments = part.MediaSegments.Where(seg => seg.Index >= customRange.StartSegIndex && seg.Index <= customRange.EndSegIndex).ToList();
                else
                    newSegments = part.MediaSegments.Where(seg => starts[seg] >= customRange.StartSec
                                                                  && starts[seg] <= customRange.EndSec).ToList();

                part.MediaSegments = newSegments;
            }
            var first = stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).FirstOrDefault();
            stream.SkippedDuration = first == null ? 0 : starts[first];
            stream.Playlist.RemoveEmptyParts();
        }
    }

    // 去重/删段后默认 ID 可能不再存在，回退到可用选项，不能使交互界面崩溃。
    internal static StreamSpec? DefaultTrack(List<StreamSpec> tracks, string id) =>
        tracks.FirstOrDefault(s => s.GroupId == id) ?? tracks.FirstOrDefault();

    /// <summary>
    /// 根据用户输入，清除广告分片
    /// </summary>
    /// <param name="selectedSteams"></param>
    /// <param name="keywords"></param>
    public static void CleanAd(List<StreamSpec> selectedSteams, string[]? keywords, bool log = true)
    {
        var regList = ParseAdKeywords(keywords);
        foreach (var reg in log ? regList : [])
        {
            Logger.InfoMarkUp($"{ResString.customAdKeywordsFound}[turquoise4 underline]{reg}[/]");
        }

        foreach (var stream in selectedSteams)
        {
            if (stream.Playlist == null) continue;

            // 未启用广告过滤的直播可能只有 init，保留它等待首个媒体分片。
            if (stream.Playlist.IsLive && regList.Count == 0)
                continue;

            var countBefore = stream.SegmentsCount;
            var keptParts = new List<MediaPart>();

            foreach (var part in stream.Playlist.MediaParts)
            {
                // init 命中广告时，依赖它的媒体也必须整体移除，不能借用正文 init。
                var init = part.MediaInit;
                if (init != null && IsAd(init.Url, regList))
                {
                    part.MediaSegments = [];
                    part.MediaInit = null;
                    continue;
                }
                // 没有找到广告分片
                if (part.MediaSegments.All(x => !IsAd(x.Url, regList)))
                {
                    keptParts.Add(part);
                    continue;
                }
                // 找到广告分片 清理
                if (stream.Playlist.IsLive)
                {
                    part.MediaSegments = CleanAdSegments(part.MediaSegments, regList);
                    keptParts.Add(part);
                    continue;
                }
                // 点播中删除段内广告会留下 tfdt 空洞，按原始相邻关系拆段。
                // 每段继续使用原 init，但独立 remux/concat 后才能移除空洞而不丢正文。
                var run = new List<MediaSegment>();
                foreach (var segment in part.MediaSegments)
                {
                    if (!IsAd(segment.Url, regList))
                        run.Add(segment);
                    else if (run.Count > 0)
                    {
                        keptParts.Add(part.WithSegments(run));
                        run = [];
                    }
                }
                if (run.Count > 0)
                    keptParts.Add(part.WithSegments(run));
            }
            stream.Playlist.MediaParts = keptParts;

            // 如果 #EXT-X-MAP 初始化分片本身命中广告关键字，也一并清除。
            // 同时移除所有没有媒体依赖的 init，即使其 URL 未命中广告。
            stream.Playlist.RemoveEmptyParts();

            var countAfter = stream.SegmentsCount;

            if (log && countBefore != countAfter)
            {
                Logger.WarnMarkUp("[grey]{} segments => {} segments[/]", countBefore, countAfter);
            }
        }
    }

    /// <summary>
    /// 将广告关键字编译为正则表达式列表（供 VOD 与直播复用）
    /// </summary>
    /// <param name="keywords"></param>
    public static List<Regex> ParseAdKeywords(string[]? keywords)
    {
        return keywords == null ? [] : keywords.Select(s => new Regex(s)).ToList();
    }

    /// <summary>
    /// 判断给定的URL是否命中任意一个广告关键字正则
    /// </summary>
    /// <param name="url"></param>
    /// <param name="regList"></param>
    public static bool IsAd(string url, IEnumerable<Regex> regList)
    {
        return regList.Any(reg => reg.IsMatch(url));
    }

    /// <summary>
    /// 从分片列表中剔除命中广告关键字的分片，返回过滤后的新列表
    /// </summary>
    /// <param name="segments"></param>
    /// <param name="regList"></param>
    public static List<MediaSegment> CleanAdSegments(List<MediaSegment> segments, List<Regex> regList)
    {
        if (regList.Count == 0) return segments;
        return segments.Where(x => !IsAd(x.Url, regList)).ToList();
    }

    private static string GetBaseLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return string.Empty;

        return language
            .Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries)[0]
            .ToLowerInvariant();
    }
}