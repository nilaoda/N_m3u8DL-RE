using N_m3u8DL_RE.Common.Enum;
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
            var encStr = string.Empty;

            //增加加密标志
            if (Playlist != null && Playlist.MediaParts.Any(m => m.MediaSegments.Any(s => s.EncryptInfo.Method != EncryptMethod.NONE)))
            {
                encStr = "[red]*[/] ";
            }

            if (MediaType == Enum.MediaType.AUDIO)
            {
                var d = $"{GroupId} | {Name} | {Language} | {(Channels != null ? Channels + "CH" : "")} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}".Replace("|  |", "|");
                return $"[deepskyblue3]Aud[/] {encStr}" + d.EscapeMarkup().Trim().Trim('|').Trim();
            }
            else if (MediaType == Enum.MediaType.SUBTITLES)
            {
                var d = $"{GroupId} | {Language} | {Name} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}".Replace("|  |", "|");
                return $"[deepskyblue3_1]Sub[/] {encStr}" + d.EscapeMarkup().Trim().Trim('|').Trim();
            }
            else
            {
                var d = $"{Resolution} | {Bandwidth / 1000} Kbps | {FrameRate} | {Codecs} | {(Playlist != null ? Playlist.MediaParts.Sum(x => x.MediaSegments.Count) + " Segments" : "")}".Replace("|  |", "|");
                return $"[aqua]Vid[/] {encStr}" + d.EscapeMarkup().Trim().Trim('|').Trim();
            }
        }
    }
}
