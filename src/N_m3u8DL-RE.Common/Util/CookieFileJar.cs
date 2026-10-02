using System.Net;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Common.Util;

internal sealed record FileCookie(string Domain, bool HostOnly, string Path, bool Secure, bool HttpOnly,
    DateTimeOffset? Expires, string Name, string Value)
{
    /// <summary>RFC 6265 中 Cookie 的唯一标识：名称、域（不含前导点，忽略大小写）和路径。</summary>
    internal (string Name, string Domain, string Path) Key => CookieFileJar.Key(Name, Domain, Path);
}

/// <summary>
/// Cookie 文件中的条目自行匹配，不经过 System.Net.Cookie：后者拒绝值中含逗号的 Cookie（如 JSON），
/// 而浏览器导出的文件中很常见。服务器返回的 Set-Cookie 仍交给 CookieContainer 解析。
/// </summary>
internal sealed partial class CookieFileJar(List<FileCookie> fileCookies)
{
    // 把过期时间替换为未来时间，使被删除的 Cookie 也保留在临时容器中，以便取得其标识。
    private const string FutureExpires = "Fri, 01 Jan 2100 00:00:00 GMT";
    private const string FutureMaxAge = "86400";

    private readonly Lock _lock = new();
    // 浏览器导出的文件可能包含数百个同域 Cookie，不能使用容器默认的每域 20 个限制。
    private readonly CookieContainer _serverCookies = NewContainer();

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
        lock (_lock)
        {
            var file = fileCookies.Where(c => Matches(c, uri, now)).Select(c => (c.Path, Text: $"{c.Name}={c.Value}"));
            var server = _serverCookies.GetCookies(uri).Select(c => (c.Path, Text: $"{c.Name}={c.Value}"));
            // RFC 6265：路径更长的 Cookie 排在前面，同长度时先创建的（文件中的）在前。
            return string.Join("; ", file.Concat(server).OrderByDescending(c => c.Path.Length).Select(c => c.Text));
        }
    }

    public void SetCookies(Uri uri, string setCookieHeader)
    {
        lock (_lock)
        {
            // 先交给容器校验，无效时抛出 CookieException 且不影响文件中的 Cookie。
            _serverCookies.SetCookies(uri, setCookieHeader);
            if (fileCookies.Count == 0)
                return;

            // 一个 Set-Cookie 值可能包含多个 Cookie；服务器更新或删除的每个 Cookie 都替换文件中标识相同的条目。
            var probe = NewContainer();
            probe.SetCookies(uri, WithoutExpiration(setCookieHeader));
            var updated = probe.GetAllCookies().Select(c => Key(c.Name, c.Domain, c.Path)).ToHashSet();
            fileCookies.RemoveAll(c => updated.Contains(c.Key));
        }
    }

    internal static (string Name, string Domain, string Path) Key(string name, string domain, string path) =>
        (name, domain.TrimStart('.').ToLowerInvariant(), path);

    private static CookieContainer NewContainer() => new(int.MaxValue, int.MaxValue, int.MaxValue);

    private static string WithoutExpiration(string setCookieHeader)
    {
        var header = ExpiresRegex().Replace(setCookieHeader, "${1}" + FutureExpires);
        return MaxAgeRegex().Replace(header, "${1}" + FutureMaxAge);
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

    // Expires 的值可能以星期加逗号开头（如 "Wed, 21 Oct 2015 07:28:00 GMT"），逗号之后才是日期。
    [GeneratedRegex(@"(;\s*expires\s*=)\s*(?:[a-z]+\s*,)?[^;,]*", RegexOptions.IgnoreCase)]
    private static partial Regex ExpiresRegex();

    [GeneratedRegex(@"(;\s*max-age\s*=)[^;,]*", RegexOptions.IgnoreCase)]
    private static partial Regex MaxAgeRegex();
}
