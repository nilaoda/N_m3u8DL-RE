namespace N_m3u8DL_RE.Common.Util;

public static class HexUtil
{
    public static string BytesToHex(byte[] data, string split = "")
    {
        return BitConverter.ToString(data).Replace("-", split);
    }

    /// <summary>
    /// 判断是不是HEX字符串
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static bool TryParseHexString(string input, out byte[]? bytes)
    {
        bytes = null;
        input = input.ToUpper();
        if (input.StartsWith("0X"))
            input = input[2..];
        if (input.Length % 2 != 0)
            return false;
        if (input.Any(c => !"0123456789ABCDEF".Contains(c)))
            return false;
        bytes = HexToBytes(input);
        return true;
    }
    
    /// <summary>
    /// 判断是不是Base64字符串，允许省略末尾填充
    /// </summary>
    /// <param name="s">input</param>
    /// <param name="key">hex string</param>
    /// <returns></returns>
    public static bool TryParseBase64(string s, out string? key)
    {
        key = null;
        try
        {
            // 与 FromBase64String 一样忽略空白，只为完全省略填充的输入补齐末尾等号。
            s = s.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
            if (!s.Contains('=') && s.Length % 4 is 2 or 3)
                s = s.PadRight(s.Length + 4 - s.Length % 4, '=');
            key = BytesToHex(Convert.FromBase64String(s));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static byte[] HexToBytes(string hex)
    {
        var hexSpan = hex.AsSpan().Trim();
        if (hexSpan.StartsWith("0x") || hexSpan.StartsWith("0X"))
        {
            hexSpan = hexSpan[2..];
        }

        return Convert.FromHexString(hexSpan);
    }
}