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

        //基本信息
        public int? Bandwidth { get; set; }
        public string? Codecs { get; set; }
        public string? Resolution { get; set; }
        public double? FrameRate { get; set; }
        public string? Channels { get; set; }


        //外部轨道GroupId (后续寻找对应轨道信息)
        public string? AudioId { get; set; }
        public string? VideoId { get; set; }
        public string? SubtitleId { get; set; }

        public string Url { get; set; }

        public Playlist Playlist { get; set; }

        public override string ToString()
        {
            var prefixStr = "";
            var returnStr = "";
            var encStr = string.Empty;

            //增加加密标志
            if (Playlist != null && Playlist.MediaParts.Any(m => m.MediaSegments.Any(s => s.EncryptInfo.Method != EncryptMethod.NONE)))
            {
                var ms = Playlist.MediaParts.SelectMany(m => m.MediaSegments.Select(s => s.EncryptInfo.Method)).Where(e => e != EncryptMethod.NONE).Distinct();
                encStr = $"[red]*{string.Join(",", ms).EscapeMarkup()}[/] ";
            }

            if (MediaType == Enum.MediaType.AUDIO)
            {
                prefixStr = $"[deepskyblue3]Aud[/] {encStr}";
                var d = $"{GroupId} | {(Bandwidth != null ? (Bandwidth / 1000) + " Kbps" : "")} | {Name} | {Language} | {(Channels != null ? Channels + "CH" : "")} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}";
                returnStr = d.EscapeMarkup();
            }
            else if (MediaType == Enum.MediaType.SUBTITLES)
            {
                prefixStr = $"[deepskyblue3_1]Sub[/] {encStr}";
                var d = $"{GroupId} | {Language} | {Name} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}";
                returnStr = d.EscapeMarkup();
            }
            else
            {
                prefixStr = $"[aqua]Vid[/] {encStr}";
                var d = $"{Resolution} | {Bandwidth / 1000} Kbps | {GroupId} | {FrameRate} | {Codecs} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}";
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
                var total = Playlist.MediaParts.Sum(x => x.MediaSegments.Sum(m => m.Duration));
                returnStr += " | " + GlobalUtil.FormatTime((int)total);
            }

            return returnStr;
        }
    }
}
