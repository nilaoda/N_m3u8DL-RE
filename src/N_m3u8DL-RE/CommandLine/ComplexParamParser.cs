using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.CommandLine
{
    internal class ComplexParamParser
    {
        private string _arg;
        public ComplexParamParser(string arg)
        {
            _arg = arg;
        }

        public string? GetValue(string key)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(_arg)) return null;

            try
            {
                var index = _arg.IndexOf(key + "=");
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

                var resultStr = result.ToString().Trim().Trim('\"').Trim('\'');

                //不应该有引号出现
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
