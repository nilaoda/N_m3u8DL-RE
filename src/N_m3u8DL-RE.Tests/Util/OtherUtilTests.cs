using System.Text;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class OtherUtilTests
{
    [Fact]
    public void GetValidFileName_ReplacesInvalidChars()
    {
        Assert.Equal("a_b_c", OtherUtil.GetValidFileName("a:b*c"));
    }

    [Fact]
    public void GetValidFileName_NoMaxLength_LeavesLongNameUntouched()
    {
        var input = new string('a', 1000);
        Assert.Equal(input, OtherUtil.GetValidFileName(input));
    }

    // Issue #650: URL-derived names (e.g. YouTube DASH query strings) can exceed the
    // filesystem's 255-byte component limit and fail to create the temp file.
    [Fact]
    public void GetValidFileName_WithMaxLength_TruncatesToByteBudget()
    {
        var input = new string('a', 1000);
        var result = OtherUtil.GetValidFileName(input, maxLength: 200);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 200);
    }

    [Fact]
    public void TruncateFileName_ShortName_IsUnchanged()
    {
        Assert.Equal("seg_001.ts", OtherUtil.TruncateFileName("seg_001.ts", 200));
    }

    [Fact]
    public void TruncateFileName_IsDeterministic()
    {
        var input = new string('x', 500);
        Assert.Equal(OtherUtil.TruncateFileName(input, 100), OtherUtil.TruncateFileName(input, 100));
    }

    // Truncated names must stay unique so live-stream segment dedup keeps working.
    [Fact]
    public void TruncateFileName_DifferentLongNames_DoNotCollide()
    {
        var prefix = new string('p', 300);
        var a = OtherUtil.TruncateFileName(prefix + "_segmentA", 100);
        var b = OtherUtil.TruncateFileName(prefix + "_segmentB", 100);
        Assert.NotEqual(a, b);
        Assert.True(Encoding.UTF8.GetByteCount(a) <= 100);
        Assert.True(Encoding.UTF8.GetByteCount(b) <= 100);
    }

    [Fact]
    public void TruncateFileName_DoesNotSplitMultiByteChars()
    {
        // each '中' is 3 UTF-8 bytes; an odd budget must not cut one in half
        var input = string.Concat(Enumerable.Repeat("中", 100));
        var result = OtherUtil.TruncateFileName(input, 50);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= 50);
        // round-trips cleanly (no replacement chars from a broken sequence)
        Assert.DoesNotContain('�', result);
    }
}
