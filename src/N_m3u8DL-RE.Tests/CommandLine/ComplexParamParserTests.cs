using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class ComplexParamParserTests
{
    [Fact]
    public void GetValue_SimpleKeyValue_ReturnsValue()
    {
        var parser = new ComplexParamParser("path=foo.srt:lang=en");
        Assert.Equal("foo.srt", parser.GetValue("path"));
        Assert.Equal("en", parser.GetValue("lang"));
    }

    [Fact]
    public void GetValue_MissingKey_ReturnsNull()
    {
        var parser = new ComplexParamParser("path=foo.srt");
        Assert.Null(parser.GetValue("lang"));
    }

    [Fact]
    public void GetValue_KeyWithoutValue_ReturnsTrue()
    {
        var parser = new ComplexParamParser("path=foo.srt:default");
        Assert.Equal("true", parser.GetValue("default"));
    }

    // Issue #737: an apostrophe inside the value (common in episode titles)
    // used to throw "Parse Argument [path] failed!".
    [Fact]
    public void GetValue_ValueWithApostrophe_IsPreserved()
    {
        var parser = new ComplexParamParser("path=S07E01 What's Next.srt:lang=en");
        Assert.Equal("S07E01 What's Next.srt", parser.GetValue("path"));
        Assert.Equal("en", parser.GetValue("lang"));
    }

    [Fact]
    public void GetValue_ValueWithDoubleQuoteInside_IsPreserved()
    {
        var parser = new ComplexParamParser("name=He said \"hi\":lang=en");
        Assert.Equal("He said \"hi\"", parser.GetValue("name"));
    }

    [Theory]
    [InlineData("path=\"foo bar.srt\"", "foo bar.srt")]
    [InlineData("path='foo bar.srt'", "foo bar.srt")]
    public void GetValue_SurroundingQuotesArePairStripped(string arg, string expected)
    {
        var parser = new ComplexParamParser(arg);
        Assert.Equal(expected, parser.GetValue("path"));
    }

    [Fact]
    public void GetValue_EscapedColonIsKeptInValue()
    {
        var parser = new ComplexParamParser(@"name=a\:b:lang=en");
        Assert.Equal("a:b", parser.GetValue("name"));
        Assert.Equal("en", parser.GetValue("lang"));
    }
}
