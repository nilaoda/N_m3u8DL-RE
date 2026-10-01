using System.Globalization;
using System.Net;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util;

internal static class NetscapeCookieFile
{
    public static CookieContainer Load(string path)
    {
        try
        {
            return Parse(File.ReadAllLines(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new IOException($"{ResString.cookiesFileReadFailed}: {path}");
        }
    }

    internal static CookieContainer Parse(string[] lines)
    {
        // 浏览器导出的文件可能包含数百个同域 Cookie，不能使用容器默认的每域 20 个限制。
        var cookies = new CookieContainer(int.MaxValue, int.MaxValue, int.MaxValue);
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

            try
            {
                // 七列以制表符分隔；保留最后一列的空值，不对 Cookie 值做 Trim 或解码。
                var fields = line.Split('\t');
                if (fields.Length != 7 || !bool.TryParse(fields[1], out var includeSubdomains) ||
                    !bool.TryParse(fields[3], out var secure) || !fields[2].StartsWith('/') ||
                    !long.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expires))
                {
                    throw new FormatException();
                }

                var domain = fields[0].StartsWith('.') ? fields[0][1..] : fields[0];
                if (Uri.CheckHostName(domain) == UriHostNameType.Unknown || fields[5].Length == 0)
                    throw new FormatException();

                var cookie = new Cookie(fields[5], fields[6], fields[2])
                {
                    Secure = secure,
                    HttpOnly = httpOnly
                };
                // 0 表示会话 Cookie，负数或过去的时间表示已过期。
                if (expires != 0)
                {
                    var expiration = DateTimeOffset.FromUnixTimeSeconds(expires);
                    if (expiration <= DateTimeOffset.UtcNow)
                        continue;
                    cookie.Expires = expiration.UtcDateTime;
                }

                var origin = new UriBuilder(secure ? "https" : "http", domain).Uri;
                if (includeSubdomains)
                {
                    cookie.Domain = "." + domain;
                }
                // 不设置 Domain，再通过源 URL 添加，才能保留仅当前主机有效的语义。
                cookies.Add(origin, cookie);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or CookieException)
            {
                // CookieException 可能包含 Cookie 值，报错只给行号，也不保留原始异常。
                throw new FormatException(string.Format(CultureInfo.CurrentCulture, ResString.cookiesFileInvalidLine, index + 1));
            }
        }
        return cookies;
    }
}
