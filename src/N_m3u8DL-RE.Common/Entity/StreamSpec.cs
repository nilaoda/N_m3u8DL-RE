using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class StreamSpec
    {
        public MediaType? MediaType { get; set; }
        public string? GroupId { get; set; }
        public string? Language { get; set; }
        public string? Name { get; set; }
        public Choise? Default { get; set; }

        //由于用户选择 被跳过的分片总时长
        public double? SkippedDuration { get; set; }

        //MSS信息
        public MSSData? MSSData { get; set; }

        //基本信息
        public int? Bandwidth { get; set; }
        public string? Codecs { get; set; }
        public string? Resolution { get; set; }
        public double? FrameRate { get; set; }
        public string? Channels { get; set; }
        public string? Extension { get; set; }

        //Dash
        public RoleType? Role { get; set; }

        //补充信息-色域
        public string? VideoRange { get; set; }
        //补充信息-特征
        public string? Characteristics { get; set; }
        //发布时间（仅MPD需要）
        public DateTime? PublishTime { get; set; }

        //外部轨道GroupId (后续寻找对应轨道信息)
        public string? AudioId { get; set; }
        public string? VideoId { get; set; }
        public string? SubtitleId { get; set; }

        public string? PeriodId { get; set; }

        /// <summary>
        /// URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 原始URL
        /// </summary>
        public string OriginalUrl { get; set; }

        public Playlist? Playlist { get; set; }

        public int SegmentsCount
        {
            get
            {
                return Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) : 0;
            }
        }

        public string ToShortString()
        {
            var prefixStr = "";
            var returnStr = "";
            var encStr = string.Empty;

            if (MediaType == Enum.MediaType.AUDIO)
            {
                prefixStr = $"[deepskyblue3]Aud[/] {encStr}";
                var d = $"{GroupId} | {(Bandwidth != null ? (Bandwidth / 1000) + " Kbps" : "")} | {Name} | {Codecs} | {Language} | {(Channels != null ? Channels + "CH" : "")} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else if (MediaType == Enum.MediaType.SUBTITLES)
            {
                prefixStr = $"[deepskyblue3_1]Sub[/] {encStr}";
                var d = $"{GroupId} | {Language} | {Name} | {Codecs} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else
            {
                prefixStr = $"[aqua]Vid[/] {encStr}";
                var d = $"{Resolution} | {Bandwidth / 1000} Kbps | {GroupId} | {FrameRate} | {Codecs} | {VideoRange} | {Role}";
                returnStr = d.EscapeMarkup();
            }

            returnStr = prefixStr + returnStr.Trim().Trim('|').Trim();
            while (returnStr.Contains("|  |"))
            {
                returnStr = returnStr.Replace("|  |", "|");
            }

            return returnStr.TrimEnd().TrimEnd('|').TrimEnd();
        }

        public string ToShortShortString()
        {
            var prefixStr = "";
            var returnStr = "";
            var encStr = string.Empty;

            if (MediaType == Enum.MediaType.AUDIO)
            {
                prefixStr = $"[deepskyblue3]Aud[/] {encStr}";
                var d = $"{(Bandwidth != null ? (Bandwidth / 1000) + " Kbps" : "")} | {Name} | {Language} | {(Channels != null ? Channels + "CH" : "")} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else if (MediaType == Enum.MediaType.SUBTITLES)
            {
                prefixStr = $"[deepskyblue3_1]Sub[/] {encStr}";
                var d = $"{Language} | {Name} | {Codecs} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else
            {
                prefixStr = $"[aqua]Vid[/] {encStr}";
                var d = $"{Resolution} | {Bandwidth / 1000} Kbps | {FrameRate} | {VideoRange} | {Role}";
                returnStr = d.EscapeMarkup();
            }

            returnStr = prefixStr + returnStr.Trim().Trim('|').Trim();
            while (returnStr.Contains("|  |"))
            {
                returnStr = returnStr.Replace("|  |", "|");
            }

            return returnStr.TrimEnd().TrimEnd('|').TrimEnd();
        }

        public override string ToString()
        {
            var prefixStr = "";
            var returnStr = "";
            var encStr = string.Empty;
            var segmentsCountStr = SegmentsCount == 0 ? "" : (SegmentsCount > 1 ? $"{SegmentsCount} Segments" : $"{SegmentsCount} Segment");

            //增加加密标志
            if (Playlist != null && Playlist.MediaParts.Any(m => m.MediaSegments.Any(s => s.EncryptInfo.Method != EncryptMethod.NONE)))
            {
                var ms = Playlist.MediaParts.SelectMany(m => m.MediaSegments.Select(s => s.EncryptInfo.Method)).Where(e => e != EncryptMethod.NONE).Distinct();
                encStr = $"[red]*{string.Join(",", ms).EscapeMarkup()}[/] ";
            }

            if (MediaType == Enum.MediaType.AUDIO)
            {
                prefixStr = $"[deepskyblue3]Aud[/] {encStr}";
                var d = $"{GroupId} | {(Bandwidth != null ? (Bandwidth / 1000) + " Kbps" : "")} | {Name} | {Codecs} | {Language} | {(Channels != null ? Channels + "CH" : "")} | {segmentsCountStr} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else if (MediaType == Enum.MediaType.SUBTITLES)
            {
                prefixStr = $"[deepskyblue3_1]Sub[/] {encStr}";
                var d = $"{GroupId} | {Language} | {Name} | {Codecs} | {Characteristics} | {segmentsCountStr} | {Role}";
                returnStr = d.EscapeMarkup();
            }
            else
            {
                prefixStr = $"[aqua]Vid[/] {encStr}";
                var d = $"{Resolution} | {Bandwidth / 1000} Kbps | {GroupId} | {FrameRate} | {Codecs} | {VideoRange} | {segmentsCountStr} | {Role}";
                returnStr = d.EscapeMarkup();
            }

            returnStr = prefixStr + returnStr.Trim().Trim('|').Trim();
            while (returnStr.Contains("|  |"))
            {
                returnStr = returnStr.Replace("|  |", "|");
            }

            //计算时长
            if (Playlist != null)
            {
                var total = Playlist.TotalDuration;
                returnStr += " | ~" + GlobalUtil.FormatTime((int)total);
            }

            return returnStr.TrimEnd().TrimEnd('|').TrimEnd();
        }
    }
}
