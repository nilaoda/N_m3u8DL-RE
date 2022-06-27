using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class WebSub
    {
        public List<SubCue> Cues { get; set; } = new List<SubCue>();
        public long MpegtsTimestamp { get; set; } = 0L;

        /// <summary>
        /// 从字节数组解析WEBVTT
        /// </summary>
        /// <param name="textBytes"></param>
        /// <returns></returns>
        public static WebSub Parse(byte[] textBytes)
        {
            return Parse(Encoding.UTF8.GetString(textBytes));
        }

        /// <summary>
        /// 从字符串解析WEBVTT
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static WebSub Parse(string text)
        {
            if (!text.Trim().StartsWith("WEBVTT"))
                throw new Exception("Bad vtt!");


            var webSub = new WebSub();
            var needPayload = false;
            var timeLine = "";
            var regex1 = new Regex("X-TIMESTAMP-MAP.*");

            if (regex1.IsMatch(text))
            {
                var timestamp = Regex.Match(regex1.Match(text).Value, "MPEGTS:(\\d+)").Groups[1].Value;
                webSub.MpegtsTimestamp = Convert.ToInt64(timestamp);
            }

            foreach (var line in text.Split('\n'))
            {
                if (string.IsNullOrEmpty(line)) continue;

                if (!needPayload && line.Contains(" --> "))
                {
                    needPayload = true;
                    timeLine = line.Trim();
                    continue;
                }

                if (needPayload)
                {
                    var payload = line.Trim();
                    var arr = Regex.Split(timeLine.Replace("-->", ""), "\\s").Where(s => !string.IsNullOrEmpty(s)).ToList();
                    var startTime = ConvertToTS(arr[0]);
                    var endTime = ConvertToTS(arr[1]);
                    var style = arr.Count > 2 ? string.Join(" ", arr.Skip(2)) : "";
                    webSub.Cues.Add(new SubCue()
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Payload = payload,
                        Settings = style
                    });
                    needPayload = false;
                }
            }

            return webSub;
        }

        private static TimeSpan ConvertToTS(string str)
        {
            var ms = Convert.ToInt32(str.Split('.').Last());
            var o = str.Split('.').First();
            var t = o.Split(':').Reverse().ToList();
            var time = 0L + ms;
            for (int i = 0; i < t.Count(); i++)
            {
                time += (int)Math.Pow(60, i) * Convert.ToInt32(t[i]) * 1000;
            }
            return TimeSpan.FromMilliseconds(time);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach (var c in this.Cues)
            {
                sb.AppendLine(c.StartTime.ToString(@"hh\:mm\:ss\.fff") + " --> " + c.EndTime.ToString(@"hh\:mm\:ss\.fff") + " " + c.Settings);
                sb.AppendLine(c.Payload);
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
