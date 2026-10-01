using System.Net;

namespace N_m3u8DL_RE.Common.Util;

internal sealed record FileCookie(string Domain, bool HostOnly, string Path, bool Secure, bool HttpOnly,
    DateTimeOffset? Expires, string Name, string Value);

/// <summary>
/// Cookie 文件中的条目自行匹配，不经过 System.Net.Cookie：后者拒绝值中含逗号的 Cookie（如 JSON），
/// 而浏览器导出的文件中很常见。服务器返回的 Set-Cookie 仍交给 CookieContainer 解析。
/// </summary>
internal sealed class CookieFileJar(List<FileCookie> fileCookies)
{
    private readonly Lock _lock = new();
    // 浏览器导出的文件可能包含数百个同域 Cookie，不能使用容器默认的每域 20 个限制。
    private readonly CookieContainer _serverCookies = new(int.MaxValue, int.MaxValue, int.MaxValue);

    internal IReadOnlyList<FileCookie> FileCookies
    {
        get
        {
            lock (_lock) return fileCookies.ToList();
        }
    }

    public string GetCookieHeader(Uri uri)
    {
        var now = DateTimeOffset.UtcNow;
        var parts = new List<string>();
        lock (_lock)
        {
            // RFC 6265：路径更长的 Cookie 排在前面。
            foreach (var cookie in fileCookies.Where(c => Matches(c, uri, now)).OrderByDescending(c => c.Path.Length))
                parts.Add($"{cookie.Name}={cookie.Value}");
            var server = _serverCookies.GetCookieHeader(uri);
            if (server.Length > 0)
                parts.Add(server);
        }
        return string.Join("; ", parts);
    }

    public void SetCookies(Uri uri, string setCookieHeader)
    {
        lock (_lock)
        {
            // 先交给容器校验，无效时抛出 CookieException 且不影响文件中的 Cookie。
            _serverCookies.SetCookies(uri, setCookieHeader);
            // 服务器更新或删除同名 Cookie 后，文件中的旧值不再发送。
            var name = setCookieHeader.Split('=', 2)[0].Trim();
            fileCookies.RemoveAll(c => c.Name == name && DomainMatches(c, uri.Host));
        }
    }

    private static bool Matches(FileCookie cookie, Uri uri, DateTimeOffset now)
    {
        if (cookie.Expires is { } expires && expires <= now)
            return false;
        if (cookie.Secure && uri.Scheme != Uri.UriSchemeHttps)
            return false;
        return DomainMatches(cookie, uri.Host) && PathMatches(cookie.Path, uri.AbsolutePath);
    }

    private static bool DomainMatches(FileCookie cookie, string host)
    {
        if (string.Equals(host, cookie.Domain, StringComparison.OrdinalIgnoreCase))
            return true;
        return !cookie.HostOnly && host.EndsWith("." + cookie.Domain, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathMatches(string cookiePath, string requestPath)
    {
        if (requestPath.Length == 0)
            requestPath = "/";
        if (!requestPath.StartsWith(cookiePath, StringComparison.Ordinal))
            return false;
        return requestPath.Length == cookiePath.Length || cookiePath.EndsWith('/') || requestPath[cookiePath.Length] == '/';
    }
}
