using System.Globalization;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util;

internal static class NetscapeCookieFile
{
    // DateTimeOffset 可表示的最大 Unix 秒数（9999-12-31T23:59:59Z）。
    private const long MaxUnixSeconds = 253402300799;
    // Chrome 内部时间戳为 1601-01-01 起的微秒数，部分导出工具会原样写入。
    private const long ChromeEpochOffsetSeconds = 11644473600;

    public static CookieFileJar Load(string path)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"{ResString.cookiesFileReadFailed}: {path}");
        }
        return Parse(lines);
    }

    internal static CookieFileJar Parse(string[] lines)
    {
        var cookies = new List<FileCookie>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var httpOnly = line.StartsWith("#HttpOnly_", StringComparison.Ordinal);
            if (httpOnly)
            {
                line = line[10..];
            }
            else if (line.StartsWith('#'))
            {
                continue;
            }

            // 七列以制表符分隔；保留最后一列的空值，不对 Cookie 值做 Trim 或解码。
            var fields = line.Split('\t');
            if (fields.Length != 7 || !bool.TryParse(fields[1], out var includeSubdomains) ||
                !bool.TryParse(fields[3], out var secure) || !fields[2].StartsWith('/') ||
                !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires))
            {
                // 报错只给行号，不包含 Cookie 内容。
                throw new FormatException(string.Format(CultureInfo.CurrentCulture, ResString.cookiesFileInvalidLine, index + 1));
            }

            var domain = fields[0].StartsWith('.') ? fields[0][1..] : fields[0];
            if (Uri.CheckHostName(domain) == UriHostNameType.Unknown || fields[5].Length == 0)
                throw new FormatException(string.Format(CultureInfo.CurrentCulture, ResString.cookiesFileInvalidLine, index + 1));

            // 无法放入 Cookie 请求头的条目（如值中含分号）只跳过该行，避免无关站点的 Cookie 导致整个文件不可用。
            if (!IsHeaderSafe(fields[5], isName: true) || !IsHeaderSafe(fields[6], isName: false))
            {
                Logger.Warn(string.Format(CultureInfo.CurrentCulture, ResString.cookiesFileSkippedLine, index + 1));
                continue;
            }

            // 0 表示会话 Cookie，负数或过去的时间表示已过期。
            DateTimeOffset? expiration = null;
            if (expires < 0)
                continue;
            if (expires > MaxUnixSeconds)
            {
                // 超出范围时先按 Chrome 时间戳换算；仍无法换算则视为不过期。
                var chromeSeconds = expires / 1_000_000 - ChromeEpochOffsetSeconds;
                expires = chromeSeconds is > 0 and <= MaxUnixSeconds ? chromeSeconds : 0;
            }
            if (expires != 0)
            {
                expiration = DateTimeOffset.FromUnixTimeSeconds(expires);
                if (expiration <= DateTimeOffset.UtcNow)
                    continue;
            }

            cookies.Add(new FileCookie(domain, !includeSubdomains, fields[2], secure, httpOnly, expiration, fields[5], fields[6]));
        }
        return new CookieFileJar(cookies);
    }

    private static bool IsHeaderSafe(string text, bool isName)
    {
        foreach (var c in text)
        {
            if (c == ';' || char.IsControl(c) || (isName && (c == '=' || char.IsWhiteSpace(c))))
                return false;
        }
        return true;
    }
}
