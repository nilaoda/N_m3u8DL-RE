using N_m3u8DL_RE.Parser.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Util
{
    public partial class ParserUtil
    {
        [GeneratedRegex("\\$Number%([^$]+)d\\$")]
        private static partial Regex VarsNumberRegex();

        /// <summary>
        /// 从以下文本中获取参数
        /// #EXT-X-STREAM-INF:PROGRAM-ID=1,BANDWIDTH=2149280,CODECS="mp4a.40.2,avc1.64001f",RESOLUTION=1280x720,NAME="720"
        /// </summary>
        /// <param name="line">等待被解析的一行文本</param>
        /// <param name="key">留空则获取第一个英文冒号后的全部字符</param>
        /// <returns></returns>
        public static string GetAttribute(string line, string key = "")
        {
            line = line.Trim();
            if (key == "")
                return line[(line.IndexOf(':') + 1)..];

            var index = -1;
            var result = string.Empty;
            if ((index = line.IndexOf(key + "=\"")) > -1)
            {
                var startIndex = index + (key + "=\"").Length;
                var endIndex = startIndex + line[startIndex..].IndexOf('\"');
                result = line[startIndex..endIndex];
            }
            else if ((index = line.IndexOf(key + "=")) > -1)
            {
                var startIndex = index + (key + "=").Length;
                var endIndex = startIndex + line[startIndex..].IndexOf(',');
                if (endIndex >= startIndex) result = line[startIndex..endIndex];
                else result = line[startIndex..];
            }

            return result;
        }

        /// <summary>
        /// 从如下文本中提取
        /// <n>[@<o>]
        /// </summary>
        /// <param name="input"></param>
        /// <returns>n(length) o(start)</returns>
        public static (long, long?) GetRange(string input)
        {
            var t = input.Split('@');
            if (t.Length > 0)
            {
                if (t.Length == 1)
                {
                    return (Convert.ToInt64(t[0]), null);
                }
                if (t.Length == 2)
                {
                    return (Convert.ToInt64(t[0]), Convert.ToInt64(t[1]));
                }
            }
            return (0, null);
        }

        /// <summary>
        /// 从100-300这种字符串中获取StartRange, ExpectLength信息
        /// </summary>
        /// <param name="range"></param>
        /// <returns>StartRange, ExpectLength</returns>
        public static (long, long) ParseRange(string range)
        {
            var start = Convert.ToInt64(range.Split('-')[0]);
            var end = Convert.ToInt64(range.Split('-')[1]);
            return (start, end - start + 1);
        }

        /// <summary>
        /// MPD SegmentTemplate替换
        /// </summary>
        /// <param name="text"></param>
        /// <param name="keyValuePairs"></param>
        /// <returns></returns>
        public static string ReplaceVars(string text, Dictionary<string, object?> keyValuePairs)
        {
            foreach (var item in keyValuePairs)
                if (text.Contains(item.Key))
                    text = text.Replace(item.Key, item.Value!.ToString());

            //处理特殊形式数字 如 $Number%05d$
            var regex = VarsNumberRegex();
            if (regex.IsMatch(text) && keyValuePairs.ContainsKey(DASHTags.TemplateNumber)) 
            {
                foreach (Match m in regex.Matches(text))
                {
                    text = text.Replace(m.Value, keyValuePairs[DASHTags.TemplateNumber]?.ToString()?.PadLeft(Convert.ToInt32(m.Groups[1].Value), '0'));
                }
            }

            return text;
        }

        /// <summary>
        /// 拼接Baseurl和RelativeUrl
        /// </summary>
        /// <param name="baseurl">Baseurl</param>
        /// <param name="url">RelativeUrl</param>
        /// <returns></returns>
        public static string CombineURL(string baseurl, string url)
        {
            if (string.IsNullOrEmpty(baseurl))
                return url;

            Uri uri1 = new Uri(baseurl);  //这里直接传完整的URL即可
            Uri uri2 = new Uri(uri1, url);
            url = uri2.ToString();

            return url;
        }
    }
}
