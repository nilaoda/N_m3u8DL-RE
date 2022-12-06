using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    public class FilterUtil
    {
        public static List<StreamSpec> DoFilterKeep(IEnumerable<StreamSpec> lists, StreamFilter? filter)
        {
            if (filter == null) return new List<StreamSpec>();

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

            var bestNumberStr = filter.For.Replace("best", "");
            var worstNumberStr = filter.For.Replace("worst", "");

            if (filter.For == "best" && inputs.Count() > 0)
                inputs = inputs.Take(1).ToList();
            else if (filter.For == "worst" && inputs.Count() > 0)
                inputs = inputs.TakeLast(1).ToList();
            else if (int.TryParse(bestNumberStr, out int bestNumber) && inputs.Count() > 0)
                inputs = inputs.Take(bestNumber).ToList();
            else if (int.TryParse(worstNumberStr, out int worstNumber) && inputs.Count() > 0)
                inputs = inputs.TakeLast(worstNumber).ToList();

            return inputs.ToList();
        }

        public static List<StreamSpec> DoFilterDrop(IEnumerable<StreamSpec> lists, StreamFilter? filter)
        {
            if (filter == null) return new List<StreamSpec>(lists);

            var inputs = lists.Where(_ => true);
            var selected = DoFilterKeep(lists, filter);

            inputs = inputs.Where(i => selected.All(s => s.ToString() != i.ToString()));

            return inputs.ToList();
        }

        public static List<StreamSpec> SelectStreams(IEnumerable<StreamSpec> lists)
        {
            if (lists.Count() == 1)
                return new List<StreamSpec>(lists);

            //基本流
            var basicStreams = lists.Where(x => x.MediaType == null);
            //可选音频轨道
            var audios = lists.Where(x => x.MediaType == MediaType.AUDIO);
            //可选字幕轨道
            var subs = lists.Where(x => x.MediaType == MediaType.SUBTITLES);

            var prompt = new MultiSelectionPrompt<StreamSpec>()
                        .Title(ResString.promptTitle)
                        .UseConverter(x =>
                        {
                            if (x.Name != null && x.Name.StartsWith("__"))
                                return $"[darkslategray1]{x.Name.Substring(2)}[/]";
                            else
                                return x.ToString().EscapeMarkup().RemoveMarkup();
                        })
                        .Required()
                        .PageSize(10)
                        .MoreChoicesText(ResString.promptChoiceText)
                        .InstructionsText(ResString.promptInfo)
                        ;

            //默认选中第一个
            var first = lists.First();
            prompt.Select(first);

            if (basicStreams.Any())
            {
                prompt.AddChoiceGroup(new StreamSpec() { Name = "__Basic" }, basicStreams);
            }

            if (audios.Any())
            {
                prompt.AddChoiceGroup(new StreamSpec() { Name = "__Audio" }, audios);
                //默认音轨
                if (first.AudioId != null)
                {
                    prompt.Select(audios.First(a => a.GroupId == first.AudioId));
                }
            }
            if (subs.Any())
            {
                prompt.AddChoiceGroup(new StreamSpec() { Name = "__Subtitle" }, subs);
                //默认字幕轨
                if (first.SubtitleId != null)
                {
                    prompt.Select(subs.First(s => s.GroupId == first.SubtitleId));
                }
            }

            //如果此时还是没有选中任何流，自动选择一个
            prompt.Select(basicStreams.Concat(audios).Concat(subs).First());

            //多选
            var selectedStreams = AnsiConsole.Prompt(prompt);

            return selectedStreams;
        }
    }
}
