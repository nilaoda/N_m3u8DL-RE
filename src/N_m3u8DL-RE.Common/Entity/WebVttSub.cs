﻿using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Common.Entity
{
    public partial class WebVttSub
    {
        [GeneratedRegex("X-TIMESTAMP-MAP.*")]
        private static partial Regex TSMapRegex();
        [GeneratedRegex("MPEGTS:(\\d+)")]
        private static partial Regex TSValueRegex();
        [GeneratedRegex("\\s")]
        private static partial Regex SplitRegex();
        [GeneratedRegex(@"<c\..*?>([\s\S]*?)<\/c>")]
        private static partial Regex VttClassRegex();

        public List<SubCue> Cues { get; set; } = [];
        public long MpegtsTimestamp { get; set; }

        /// <summary>
        /// 从字节数组解析WEBVTT
        /// </summary>
        /// <param name="textBytes"></param>
        /// <returns></returns>
        public static WebVttSub Parse(byte[] textBytes, long BaseTimestamp = 0L)
        {
            return Parse(Encoding.UTF8.GetString(textBytes), BaseTimestamp);
        }

        /// <summary>
        /// 从字节数组解析WEBVTT
        /// </summary>
        /// <param name="textBytes"></param>
        /// <param name="encoding"></param>
        /// <returns></returns>
        public static WebVttSub Parse(byte[] textBytes, Encoding encoding, long BaseTimestamp = 0L)
        {
            return Parse(encoding.GetString(textBytes), BaseTimestamp);
        }

        /// <summary>
        /// 从字符串解析WEBVTT
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>
        public static WebVttSub Parse(string text, long BaseTimestamp = 0L)
        {
            if (!text.Trim().StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException("Bad vtt!");
            }

            text += Environment.NewLine;

            WebVttSub webSub = new();
            bool needPayload = false;
            string timeLine = "";
            Match tsMapMatch = TSMapRegex().Match(text);

            if (tsMapMatch.Success)
            {
                string timestamp = TSValueRegex().Match(tsMapMatch.Value).Groups[1].Value;
                webSub.MpegtsTimestamp = Convert.ToInt64(timestamp, CultureInfo.InvariantCulture);
            }

            List<string> payloads = [];
            foreach (string line in text.Split('\n'))
            {
                if (line.Contains(" --> "))
                {
                    needPayload = true;
                    timeLine = line.Trim();
                    continue;
                }

                if (!needPayload)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(line.Trim()))
                {
                    string payload = string.Join(Environment.NewLine, payloads);
                    if (string.IsNullOrEmpty(payload.Trim()))
                    {
                        continue; // 没获取到payload 跳过添加
                    }

                    List<string> arr = [.. SplitRegex().Split(timeLine.Replace("-->", "")).Where(s => !string.IsNullOrEmpty(s))];
                    TimeSpan startTime = ConvertToTS(arr[0]);
                    TimeSpan endTime = ConvertToTS(arr[1]);
                    string style = arr.Count > 2 ? string.Join(" ", arr.Skip(2)) : "";
                    webSub.Cues.Add(new SubCue()
                    {
                        StartTime = startTime,
                        EndTime = endTime,
                        Payload = RemoveClassTag(string.Join("", payload.Where(c => c != 8203))), // Remove Zero Width Space!
                        Settings = style
                    });
                    payloads.Clear();
                    needPayload = false;
                }
                else
                {
                    payloads.Add(line.Trim());
                }
            }

            if (BaseTimestamp == 0)
            {
                return webSub;
            }

            foreach (SubCue item in webSub.Cues)
            {
                if (item.StartTime.TotalMilliseconds - BaseTimestamp >= 0)
                {
                    item.StartTime = TimeSpan.FromMilliseconds(item.StartTime.TotalMilliseconds - BaseTimestamp);
                    item.EndTime = TimeSpan.FromMilliseconds(item.EndTime.TotalMilliseconds - BaseTimestamp);
                }
                else
                {
                    break;
                }
            }

            return webSub;
        }

        private static string RemoveClassTag(string text)
        {
            return VttClassRegex().IsMatch(text)
                ? string.Join(Environment.NewLine, text.Split('\n').Select(line => line.TrimEnd()).Select(line =>
                {
                    return string.Concat(VttClassRegex().Matches(line).Select(x => x.Groups[1].Value + " "));
                })).TrimEnd()
                : text;
        }

        /// <summary>
        /// 从另一个字幕中获取所有Cue，并加载此字幕中，且自动修正偏移
        /// </summary>
        /// <param name="webSub"></param>
        /// <returns></returns>
        public WebVttSub AddCuesFromOne(WebVttSub webSub)
        {
            FixTimestamp(webSub, MpegtsTimestamp);
            foreach (SubCue item in webSub.Cues)
            {
                if (Cues.Contains(item))
                {
                    continue;
                }

                // 如果相差只有1ms，且payload相同，则拼接
                SubCue? last = Cues.LastOrDefault();
                if (last != null && Cues.Count > 0 && (item.StartTime - last.EndTime).TotalMilliseconds <= 1 && item.Payload == last.Payload)
                {
                    last.EndTime = item.EndTime;
                }
                else
                {
                    Cues.Add(item);
                }
            }
            return this;
        }

        private void FixTimestamp(WebVttSub sub, long baseTimestamp)
        {
            if (sub.MpegtsTimestamp == 0)
            {
                return;
            }

            // 确实存在时间轴错误的情况，才修复
            if ((Cues.Count > 0 && sub.Cues.Count > 0 && sub.Cues.First().StartTime < Cues.Last().EndTime && sub.Cues.First().EndTime != Cues.Last().EndTime) || Cues.Count == 0)
            {
                // The MPEG2 transport stream clocks (PCR, PTS, DTS) all have units of 1/90000 second
                long seconds = (sub.MpegtsTimestamp - baseTimestamp) / 90000;
                TimeSpan offset = TimeSpan.FromSeconds(seconds);
                // 当前预添加的字幕的起始时间小于实际上已经走过的时间(如offset已经是100秒，而字幕起始却是2秒)，才修复
                if (sub.Cues.Count > 0 && sub.Cues.First().StartTime < offset)
                {
                    foreach (SubCue subCue in sub.Cues)
                    {
                        subCue.StartTime += offset;
                        subCue.EndTime += offset;
                    }
                }
            }
        }

        private IEnumerable<SubCue> GetCues()
        {
            return Cues.Where(c => !string.IsNullOrEmpty(c.Payload));
        }

        private static TimeSpan ConvertToTS(string str)
        {
            // 17.0s
            if (str.EndsWith('s'))
            {
                double sec = Convert.ToDouble(str[..^1], CultureInfo.InvariantCulture);
                return TimeSpan.FromSeconds(sec);
            }

            str = str.Replace(',', '.');
            long time = 0;
            string[] parts = str.Split('.');
            if (parts.Length > 1)
            {
                time += Convert.ToInt32(parts.Last().PadRight(3, '0'), CultureInfo.InvariantCulture);
                str = parts.First();
            }
            List<string> t = [.. str.Split(':').Reverse()];
            for (int i = 0; i < t.Count; i++)
            {
                time += (long)Math.Pow(60, i) * Convert.ToInt32(t[i], CultureInfo.InvariantCulture) * 1000;
            }
            return TimeSpan.FromMilliseconds(time);
        }

        public override string ToString()
        {
            StringBuilder sb = new();
            foreach (SubCue c in GetCues())  // 输出时去除空串
            {
                _ = sb.AppendLine(c.StartTime.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) + " --> " + c.EndTime.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture) + " " + c.Settings);
                _ = sb.AppendLine(c.Payload);
                _ = sb.AppendLine();
            }
            _ = sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// 字幕向前平移指定时间
        /// </summary>
        /// <param name="time"></param>
        public void LeftShiftTime(TimeSpan time)
        {
            foreach (SubCue cue in Cues)
            {
                if (cue.StartTime.TotalSeconds - time.TotalSeconds > 0)
                {
                    cue.StartTime -= time;
                }
                else
                {
                    cue.StartTime = TimeSpan.FromSeconds(0);
                }

                if (cue.EndTime.TotalSeconds - time.TotalSeconds > 0)
                {
                    cue.EndTime -= time;
                }
                else
                {
                    cue.EndTime = TimeSpan.FromSeconds(0);
                }
            }
        }

        public string ToVtt()
        {
            return "WEBVTT" + Environment.NewLine + Environment.NewLine + ToString();
        }

        public string ToSrt()
        {
            StringBuilder sb = new();
            int index = 1;
            foreach (SubCue c in GetCues())
            {
                _ = sb.AppendLine(CultureInfo.InvariantCulture, $"{index++}");
                _ = sb.AppendLine(c.StartTime.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture) + " --> " + c.EndTime.ToString(@"hh\:mm\:ss\,fff", CultureInfo.InvariantCulture));
                _ = sb.AppendLine(c.Payload);
                _ = sb.AppendLine();
            }
            _ = sb.AppendLine();

            string srt = sb.ToString();

            if (string.IsNullOrEmpty(srt.Trim()))
            {
                srt = "1\r\n00:00:00,000 --> 00:00:01,000"; // 空字幕
            }

            return srt;
        }
    }
}