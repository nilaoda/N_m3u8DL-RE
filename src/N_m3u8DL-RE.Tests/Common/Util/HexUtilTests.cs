using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class HexUtilTests
{
    [Fact]
    public void BytesToHex_MultipleBytesWithDefaultSplit_ReturnsHexChars()
    {
        var result = HexUtil.BytesToHex([0xAB, 0xCD, 0xEF]);
        Assert.Equal("ABCDEF", result);
    }

    [Theory]
    [InlineData("AQ==", "01")]
    [InlineData("AQ", "01")]
    [InlineData("AQI=", "0102")]
    [InlineData("AQI", "0102")]
    [InlineData(" A Q\t\r\n", "01")]
    public void TryParseBase64_AcceptsPaddedAndUnpaddedInput(string input, string expected)
    {
        Assert.True(HexUtil.TryParseBase64(input, out var hex));
        Assert.Equal(expected, hex);
    }

    [Theory]
    [InlineData("A")]
    [InlineData("A!")]
    [InlineData("AQ=")]
    [InlineData("AQ===")]
    public void TryParseBase64_RejectsInvalidInput(string input)
    {
        Assert.False(HexUtil.TryParseBase64(input, out var hex));
        Assert.Null(hex);
    }

    [Fact]
    public void BytesToHex_MultipleBytesWithCustomSplit_ReturnsHexChars()
    {
        var result = HexUtil.BytesToHex([0xAA, 0xBB, 0xCC], ":");
        Assert.Equal("AA:BB:CC", result);
    }
}