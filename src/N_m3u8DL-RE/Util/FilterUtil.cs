using System.Text.RegularExpressions;

using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;

using Spectre.Console;

namespace N_m3u8DL_RE.Util
{
    public static class FilterUtil
    {
        public static List<StreamSpec> DoFilterKeep(IEnumerable<StreamSpec> lists, StreamFilter? filter)
        {
            if (filter == null)
            {
                return [];
            }

            IEnumerable<StreamSpec> inputs = lists.Where(_ => true);
            if (filter.GroupIdReg != null)
            {
                inputs = inputs.Where(i => i.GroupId != null && filter.GroupIdReg.IsMatch(i.GroupId));
            }

            if (filter.LanguageReg != null)
            {
                inputs = inputs.Where(i => i.Language != null && filter.LanguageReg.IsMatch(i.Language));
            }

            if (filter.NameReg != null)
            {
                inputs = inputs.Where(i => i.Name != null && filter.NameReg.IsMatch(i.Name));
            }

            if (filter.CodecsReg != null)
            {
                inputs = inputs.Where(i => i.Codecs != null && filter.CodecsReg.IsMatch(i.Codecs));
            }

            if (filter.ResolutionReg != null)
            {
                inputs = inputs.Where(i => i.Resolution != null && filter.ResolutionReg.IsMatch(i.Resolution));
            }

            if (filter.FrameRateReg != null)
            {
                inputs = inputs.Where(i => i.FrameRate != null && filter.FrameRateReg.IsMatch($"{i.FrameRate}"));
            }

            if (filter.ChannelsReg != null)
            {
                inputs = inputs.Where(i => i.Channels != null && filter.ChannelsReg.IsMatch(i.Channels));
            }

            if (filter.VideoRangeReg != null)
            {
                inputs = inputs.Where(i => i.VideoRange != null && filter.VideoRangeReg.IsMatch(i.VideoRange));
            }

            if (filter.UrlReg != null)
            {
                inputs = inputs.Where(i => i.Url != null && filter.UrlReg.IsMatch(i.Url));
            }

            if (filter.SegmentsMaxCount != null && inputs.All(i => i.SegmentsCount > 0))
            {
                inputs = inputs.Where(i => i.SegmentsCount < filter.SegmentsMaxCount);
            }

            if (filter.SegmentsMinCount != null && inputs.All(i => i.SegmentsCount > 0))
            {
                inputs = inputs.Where(i => i.SegmentsCount > filter.SegmentsMinCount);
            }

            if (filter.PlaylistMinDur != null)
            {
                inputs = inputs.Where(i => i.Playlist?.TotalDuration > filter.PlaylistMinDur);
            }

            if (filter.PlaylistMaxDur != null)
            {
                inputs = inputs.Where(i => i.Playlist?.TotalDuration < filter.PlaylistMaxDur);
            }

            if (filter.BandwidthMin != null)
            {
                inputs = inputs.Where(i => i.Bandwidth >= filter.BandwidthMin);
            }

            if (filter.BandwidthMax != null)
            {
                inputs = inputs.Where(i => i.Bandwidth <= filter.BandwidthMax);
            }

            if (filter.Role.HasValue)
            {
                inputs = inputs.Where(i => i.Role == filter.Role);
            }

            string bestNumberStr = filter.For.Replace("best", "");
            string worstNumberStr = filter.For.Replace("worst", "");

            if (filter.For == "best" && inputs.Any())
            {
                inputs = inputs.Take(1).ToList();
            }
            else if (filter.For == "worst" && inputs.Any())
            {
                inputs = inputs.TakeLast(1).ToList();
            }
            else if (int.TryParse(bestNumberStr, out int bestNumber) && inputs.Any())
            {
                inputs = inputs.Take(bestNumber).ToList();
            }
            else if (int.TryParse(worstNumberStr, out int worstNumber) && inputs.Any())
            {
                inputs = inputs.TakeLast(worstNumber).ToList();
            }

            return [.. inputs];
        }

        public static List<StreamSpec> DoFilterDrop(IEnumerable<StreamSpec> lists, StreamFilter? filter)
        {
            if (filter == null)
            {
                return [.. lists];
            }

            IEnumerable<StreamSpec> inputs = lists.Where(_ => true);
            List<StreamSpec> selected = DoFilterKeep(lists, filter);

            inputs = inputs.Where(i => selected.All(s => s.ToString() != i.ToString()));

            return [.. inputs];
        }

        public static List<StreamSpec> SelectStreams(IEnumerable<StreamSpec> lists)
        {
            List<StreamSpec> streamSpecs = [.. lists];
            if (streamSpecs.Count == 1)
            {
                return [.. streamSpecs];
            }

            // 基本流
            List<StreamSpec> basicStreams = [.. streamSpecs.Where(x => x.MediaType == null)];
            // 可选音频轨道
            List<StreamSpec> audios = [.. streamSpecs.Where(x => x.MediaType == MediaType.AUDIO)];
            // 可选字幕轨道
            List<StreamSpec> subs = [.. streamSpecs.Where(x => x.MediaType == MediaType.SUBTITLES)];

            MultiSelectionPrompt<StreamSpec> prompt = new MultiSelectionPrompt<StreamSpec>()
                    .Title(ResString.promptTitle)
                    .UseConverter(x =>
                    {
                        return x.Name != null && x.Name.StartsWith("__") ? $"[darkslategray1]{x.Name[2..]}[/]" : x.ToString().EscapeMarkup().RemoveMarkup();
                    })
                    .Required()
                    .PageSize(10)
                    .MoreChoicesText(ResString.promptChoiceText)
                    .InstructionsText(ResString.promptInfo)
                ;

            // 默认选中第一个
            StreamSpec first = streamSpecs.First();
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
            List<StreamSpec> selectedStreams = CustomAnsiConsole.Console.Prompt(prompt);

            return selectedStreams;
        }

        /// <summary>
        /// 直播使用。对齐各个轨道的起始。
        /// </summary>
        /// <param name="selectedSteams"></param>
        /// <param name="takeLastCount"></param>
        public static void SyncStreams(List<StreamSpec> selectedSteams, int takeLastCount = 15)
        {
            // 通过Date同步
            if (selectedSteams.All(x => x.Playlist!.MediaParts[0].MediaSegments.All(x => x.DateTime != null)))
            {
                DateTime? minDate = selectedSteams.Max(s => s.Playlist!.MediaParts[0].MediaSegments.Min(s => s.DateTime))!;
                foreach (StreamSpec item in selectedSteams)
                {
                    foreach (MediaPart part in item.Playlist!.MediaParts)
                    {
                        // 秒级同步 忽略毫秒
                        part.MediaSegments = [.. part.MediaSegments.Where(s => s.DateTime!.Value.Ticks / TimeSpan.TicksPerSecond >= minDate.Value.Ticks / TimeSpan.TicksPerSecond)];
                    }
                }
            }
            else // 通过index同步
            {
                long minIndex = selectedSteams.Max(s => s.Playlist!.MediaParts[0].MediaSegments.Min(s => s.Index));
                foreach (StreamSpec item in selectedSteams)
                {
                    foreach (MediaPart part in item.Playlist!.MediaParts)
                    {
                        part.MediaSegments = [.. part.MediaSegments.Where(s => s.Index >= minIndex)];
                    }
                }
            }

            // 取最新的N个分片
            if (selectedSteams.Any(x => x.Playlist!.MediaParts[0].MediaSegments.Count > takeLastCount))
            {
                int skipCount = selectedSteams.Min(x => x.Playlist!.MediaParts[0].MediaSegments.Count) - takeLastCount + 1;
                if (skipCount < 0)
                {
                    skipCount = 0;
                }

                foreach (StreamSpec item in selectedSteams)
                {
                    foreach (MediaPart part in item.Playlist!.MediaParts)
                    {
                        part.MediaSegments = [.. part.MediaSegments.Skip(skipCount)];
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
            if (customRange == null)
            {
                return;
            }

            Logger.InfoMarkUp($"{ResString.customRangeFound}[Cyan underline]{customRange.InputStr}[/]");
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.customRangeWarn}[/]");

            bool filterByIndex = customRange is { StartSegIndex: not null, EndSegIndex: not null };
            bool filterByTime = customRange is { StartSec: not null, EndSec: not null };

            if (!filterByIndex && !filterByTime)
            {
                Logger.ErrorMarkUp(ResString.customRangeInvalid);
                return;
            }

            foreach (StreamSpec stream in selectedSteams)
            {
                double skippedDur = 0d;
                if (stream.Playlist == null)
                {
                    continue;
                }

                foreach (MediaPart part in stream.Playlist.MediaParts)
                {
                    List<MediaSegment> newSegments;
                    if (filterByIndex)
                    {
                        newSegments = [.. part.MediaSegments.Where(seg => seg.Index >= customRange.StartSegIndex && seg.Index <= customRange.EndSegIndex)];
                    }
                    else
                    {
                        newSegments = [.. part.MediaSegments.Where(seg => stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).Where(x => x.Index < seg.Index).Sum(x => x.Duration) >= customRange.StartSec
                                                                      && stream.Playlist.MediaParts.SelectMany(p => p.MediaSegments).Where(x => x.Index < seg.Index).Sum(x => x.Duration) <= customRange.EndSec)];
                    }

                    if (newSegments.Count > 0)
                    {
                        skippedDur += part.MediaSegments.Where(seg => seg.Index < newSegments.First().Index).Sum(x => x.Duration);
                    }

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
            if (keywords == null)
            {
                return;
            }

            List<Regex> regList = [.. keywords.Select(s => new Regex(s))];
            foreach (Regex? reg in regList)
            {
                Logger.InfoMarkUp($"{ResString.customAdKeywordsFound}[Cyan underline]{reg}[/]");
            }

            foreach (StreamSpec stream in selectedSteams)
            {
                if (stream.Playlist == null)
                {
                    continue;
                }

                int countBefore = stream.SegmentsCount;

                foreach (MediaPart part in stream.Playlist.MediaParts)
                {
                    // 没有找到广告分片
                    if (part.MediaSegments.All(x => regList.All(reg => !reg.IsMatch(x.Url))))
                    {
                        continue;
                    }
                    // 找到广告分片 清理
                    part.MediaSegments = [.. part.MediaSegments.Where(x => regList.All(reg => !reg.IsMatch(x.Url)))];
                }

                // 清理已经为空的 part
                stream.Playlist.MediaParts = [.. stream.Playlist.MediaParts.Where(x => x.MediaSegments.Count > 0)];

                int countAfter = stream.SegmentsCount;

                if (countBefore != countAfter)
                {
                    Logger.WarnMarkUp("[grey]{} segments => {} segments[/]", countBefore, countAfter);
                }
            }
        }
    }
}