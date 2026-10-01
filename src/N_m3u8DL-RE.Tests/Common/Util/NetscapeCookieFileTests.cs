using System.Net;
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

        var root = cookies.GetCookies(new Uri("http://example.com/"));
        Assert.Equal("only", root["host"]!.Value);
        Assert.Equal("shared", root["domain"]!.Value);
        Assert.Equal("", root["empty"]!.Value);
        Assert.Null(root["auth"]);
        Assert.Null(root["expired"]);
        Assert.Null(root["deleted"]);
        Assert.Null(root["ancient"]);
        var auth = cookies.GetCookies(new Uri("https://example.com/media/segment"))["auth"]!;
        Assert.True(auth.Secure);
        Assert.True(auth.HttpOnly);
        Assert.Null(cookies.GetCookies(new Uri("https://example.com/other"))["auth"]);
        Assert.Null(cookies.GetCookies(new Uri("http://example.com/media/segment"))["auth"]);
        var subdomain = cookies.GetCookies(new Uri("https://cdn.example.com/media/segment"));
        Assert.Null(subdomain["host"]);
        Assert.Equal("shared", subdomain["domain"]!.Value);
        Assert.Equal("secret", subdomain["auth"]!.Value);
        Assert.Empty(cookies.GetCookieHeader(new Uri("https://otherexample.com/media/segment")));
    }

    [Fact]
    public void HostOnlyFlagOverridesLeadingDot()
    {
        var cookies = NetscapeCookieFile.Parse([".example.com\tFALSE\t/\tFALSE\t0\ttoken\tvalue"]);
        Assert.Equal("token=value", cookies.GetCookieHeader(new Uri("https://example.com/")));
        Assert.Empty(cookies.GetCookieHeader(new Uri("https://sub.example.com/")));
    }

    [Fact]
    public void LargeExportsDoNotSilentlyEvictCookies()
    {
        var lines = Enumerable.Range(0, 350)
            .Select(index => $"example.com\tFALSE\t/\tFALSE\t0\tc{index}\tvalue").ToArray();
        var cookies = NetscapeCookieFile.Parse(lines);
        Assert.Equal(350, cookies.GetCookies(new Uri("https://example.com/")).Count);
    }

    [Theory]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\tmissing-value")]
    [InlineData("example.com\tinvalid\t/\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tinvalid\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\tnot-a-time\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\t9223372036854775807\ttoken\tsecret")]
    [InlineData("https://example.com\tFALSE\t/\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\tmedia\tFALSE\t0\ttoken\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\t\tsecret")]
    [InlineData("example.com\tFALSE\t/\tFALSE\t0\ttoken\tsecret;invalid")]
    public void InvalidRowsReportLineNumberWithoutCookieContents(string row)
    {
        var error = Assert.Throws<FormatException>(() => NetscapeCookieFile.Parse(["# comment", row]));
        Assert.Contains("2", error.Message);
        Assert.DoesNotContain("secret", error.ToString());
        Assert.Null(error.InnerException);
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
            Assert.Equal("token=value", cookies.GetCookieHeader(new Uri("https://example.com/")));
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
}
