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

    [Fact]
    public void BytesToHex_MultipleBytesWithCustomSplit_ReturnsHexChars()
    {
        var result = HexUtil.BytesToHex([0xAA, 0xBB, 0xCC], ":");
        Assert.Equal("AA:BB:CC", result);
    }
}