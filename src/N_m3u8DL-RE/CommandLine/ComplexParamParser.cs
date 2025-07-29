using System.Text;

namespace N_m3u8DL_RE.CommandLine
{
    internal sealed class ComplexParamParser(string arg)
    {
        private readonly string _arg = arg;

        public string? GetValue(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentNullException(nameof(key), "Key cannot be null or empty.");
            }

            if (string.IsNullOrEmpty(_arg))
            {
                return null;
            }

            int index = _arg.IndexOf(key + "=", StringComparison.Ordinal);
            if (index == -1)
            {
                // Allow key to be interpreted as boolean flag (e.g., --flag instead of --flag=true)
                return (_arg.Contains(key) && _arg.EndsWith(key)) ? "true" : null;
            }

            try
            {
                char[] chars = _arg[(index + key.Length + 1)..].ToCharArray();
                StringBuilder result = new();
                char last = '\0';
                for (int i = 0; i < chars.Length; i++)
                {
                    if (chars[i] == ':')
                    {
                        if (last == '\\')
                        {
                            result.Replace("\\", "");  // unescape
                            result.Append(':');
                        }
                        else
                        {
                            break;
                        }
                    }
                    else
                    {
                        result.Append(chars[i]);
                    }
                    last = chars[i];
                }

                string resultStr = result.ToString().Trim().Trim('"').Trim('\'');

                return resultStr.Contains('"') || resultStr.Contains('\'')
                    ? throw new FormatException($"Unexpected quote in value for key '{key}': {resultStr}")
                    : resultStr;
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new FormatException($"Failed to parse argument value for key '{key}'.", ex);
            }
        }
    }
}
