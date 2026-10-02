using System.Text;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class NetscapeCookieFileTests
{
    [Fact]
    public void ScopeExpirationAndEmptyValuesArePreserved()
    {
        var cookies = NetscapeCookieFile.Parse(
        [
            "# Netscape HTTP Cookie File",
            "",
            "example.com\tFALSE\t/\tFALSE\t0\thost\tonly",
            ".example.com\tTRUE\t/\tFALSE\t0\tdomain\tshared",
            "#HttpOnly_.example.com\tTRUE\t/media\tTRUE\t253402300799\tauth\tsecret",
            "example.com\tFALSE\t/\tFALSE\t1\texpired\told",
            "example.com\tFALSE\t/\tFALSE\t-1\tdeleted\told",
            "example.com\tFALSE\t/\tFALSE\t-62135596800\tancient\told",
            "example.com\tFALSE\t/\tFALSE\t0\tempty\t"
        ]);

        var root = Header(cookies, "http://example.com/");
        Assert.Contains("host=only", root);
        Assert.Contains("domain=shared", root);
        Assert.Contains("empty=", root);
        Assert.DoesNotContain("auth", root);
        Assert.DoesNotContain("expired", root);
        Assert.DoesNotContain("deleted", root);
        Assert.DoesNotContain("ancient", root);
        Assert.Contains("auth=secret", Header(cookies, "https://example.com/media/segment"));
        var auth = cookies.FileCookies.Single(c => c.Name == "auth");
        Assert.True(auth.Secure);
        Assert.True(auth.HttpOnly);
        Assert.DoesNotContain("auth", Header(cookies, "https://example.com/other"));
        Assert.DoesNotContain("auth", Header(cookies, "https://example.com/mediaother"));
        Assert.DoesNotContain("auth", Header(cookies, "http://example.com/media/segment"));
        var subdomain = Header(cookies, "https://cdn.example.com/media/segment");
        Assert.DoesNotContain("host", subdomain);
        Assert.Contains("domain=shared", subdomain);
        Assert.Contains("auth=secret", subdomain);
        Assert.Empty(Header(cookies, "https://otherexample.com/media/segment"));
    }

    [Fact]
    public void HostOnlyFlagOverridesLeadingDot()
    {
        var cookies = NetscapeCookieFile.Parse([".example.com\tFALSE\t/\tFALSE\t0\ttoken\tvalue"]);
        Assert.Equal("token=value", Header(cookies, "https://example.com/"));
        Assert.Empty(Header(cookies, "https://sub.example.com/"));
    }

    [Fact]
    public void LargeExportsDoNotSilentlyEvictCookies()
    {
        var lines = Enumerable.Range(0, 350)
            .Select(index => $"example.com\tFALSE\t/\tFALSE\t0\tc{index}\tvalue").ToArray();
        var cookies = NetscapeCookieFile.Parse(lines);
        Assert.Equal(350, Header(cookies, "https://example.com/").Split("; ").Length);
    }

    [Theory]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\tmissing-value")]
    [InlineData("example.com\tinvalid\t/\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tinvalid\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\tnot-a-time\ttoken\tsecret")]
    [InlineData("https://example.com\tFALSE\t/\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\tmedia\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\t\tsecret")]
    public void InvalidRowsReportLineNumberWithoutCookieContents(string row)
    {
        var error = Assert.Throws<FormatException>(() => NetscapeCookieFile.Parse(["# comment", row]));
        Assert.Contains("2", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void ValuesWithCommasAreSentUnchanged()
    {
        // 浏览器导出的 JSON 值常含逗号，System.Net.Cookie 会拒绝这类值。
        var cookies = NetscapeCookieFile.Parse([".example.com\tTRUE\t/\tFALSE\t0\t_policy\t{\"a\":true,\"b\":\"c\"}"]);
        Assert.Equal("_policy={\"a\":true,\"b\":\"c\"}", Header(cookies, "https://www.example.com/"));
    }

    [Fact]
    public void OutOfRangeExpirationsAreAccepted()
    {
        var future = DateTimeOffset.UtcNow.AddYears(1);
        var past = DateTimeOffset.UtcNow.AddYears(-1);
        // yt-dlp 可能写入 Chrome 时间戳（1601 年起的微秒数）。
        static long Chrome(DateTimeOffset time) => (time.ToUnixTimeSeconds() + 11644473600) * 1_000_000;
        var cookies = NetscapeCookieFile.Parse(
        [
            $"example.com\tFALSE\t/\tFALSE\t{Chrome(future)}\tchrome\tvalid",
            $"example.com\tFALSE\t/\tFALSE\t{Chrome(past)}\tchromeExpired\told",
            "example.com\tFALSE\t/\tFALSE\t9223372036854775807\tforever\tvalue",
            "example.com\tFALSE\t/\tFALSE\t-9223372036854775808\tnegative\told"
        ]);

        var header = Header(cookies, "https://example.com/");
        Assert.Contains("chrome=valid", header);
        Assert.Contains("forever=value", header);
        Assert.DoesNotContain("chromeExpired", header);
        Assert.DoesNotContain("negative", header);
        Assert.Equal(future.ToUnixTimeSeconds(), cookies.FileCookies.Single(c => c.Name == "chrome").Expires!.Value.ToUnixTimeSeconds());
    }

    [Theory]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\ttoken\tsecret;invalid")]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\tto=ken\tsecret")]
    public void RowsThatCannotBeSentAreSkippedWithoutFailingTheFile(string row)
    {
        var cookies = NetscapeCookieFile.Parse([row, "example.com\tFALSE\t/\tFALSE\t0\tok\tvalue"]);
        Assert.Equal("ok=value", Header(cookies, "https://example.com/"));
    }

    [Fact]
    public void ServerUpdatesReplaceFileCookiesWithTheSameIdentity()
    {
        var cookies = NetscapeCookieFile.Parse(
        [
            ".example.com\tTRUE\t/\tFALSE\t0\tsession\tfile",
            "other.com\tFALSE\t/\tFALSE\t0\tsession\tother"
        ]);
        cookies.SetCookies(new Uri("https://www.example.com/"), "session=server; Domain=.Example.com; Path=/");
        Assert.Equal("session=server", Header(cookies, "https://www.example.com/"));
        Assert.Equal("session=server", Header(cookies, "https://cdn.example.com/"));
        Assert.Equal("session=other", Header(cookies, "https://other.com/"));
    }

    [Fact]
    public void ServerUpdatesAtOtherPathsKeepFileCookies()
    {
        var cookies = NetscapeCookieFile.Parse(["example.com\tFALSE\t/\tFALSE\t0\tsession\tfile"]);
        var uri = new Uri("https://example.com/auth/login");
        cookies.SetCookies(uri, "session=server; Path=/auth");
        Assert.Equal("session=server; session=file", Header(cookies, "https://example.com/auth/login"));
        Assert.Equal("session=file", Header(cookies, "https://example.com/"));

        cookies.SetCookies(uri, "session=; Max-Age=0; Path=/auth");
        Assert.Equal("session=file", Header(cookies, "https://example.com/auth/login"));
    }

    [Fact]
    public void HostOnlyServerUpdatesKeepSharedFileCookies()
    {
        var cookies = NetscapeCookieFile.Parse([".example.com\tTRUE\t/\tFALSE\t0\tsession\tshared"]);
        cookies.SetCookies(new Uri("https://www.example.com/"), "session=www; Path=/");
        Assert.Equal("session=shared; session=www", Header(cookies, "https://www.example.com/"));
        Assert.Equal("session=shared", Header(cookies, "https://cdn.example.com/"));

        cookies.SetCookies(new Uri("https://www.example.com/"), "session=; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/");
        Assert.Equal("session=shared", Header(cookies, "https://www.example.com/"));
    }

    [Fact]
    public void ServerDeletionsRemoveFileCookiesWithTheSameIdentity()
    {
        var cookies = NetscapeCookieFile.Parse(
        [
            "example.com\tFALSE\t/auth\tFALSE\t0\tsession\tauth",
            "example.com\tFALSE\t/\tFALSE\t0\tsession\troot"
        ]);
        cookies.SetCookies(new Uri("https://example.com/auth/login"), "session=; Max-Age=0; Path=/auth");
        Assert.Equal("session=root", Header(cookies, "https://example.com/auth/login"));
    }

    [Fact]
    public void EveryCookieInACombinedSetCookieValueIsApplied()
    {
        var cookies = NetscapeCookieFile.Parse(
        [
            "example.com\tFALSE\t/\tFALSE\t0\ta\told",
            "example.com\tFALSE\t/\tFALSE\t0\tb\told",
            "example.com\tFALSE\t/\tFALSE\t0\tc\told",
            "example.com\tFALSE\t/\tFALSE\t0\td\tkept"
        ]);
        var uri = new Uri("https://example.com/");
        cookies.SetCookies(uri, "a=new; Path=/, b=new; Path=/");
        Assert.Equal("c=old; d=kept; a=new; b=new", Header(cookies, "https://example.com/"));

        cookies.SetCookies(uri, "a=; Expires=Thu, 01 Jan 1970 00:00:00 GMT; Path=/, c=; Max-Age=0; Path=/");
        Assert.Equal("d=kept; b=new", Header(cookies, "https://example.com/"));
    }

    [Theory]
    [InlineData("session=\"a; expires=x\"; Path=/", "session=\"a; expires=x\"")]
    [InlineData("session=\"a; max-age=0\"; Path=/", "session=\"a; max-age=0\"")]
    [InlineData("session= \"a, b; Expires=Wed, 21 Oct 2099 07:28:00 GMT\"; Path=/", "session=\"a, b; Expires=Wed, 21 Oct 2099 07:28:00 GMT\"")]
    [InlineData("session=\"a; max-age=5\"; Max-Age=0; Path=/", "")]
    [InlineData("session=new; Expires=\"Thu, 01 Jan 1970 00:00:00 GMT\"; Path=/", "")]
    public void ExpirationAttributesInsideQuotedValuesAreNotRewritten(string setCookie, string expected)
    {
        // 引号内的 "; expires=" 属于 Cookie 值，不能当作属性改写，否则文件中的旧值会与新值一起发送。
        var cookies = NetscapeCookieFile.Parse(["example.com\tFALSE\t/\tFALSE\t0\tsession\told"]);
        cookies.SetCookies(new Uri("https://example.com/"), setCookie);
        Assert.Equal(expected, Header(cookies, "https://example.com/"));
    }

    [Fact]
    public void DuplicateFileEntriesKeepTheLastValue()
    {
        var cookies = NetscapeCookieFile.Parse(
        [
            ".example.com\tTRUE\t/\tFALSE\t0\tsession\tfirst",
            "example.com\tFALSE\t/\tFALSE\t0\tother\tvalue",
            "Example.COM\tTRUE\t/\tFALSE\t0\tsession\tlast",
            "example.com\tFALSE\t/media\tFALSE\t0\tsession\tmedia"
        ]);
        Assert.Equal("session=last; other=value", Header(cookies, "https://example.com/"));
        Assert.Equal("session=media; session=last; other=value", Header(cookies, "https://example.com/media/a"));
        Assert.Equal(3, cookies.FileCookies.Count);
    }

    [Fact]
    public void FileWithBomAndCrLfIsLoadedWithoutModification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cookies-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, "# Netscape HTTP Cookie File\r\nexample.com\tFALSE\t/\tFALSE\t0\ttoken\tvalue\r\n",
                new UTF8Encoding(true));
            var original = File.ReadAllBytes(path);
            var cookies = NetscapeCookieFile.Load(path);
            Assert.Equal("token=value", Header(cookies, "https://example.com/"));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileFailsInsteadOfSendingUnauthenticatedRequests()
    {
        Assert.Throws<IOException>(() => NetscapeCookieFile.Load(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.txt")));
    }

    private static string Header(CookieFileJar cookies, string url) => cookies.GetCookieHeader(new Uri(url));
}
