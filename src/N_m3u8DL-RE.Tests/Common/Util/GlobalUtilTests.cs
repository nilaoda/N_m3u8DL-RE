using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class GlobalUtilTests
{
    [Theory]
    [InlineData(0, "00m00s")]
    [InlineData(59, "00m59s")]
    [InlineData(60, "01m00s")]
    [InlineData(3600, "01h00m00s")]
    [InlineData(86399, "23h59m59s")]
    [InlineData(86400, "24h00m00s")]
    [InlineData(90061, "25h01m01s")]
    [InlineData(172800, "48h00m00s")]
    public void DurationUsesTotalHoursAndKeepsExistingShortFormat(int seconds, string expected)
    {
        Assert.Equal(expected, GlobalUtil.FormatTime(seconds));
        Assert.Equal(expected, GlobalUtil.FormatTime(TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void DurationDoesNotOverflowAnIntegerSecondCounter()
    {
        Assert.Equal("720000h00m00s", GlobalUtil.FormatTime(TimeSpan.FromDays(30000)));
    }
}
