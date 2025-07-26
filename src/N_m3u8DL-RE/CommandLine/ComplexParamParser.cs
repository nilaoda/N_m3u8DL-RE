using System.Text;

namespace N_m3u8DL_RE.CommandLine
{
    internal class ComplexParamParser(string arg)
    {
        private readonly string _arg = arg;

        public string? GetValue(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_arg)) return null;

            try
            {
                int index = _arg.IndexOf(key + "=", StringComparison.Ordinal);
                if (index == -1) return (_arg.Contains(key) && _arg.EndsWith(key)) ? "true" : null;

                char[] chars = _arg[(index + key.Length + 1)..].ToCharArray();
                StringBuilder result = new StringBuilder();
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

                string resultStr = result.ToString().Trim().Trim('\"').Trim('\'');

                // 不应该有引号出现
                if (resultStr.Contains('\"') || resultStr.Contains('\'')) throw new Exception();

                return resultStr;
            }
            catch (Exception)
            {
                throw new ArgumentException($"Parse Argument [{key}] failed!");
            }
        }
    }
}