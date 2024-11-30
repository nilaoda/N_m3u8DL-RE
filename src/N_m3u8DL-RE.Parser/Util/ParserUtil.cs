using N_m3u8DL_RE.Parser.Constants;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Parser.Util;

public static partial class ParserUtil
{
    [GeneratedRegex(@"\$Number%([^$]+)d\$")]
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
        if ((index = line.IndexOf(key + "=\"", StringComparison.Ordinal)) > -1)
        {
            var startIndex = index + (key + "=\"").Length;
            var endIndex = startIndex + line[startIndex..].IndexOf('\"');
            result = line[startIndex..endIndex];
        }
        else if ((index = line.IndexOf(key + "=", StringComparison.Ordinal)) > -1)
        {
            var startIndex = index + (key + "=").Length;
            var endIndex = startIndex + line[startIndex..].IndexOf(',');
            result = endIndex >= startIndex ? line[startIndex..endIndex] : line[startIndex..];
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
        return t.Length switch
        {
            <= 0 => (0, null),
            1 => (Convert.ToInt64(t[0]), null),
            2 => (Convert.ToInt64(t[0]), Convert.ToInt64(t[1])),
            _ => (0, null)
        };
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

        // 处理特殊形式数字 如 $Number%05d$
        var regex = VarsNumberRegex();
        if (regex.IsMatch(text) && keyValuePairs.TryGetValue(DASHTags.TemplateNumber, out var keyValuePair)) 
        {
            foreach (Match m in regex.Matches(text))
            {
                text = text.Replace(m.Value, keyValuePair?.ToString()?.PadLeft(Convert.ToInt32(m.Groups[1].Value), '0'));
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

        var uri1 = new Uri(baseurl);  // 这里直接传完整的URL即可
        var uri2 = new Uri(uri1, url);
        url = uri2.ToString();

        return url;
    }
}