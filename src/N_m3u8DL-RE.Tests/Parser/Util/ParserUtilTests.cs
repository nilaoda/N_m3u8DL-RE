using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Util;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Parser.Util;

public class ParserUtilTests
{
    [Theory]
    [InlineData("$Number%08x$", 28324L, "00006ea4")]
    [InlineData("$Number%08X$", 28324L, "00006EA4")]
    [InlineData("$Number%08x$", 22999L, "000059d7")]
    [InlineData("$Number%08x$", 0L, "00000000")]
    [InlineData("$Number%02x$", 28324L, "6ea4")]
    [InlineData("$Number%016x$", 4294967297L, "0000000100000001")]
    [InlineData("$Number%08d$", 28324L, "00028324")]
    [InlineData("$Number%05d$", 42L, "00042")]
    [InlineData("$Number%02d$", 28324L, "28324")]
    [InlineData("$Number$", 28324L, "28324")]
    [InlineData("$RepresentationID$/$Number%08x$/$Number%05d$/$Number%08X$.m4s", 28324L, "audio/00006ea4/28324/00006EA4.m4s")]
    public void ReplaceVars_FormatsNumber(string template, long number, string expected)
    {
        var values = new Dictionary<string, object?>
        {
            [DASHTags.TemplateNumber] = number,
            [DASHTags.TemplateRepresentationID] = "audio"
        };
        ParserUtil.ReplaceVars(template, values).ShouldBe(expected);
    }

    [Fact]
    public void ReplaceVars_LeavesUnavailableNumberUnchanged()
    {
        ParserUtil.ReplaceVars("$Number%08x$", new()).ShouldBe("$Number%08x$");
    }
}
