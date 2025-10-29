﻿using N_m3u8DL_RE.Common.Entity;
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
            inputs = inputs.Where(i => i.Role == filter.Role);

        var bestNumberStr = filter.For.Replace("best", "");
        var worstNumberStr = filter.For.Replace("worst", "");

        if (filter.For == "best" && inputs.Any())
            inputs = inputs.Take(1).ToList();
        else if (filter.For == "worst" && inputs.Any())
            inputs = inputs.TakeLast(1).ToList();
        else if (int.TryParse(bestNumberStr, out int bestNumber) && inputs.Any())
            inputs = inputs.Take(bestNumber).ToList();
        else if (int.TryParse(worstNumberStr, out int worstNumber) && inputs.Any())
            inputs = inputs.TakeLast(worstNumber).ToList();

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
                prompt.Select(audios.First(a => a.GroupId == first.AudioId));
            }
        }
        if (subs.Count != 0)
        {
            prompt.AddChoiceGroup(new StreamSpec() { Name = "__Subtitle" }, subs);
            // 默认字幕轨
            if (first.SubtitleId != null)
            {
                prompt.Select(subs.First(s => s.GroupId == first.SubtitleId));
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

        Logger.InfoMarkUp($"{ResString.customRangeFound}[Cyan underline]{customRange.InputStr}[/]");
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
            var skippedDur = 0d;
            if (stream.Playlist == null) continue;
            foreach (var part in stream.Playlist.MediaParts)
            {
                List<MediaSegment> newSegments;
                if (filterByIndex)
                    newSegments = part.MediaSegments.Where(seg => seg.Index >= customRange.StartSegIndex && seg.Index <= customRange.EndSegIndex).ToList();
                else
                    newSegments = part.MediaSegments.Where(seg => stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).Where(x => x.Index < seg.Index).Sum(x => x.Duration) >= customRange.StartSec
                                                                  && stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).Where(x => x.Index < seg.Index).Sum(x => x.Duration) <= customRange.EndSec).ToList();

                if (newSegments.Count > 0)
                    skippedDur += part.MediaSegments.Where(seg => seg.Index < newSegments.First().Index).Sum(x => x.Duration);
                part.MediaSegments = newSegments;
            }
            stream.SkippedDuration = skippedDur;
        }
    }

    /// <summary>
    /// 根据用户输入，清除广告分片
    /// </summary>
    /// <param name="selectedSteams"></param>
    /// <param name="keywords"></param>
    public static void CleanAd(List<StreamSpec> selectedSteams, string[]? keywords)
    {
        if (keywords == null) return;
        var regList = keywords.Select(s => new Regex(s)).ToList();
        foreach ( var reg in regList)
        {
            Logger.InfoMarkUp($"{ResString.customAdKeywordsFound}[Cyan underline]{reg}[/]");
        }

        foreach (var stream in selectedSteams)
        {
            if (stream.Playlist == null) continue;

            var countBefore = stream.SegmentsCount;

            foreach (var part in stream.Playlist.MediaParts)
            {
                // 没有找到广告分片
                if (part.MediaSegments.All(x => regList.All(reg => !reg.IsMatch(x.Url))))
                {
                    continue;
                }
                // 找到广告分片 清理
                part.MediaSegments = part.MediaSegments.Where(x => regList.All(reg => !reg.IsMatch(x.Url))).ToList();
            }

            // 清理已经为空的 part
            stream.Playlist.MediaParts = stream.Playlist.MediaParts.Where(x => x.MediaSegments.Count > 0).ToList();

            var countAfter = stream.SegmentsCount;

            if (countBefore != countAfter)
            {
                Logger.WarnMarkUp("[grey]{} segments => {} segments[/]", countBefore, countAfter);
            }
        }
    }
}