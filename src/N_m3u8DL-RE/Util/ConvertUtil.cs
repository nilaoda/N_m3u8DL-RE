using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Enum;
using System.Text;

namespace N_m3u8DL_RE.Util
{
    internal class ConvertUtil
    {
        public static Dictionary<string,string> SplitHeaderArrayToDic(string[]? headers)
        {
            Dictionary<string,string> dic = new();

            if (headers != null)
            {
                foreach (string header in headers)
                {
                    var index = header.IndexOf(':');
                    if (index != -1)
                    {
                        dic[header[..index].Trim().ToLower()] = header[(index + 1)..].Trim();
                    }
                }
            }

            return dic;
        }

        private static string WebVtt2Srt(WebVttSub vtt)
        {
            StringBuilder sb = new StringBuilder();
            int index = 1;
            foreach (var c in vtt.Cues)
            {
                sb.AppendLine($"{index++}");
                sb.AppendLine(c.StartTime.ToString(@"hh\:mm\:ss\,fff") + " --> " + c.EndTime.ToString(@"hh\:mm\:ss\,fff"));
                sb.AppendLine(c.Payload);
                sb.AppendLine();
            }
            sb.AppendLine();
            return sb.ToString();
        }

        public static string WebVtt2Other(WebVttSub vtt, SubtitleFormat toFormat)
        {
            Logger.Debug($"Convert {SubtitleFormat.VTT} ==> {toFormat}");
            return toFormat switch
            {
                SubtitleFormat.VTT => vtt.ToStringWithHeader(),
                SubtitleFormat.SRT => WebVtt2Srt(vtt),
                _ => throw new NotSupportedException($"{toFormat} not supported!")
            };
        }

        public static string GetValidFileName(string input, string re = ".", bool filterSlash = false)
        {
            string title = input;
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                title = title.Replace(invalidChar.ToString(), re);
            }
            if (filterSlash)
            {
                title = title.Replace("/", re);
                title = title.Replace("\\", re);
            }
            return title.Trim('.');
        }
    }
}
