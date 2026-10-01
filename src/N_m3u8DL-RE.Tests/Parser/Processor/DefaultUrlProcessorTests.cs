using System.Web;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor;

namespace N_m3u8DL_RE.Tests.Parser.Processor;

public class DefaultUrlProcessorTests
{
    [Theory]
    [InlineData("file:///tmp/manifest.mpd", "https://example.com/manifest.mpd?token=base", "base")]
    [InlineData("file:///tmp/manifest.mpd?token=input", "https://example.com/manifest.mpd?token=base", "input")]
    [InlineData("https://example.com/manifest.mpd?token=input", "https://cdn.example.com/?token=base", "input")]
    [InlineData("https://example.com/manifest.mpd", "https://cdn.example.com/?token=base", "segment")]
    [InlineData("file:///tmp/manifest.mpd", "", "segment")]
    [InlineData("file:///tmp/manifest.mpd", "file:///tmp/base.mpd?token=base", "segment")]
    public void QuerySourcePreservesExistingPriority(string source, string baseUrl, string expectedToken)
    {
        var config = new ParserConfig { Url = source, BaseUrl = baseUrl, AppendUrlParams = true };
        var result = new DefaultUrlProcessor().Process("https://cdn.example.com/seg.m4s?token=segment&own=1", config);
        var query = HttpUtility.ParseQueryString(new Uri(result).Query);
        Assert.Equal(expectedToken, query["token"]);
        Assert.Equal("1", query["own"]);
    }

    [Fact]
    public void EmptyAndEncodedValuesArePreserved()
    {
        var config = new ParserConfig
        {
            Url = "file:///tmp/manifest.mpd",
            BaseUrl = "https://example.com/manifest.mpd?empty=&token=a%2Bb%2Fc%3D&name=%E4%B8%AD%E6%96%87",
            AppendUrlParams = true
        };
        var result = new DefaultUrlProcessor().Process("https://example.com/seg.m4s?own=1", config);
        var query = HttpUtility.ParseQueryString(new Uri(result).Query);
        Assert.Equal("", query["empty"]);
        Assert.Equal("a+b/c=", query["token"]);
        Assert.Equal("中文", query["name"]);
        Assert.Equal("1", query["own"]);
    }

    [Theory]
    [InlineData("file:///tmp/seg.m4s")]
    [InlineData("base64://AAAA")]
    public void LocalAndSyntheticResourcesAreUnchanged(string resource)
    {
        var config = new ParserConfig
        {
            Url = "file:///tmp/manifest.mpd", BaseUrl = "https://example.com/?token=base", AppendUrlParams = true
        };
        Assert.Equal(resource, new DefaultUrlProcessor().Process(resource, config));
    }
}
