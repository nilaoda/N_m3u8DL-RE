using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Util
{
    internal class ParserUtil
    {
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
                return line.Substring(line.IndexOf(':') + 1);

            if (line.Contains(key + "=\""))
            {
                return Regex.Match(line, key + "=\"([^\"]*)\"").Groups[1].Value;
            }
            else if (line.Contains(key + "="))
            {
                return Regex.Match(line, key + "=([^,]*)").Groups[1].Value;
            }

            return string.Empty;
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
