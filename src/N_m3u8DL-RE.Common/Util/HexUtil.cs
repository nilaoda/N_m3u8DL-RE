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
    /// 判断是不是Base64字符串
    /// </summary>
    /// <param name="s">input</param>
    /// <param name="key">hex string</param>
    /// <returns></returns>
    public static bool TryParseBase64(string s, out string? key)
    {
        key = null;
        try
        {
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