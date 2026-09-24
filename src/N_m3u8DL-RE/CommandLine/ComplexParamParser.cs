using System.Text;

namespace N_m3u8DL_RE.CommandLine;

internal class ComplexParamParser
{
    private readonly string _arg;
    public ComplexParamParser(string arg)
    {
        _arg = arg;
    }

    public string? GetValue(string key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_arg)) return null;

        try
        {
            var index = _arg.IndexOf(key + "=", StringComparison.Ordinal);
            if (index == -1) return (_arg.Contains(key) && _arg.EndsWith(key)) ? "true" : null;

            var chars = _arg[(index + key.Length + 1)..].ToCharArray();
            var result = new StringBuilder();
            char last = '\0';
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == ':')
                {
                    if (last == '\\')
                    {
                        result.Replace("\\", "");
                        last = chars[i];
                        result.Append(chars[i]);
                    }
                    else break;
                }
                else
                {
                    last = chars[i];
                    result.Append(chars[i]);
                }
            }

            var resultStr = result.ToString().Trim();

            // 仅去除成对的首尾引号, 保留值内部的引号(例如文件名中的撇号: What's Next)
            if (resultStr.Length >= 2
                && (resultStr[0] == '\"' || resultStr[0] == '\'')
                && resultStr[^1] == resultStr[0])
            {
                resultStr = resultStr[1..^1];
            }

            return resultStr;
        }
        catch (Exception)
        {
            throw new ArgumentException($"Parse Argument [{key}] failed!");
        }
    }
}