using System.Globalization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Util;

internal static partial class VodPartSelector
{
    // DASH 使用清单顺序（id 可以缺失）；HLS 使用不连续序号，同一序号的 MAP 属于同一段。
    // 编号不随选流、范围或 URL 去广告重排，用户预览后可以用相同编号重跑下载。
    internal static long Id(MediaPart part) => part.PeriodIndex ?? part.DiscontinuitySequence ?? 0;

    public static HashSet<long> ParseIds(string value)
    {
        var ids = new HashSet<long>();
        foreach (var item in value.Split(','))
        {
            var range = item.Trim().Split('-');
            if (range.Length is < 1 or > 2 || !long.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                (range.Length == 2 && !long.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out _)))
                throw new ArgumentException(ResString.vodDropPartsInvalid);
            var last = range.Length == 1 ? first : long.Parse(range[1], CultureInfo.InvariantCulture);
            if (last < first || last - first > 100000)
                throw new ArgumentException(ResString.vodDropPartsRangeInvalid);
            for (var id = first; id <= last; id++)
            {
                ids.Add(id);
                if (id == long.MaxValue)
                    break;
            }
        }
        return ids;
    }

    public static void Apply(List<StreamSpec> streams, string? value)
    {
        if (value == null)
            return;
        EnsureVod(streams);
        var ids = ParseIds(value);
        var available = streams.SelectMany(s => s.Playlist?.MediaParts ?? []).Select(Id).ToHashSet();
        var unknown = ids.Except(available).ToList();
        if (unknown.Count > 0)
            throw new ArgumentException(string.Format(ResString.vodPartIdsUnknown, string.Join(',', unknown)));
        // 全轨道同时删除，避免音频/空字幕把已删除广告重新加入公共时间轴。
        foreach (var stream in streams)
        {
            stream.Playlist!.MediaParts.RemoveAll(part => ids.Contains(Id(part)));
            stream.Playlist.RemoveEmptyParts();
        }
    }

    // 展示按视频、音频、字幕排序，归组身份仍按原来的键排序，避免界面调整影响选段。
    private static int DisplayOrder(string label) => label.StartsWith("Vid ", StringComparison.Ordinal) ? 0 :
        label.StartsWith("Aud ", StringComparison.Ordinal) ? 1 : label.StartsWith("Sub ", StringComparison.Ordinal) ? 2 : 3;

    internal sealed record Configuration(string Key, string Label);
    internal sealed class PartGroup(string configuration)
    {
        public string Configuration { get; } = configuration;
        public List<long> Ids { get; } = [];
        public double Duration { get; set; }
        public List<double> SectionDurations { get; } = [];
        private string DisplayDuration => Duration < 60
            ? Duration.ToString("0.###", CultureInfo.InvariantCulture) + " s" : GlobalUtil.FormatTime((int)Duration);
        public string Display => $"{Configuration} | {DisplayDuration} | " +
            string.Format(ResString.vodPartCount, Ids.Count);
    }

    internal static bool ShouldPrompt(MyOption options) => options.VodSelectParts ??
        (!options.AutoSelect && !options.SubOnly && options.VideoFilter == null && options.AudioFilter == null &&
         options.SubtitleFilter == null && options.VodDropParts == null && !Console.IsInputRedirected &&
         CustomAnsiConsole.Console.Profile.Capabilities.Interactive);

    internal static bool HasMultipleSections(List<StreamSpec> streams) => streams
        .SelectMany(s => s.Playlist?.MediaParts ?? []).Select(Id).Distinct().Skip(1).Any();

    // 同一段内的画质集合、音频配置共同定义一组。忽略码率、URL 和 track ID，
    // 不把重复 MAP 当成不同选择；字幕缺失/空广告字幕不能把相同音视频配置拆开。
    internal static List<PartGroup> BuildGroups(List<StreamSpec> streams,
        Func<StreamSpec, MediaPart, Configuration>? configuration = null)
    {
        var groups = new Dictionary<string, PartGroup>(StringComparer.Ordinal);
        foreach (var section in streams.SelectMany(s => (s.Playlist?.MediaParts ?? [])
                     .Where(p => p.MediaSegments.Count > 0).Select(p => (Stream: s, Part: p)))
                     .GroupBy(x => Id(x.Part)).OrderBy(g => g.Key))
        {
            var media = section.Where(x => x.Stream.MediaType != MediaType.SUBTITLES).ToList();
            if (media.Count == 0)
                media = section.ToList();
            var configs = media.Select(x => configuration?.Invoke(x.Stream, x.Part) ?? ManifestConfiguration(x.Stream, x.Part))
                .DistinctBy(c => c.Key).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();
            var key = string.Concat(configs.Select(c => $"{c.Key.Length}:{c.Key}"));
            if (!groups.TryGetValue(key, out var group))
            {
                group = new PartGroup(GroupLabel(media, configs, configuration == null));
                groups.Add(key, group);
            }
            group.Ids.Add(section.Key);
            var duration = media.GroupBy(x => x.Stream, ReferenceEqualityComparer.Instance)
                .Max(track =>
                {
                    var segments = track.Sum(x => x.Part.MediaSegments.Sum(s => s.Duration));
                    var period = track.Max(x => x.Part.PeriodDuration);
                    // URL 过滤可能把一个 Period 拆成多个 part，不能重复累加整段时长。
                    return segments > 0 ? Math.Min(segments, period ?? double.MaxValue) : period ?? 0;
                });
            group.Duration += duration;
            group.SectionDurations.Add(duration);
        }
        return groups.Values.SelectMany(SplitByDuration).ToList();
    }

    private static string GroupLabel(List<(StreamSpec Stream, MediaPart Part)> media,
        List<Configuration> configs, bool manifestOnly)
    {
        var labels = configs.Select(c => c.Label).Distinct().OrderBy(DisplayOrder)
            .ThenBy(label => label, StringComparer.Ordinal).ToList();
        var video = media.Where(x => x.Stream.MediaType is null or MediaType.VIDEO).ToList();
        // DASH 选段发生在选画质之前，同一个 Period 中的多个视频配置是备选画质，
        // 不能用加号展示成同时下载的轨道。这里只压缩标签，完整配置集合仍用于归组。
        // HLS 已选中的轨道及 init 探测结果继续显示实际配置。
        if (!manifestOnly || video.Count == 0 || video.Any(x => x.Part.PeriodIndex == null))
            return string.Join(" + ", labels);
        var choices = video.Select(x => ManifestConfiguration(x.Stream, x.Part)).DistinctBy(c => c.Key).ToList();
        if (choices.Count < 2)
            return string.Join(" + ", labels);

        var codecs = video.Select(x => (x.Part.Codecs ?? x.Stream.Codecs)?.Split('.')[0] switch
        {
            "avc1" or "avc3" => "H.264",
            "hev1" or "hvc1" => "H.265",
            "vp9" or "vp09" => "VP9",
            "av01" => "AV1",
            var codec => codec ?? "?",
        }).Distinct().Order(StringComparer.Ordinal);
        var resolutions = video.Select(x => x.Stream.Resolution).OfType<string>()
            .Where(r => !string.IsNullOrWhiteSpace(r)).Distinct().OrderBy(ResolutionArea)
            .ThenBy(r => r, StringComparer.Ordinal).ToList();
        string[] limits = resolutions.Count == 0 ? [] : [resolutions[0], resolutions[^1]];
        var range = string.Join("–", limits.Distinct());
        var summary = $"Vid {string.Join(" / ", codecs)} | " +
            $"{range} ({string.Format(ResString.vodAvailableConfigs, choices.Count)})".TrimStart();
        var videoLabels = choices.Select(c => c.Label).ToHashSet(StringComparer.Ordinal);
        return string.Join(" + ", labels.Where(l => !videoLabels.Contains(l)).Prepend(summary));
    }

    private static long ResolutionArea(string resolution)
    {
        var values = resolution.Split('x');
        return values.Length == 2 && int.TryParse(values[0], out var width) && int.TryParse(values[1], out var height)
            ? (long)width * height : 0;
    }

    private static IEnumerable<PartGroup> SplitByDuration(PartGroup group)
    {
        var durations = group.SectionDurations.Where(d => d > 0).Distinct().Order().ToList();
        // 同编码的插播和正文也可能无法区分。只有时长存在至少十倍的明显间隔，
        // 且长段至少一分钟时才分成两组；标签只描述时长，不推断广告，默认仍全部保留。
        var gap = Enumerable.Range(1, Math.Max(0, durations.Count - 1))
            .Where(i => durations[i] >= 60 && durations[i] / durations[i - 1] >= 10)
            .OrderByDescending(i => durations[i] / durations[i - 1]).FirstOrDefault();
        if (gap == 0)
            return [group];
        var boundary = durations[gap];
        bool[] lengths = [false, true];
        return lengths.Select(isLong =>
        {
            var indices = Enumerable.Range(0, group.Ids.Count)
                .Where(i => (group.SectionDurations[i] >= boundary) == isLong).ToList();
            var own = indices.Select(i => group.SectionDurations[i]).ToList();
            var range = own.Min() == own.Max() ? $"{own.Min():0.###}" : $"{own.Min():0.###}–{own.Max():0.###}";
            var split = new PartGroup(group.Configuration + " | " + string.Format(ResString.vodSectionDuration, range));
            split.Ids.AddRange(indices.Select(i => group.Ids[i]));
            split.SectionDurations.AddRange(own);
            split.Duration = own.Sum();
            return split;
        });
    }

    // 画质选择仍保留同分辨率的不同码率和命名轨道，仅去掉各 Period 中重复出现的选项。
    private static string QualityKey(StreamSpec stream) =>
        $"{ManifestConfiguration(stream, stream.Playlist?.MediaParts.FirstOrDefault() ?? new MediaPart()).Key}|{stream.Bandwidth}|{stream.Name}|{stream.Characteristics}";

    internal static List<StreamSpec> QualityChoices(List<StreamSpec> streams, List<StreamSpec>? sourceStreams = null)
    {
        var choices = streams.GroupBy(QualityKey)
            .Select(g => g.OrderByDescending(s => s.Playlist?.TotalDuration ?? 0).First()).ToList();
        var sources = sourceStreams ?? streams;
        foreach (var choice in choices)
        {
            // 默认关联使用的是 Period 内的 ID；去重后必须指向保留下来的配置代表。
            // 源列表还能解析被删 Period 的 ID，避免把另一语言的音轨当成默认音轨。
            choice.AudioId = Remap(choice.AudioId, MediaType.AUDIO);
            choice.SubtitleId = Remap(choice.SubtitleId, MediaType.SUBTITLES);
        }
        return choices;

        string? Remap(string? id, MediaType type)
        {
            if (id == null)
                return null;
            var tracks = choices.Where(s => s.MediaType == type).ToList();
            if (tracks.Any(s => s.GroupId == id))
                return id;
            var source = sources.FirstOrDefault(s => s.MediaType == type && s.GroupId == id);
            var target = source == null ? null : tracks.FirstOrDefault(s => QualityKey(s) == QualityKey(source)) ??
                tracks.Where(s => s.Language == source.Language && s.Role == source.Role)
                    .OrderByDescending(s => s.Bandwidth).FirstOrDefault();
            return (target ?? tracks.FirstOrDefault())?.GroupId;
        }
    }

    private static Configuration ManifestConfiguration(StreamSpec stream, MediaPart part)
    {
        var type = stream.MediaType ?? MediaType.VIDEO;
        string?[] values = [type == MediaType.AUDIO ? "Aud" : type == MediaType.SUBTITLES ? "Sub" : "Vid",
            part.Codecs ?? stream.Codecs, stream.Resolution,
            stream.Channels == null ? null : $"{stream.Channels}CH", stream.VideoRange,
            type is MediaType.AUDIO or MediaType.SUBTITLES ? stream.Language : null];
        var label = string.Join(" ", values.Where(s => !string.IsNullOrWhiteSpace(s)));
        return new Configuration($"{label}|{stream.FrameRate}|{stream.Language}|{stream.Role}", label);
    }

    [GeneratedRegex(@"\b\d+ Hz\b|\b(?:mono|stereo|\d+(?:\.\d+)?(?:\(side\))? channels?|\d+\.\d+(?:\([a-z]+\))?)(?=\s|,|$)", RegexOptions.IgnoreCase)]
    private static partial Regex AudioConfigurationRegex();

    internal static Configuration? ProbeConfiguration(List<Mediainfo> infos)
    {
        if (infos.Count == 0 || infos.Any(i => i.Type is not ("Video" or "Audio") || string.IsNullOrEmpty(i.BaseInfo)))
            return null;
        // init 不足以提供关键参数时标为未验证，不能用只有编码名的信息合并不同配置。
        if (infos.Any(i => i.Type == "Video" && string.IsNullOrEmpty(i.Resolution) ||
                           i.Type == "Audio" && AudioConfigurationRegex().Matches(i.Text ?? "").Count < 2))
            return null;
        var labels = infos.Select(i =>
        {
            string?[] values = [i.Type == "Audio" ? "Aud" : "Vid", i.BaseInfo, i.Resolution,
                i.Type == "Audio" ? string.Join(" ", AudioConfigurationRegex().Matches(i.Text ?? "").Select(m => m.Value)) : null,
                i.Fps, i.DolbyVison ? "DOVI" : i.HDR ? "HDR" : null];
            return string.Join(" ", values.Where(s => !string.IsNullOrWhiteSpace(s)));
        }).Order(StringComparer.Ordinal).ToList();
        var label = string.Join(" + ", labels.OrderBy(DisplayOrder).ThenBy(label => label, StringComparer.Ordinal));
        return new Configuration(string.Join(" + ", labels), label);
    }

    internal static async Task<List<PartGroup>> InspectGroupsAsync(List<StreamSpec> streams, MyOption options,
        bool inspectInit)
    {
        EnsureVod(streams);
        if (!inspectInit)
            return BuildGroups(streams);
        var temporary = Directory.CreateTempSubdirectory("vod-part-preview-").FullName;
        try
        {
            var previewOptions = options.Clone();
            previewOptions.DownloadRetryCount = 0;
            var downloader = new SimpleDownloader(new DownloaderConfig { MyOptions = previewOptions, DirPrefix = temporary });
            var probed = new ConcurrentDictionary<string, Configuration?>();
            var contentProbes = new ConcurrentDictionary<string, Lazy<Task<Configuration?>>>();
            var inits = streams.Where(s => s.MediaType != MediaType.SUBTITLES)
                         .SelectMany(s => s.Playlist!.MediaParts).Where(p => p.MediaSegments.Count > 0)
                         .Select(p => p.MediaInit).OfType<MediaSegment>().DistinctBy(VodInitCache.Identity).ToList();
            // 相同下载身份仅请求一次。不同身份用独立路径并发下载，避免大量广告 MAP 串行等待。
            // 最多同时处理四个 init，并遵守用户更低的线程数，避免一次启动几十个探测进程。
            await Parallel.ForEachAsync(inits, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(options.ThreadCount, 1, 4) }, async (init, _) =>
            {
                var identity = VodInitCache.Identity(init);
                var speed = new SpeedContainer();
                var result = await downloader.DownloadSegmentAsync(init, Path.Combine(temporary, identity + ".mp4.tmp"), speed, options.Headers);
                if (result is not { Success: true })
                {
                    probed[identity] = null;
                    return;
                }
                // 不同 URL 的 init 可能完全相同，按下载/解密后的完整内容复用探测结果。
                // Lazy 保证同时到达的相同内容也只启动一次 FFmpeg；下载身份仍包含范围和密钥。
                await using var file = File.OpenRead(result.ActualFilePath);
                var content = Convert.ToHexString(await SHA256.HashDataAsync(file));
                var probe = contentProbes.GetOrAdd(content, _ => new Lazy<Task<Configuration?>>(
                    async () => ProbeConfiguration(await MediainfoUtil.ReadInfoAsync(options.FFmpegBinaryPath!, result.ActualFilePath))));
                probed[identity] = await probe.Value;
            });
            return BuildGroups(streams, (stream, part) =>
            {
                var manifest = ManifestConfiguration(stream, part);
                if (part.MediaInit == null || stream.MediaType == MediaType.SUBTITLES)
                    return manifest;
                var identity = VodInitCache.Identity(part.MediaInit);
                if (probed.TryGetValue(identity, out var actual) && actual != null)
                    return actual with { Key = $"{actual.Key}|{stream.Language}|{stream.Role}" };
                // 无法验证的不同 init 不按 rendition 描述盲目合并，也不把 URL 显示给用户。
                return new Configuration($"{manifest.Key}|{identity}", $"{manifest.Label} ({ResString.vodConfigUnknown})");
            });
        }
        finally { Directory.Delete(temporary, true); }
    }

    public static async Task ListAsync(List<StreamSpec> streams, MyOption options, bool inspectInit = false)
    {
        var groups = await InspectGroupsAsync(streams, options, inspectInit);
        for (var i = 0; i < groups.Count; i++)
            Logger.Info($"{i + 1}. {groups[i].Display} | {ResString.vodPartIdsLabel}: {FormatIds(groups[i].Ids)}");
    }

    internal static string FormatIds(IEnumerable<long> ids)
    {
        var ranges = new List<string>();
        long? first = null, last = null;
        foreach (var id in ids.Distinct().Order())
        {
            if (first != null && last != long.MaxValue && id == last + 1)
            {
                last = id;
                continue;
            }
            if (first != null)
                ranges.Add(first == last ? $"{first}" : $"{first}-{last}");
            first = last = id;
        }
        if (first != null)
            ranges.Add(first == last ? $"{first}" : $"{first}-{last}");
        return string.Join(',', ranges);
    }

    internal static void KeepGroups(List<StreamSpec> streams, List<PartGroup> groups, List<PartGroup> selected)
    {
        if (selected.Count == 0)
            throw new ArgumentException(ResString.vodSelectAtLeastOne);
        var removed = groups.Except(selected).SelectMany(g => g.Ids).ToList();
        if (removed.Count > 0)
            Apply(streams, FormatIds(removed));
    }

    internal static List<StreamSpec> PreviewStreams(List<StreamSpec> streams, string[]? adKeywords)
    {
        if (adKeywords is not { Length: > 0 })
            return streams;
        // 预览在独立 part 列表上去广告，不能提前改变实际时间范围或源字幕时间轴。
        var preview = SnapshotStreams(streams);
        FilterUtil.CleanAd(preview, adKeywords, log: false);
        return preview;
    }

    internal static List<StreamSpec> SnapshotStreams(List<StreamSpec> streams) =>
        streams.Select(s => s.WithPlaylist(new Playlist {
            IsLive = s.Playlist!.IsLive,
            MediaParts = s.Playlist.MediaParts.Select(p => p.WithSegments([..p.MediaSegments])).ToList()
        })).ToList();

    public static async Task SelectAsync(List<StreamSpec> streams, MyOption options, bool inspectInit = false)
    {
        EnsureVod(streams);
        if (!HasMultipleSections(streams))
            return;
        if (Console.IsInputRedirected || !CustomAnsiConsole.Console.Profile.Capabilities.Interactive)
            throw new NotSupportedException(ResString.vodPartsRequireInteractive);
        var preview = PreviewStreams(streams, options.AdKeywords);
        var groups = inspectInit
            ? await CustomAnsiConsole.Console.Status().StartAsync(ResString.vodReadingConfigs,
                _ => InspectGroupsAsync(preview, options, inspectInit))
            : await InspectGroupsAsync(preview, options, inspectInit);
        if (groups.Count == 0)
            return;
        // 仅 init URL 改变而配置相同，无需用户重复确认；显式要求交互时仍展示唯一配置。
        if (groups.Count < 2 && options.VodSelectParts != true)
            return;
        var prompt = new MultiSelectionPrompt<PartGroup>()
            .Title(ResString.vodPromptTitle).UseConverter(g => g.Display.EscapeMarkup())
            .Required().PageSize(10).MoreChoicesText(ResString.promptChoiceText)
            .InstructionsText(ResString.vodPromptInfo);
        prompt.AddChoices(groups);
        foreach (var group in groups) prompt.Select(group);
        KeepGroups(streams, groups, CustomAnsiConsole.Console.Prompt(prompt));
    }

    private static void EnsureVod(List<StreamSpec> streams)
    {
        if (streams.Any(s => s.Playlist == null || s.Playlist.IsLive))
            throw new NotSupportedException(ResString.vodPartsRequireVod);
    }
}
