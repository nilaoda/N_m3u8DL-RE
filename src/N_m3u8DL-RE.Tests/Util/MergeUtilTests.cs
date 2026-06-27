using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class MergeUtilTests
{
    // Issue #338 / #89: ffmpeg's concat protocol opens every segment at once and
    // fails with "Too many open files" when the OS file-handle limit is low.
    // The merge should detect that specific error and fall back to the concat demuxer.
    [Theory]
    [InlineData("[in#0 @ 0x7ff736704e40] Error opening input: Too many open files")]
    [InlineData("Error opening input files: Too many open files")]
    [InlineData("error opening input files: TOO MANY OPEN FILES")]
    public void IsTooManyOpenFilesError_DetectsFdExhaustion(string output)
    {
        Assert.True(MergeUtil.IsTooManyOpenFilesError(output));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Invalid data found when processing input")]
    [InlineData("Packet duration: 4191386680 / dts: 4210142341 is out of range")]
    public void IsTooManyOpenFilesError_IgnoresUnrelatedErrors(string output)
    {
        Assert.False(MergeUtil.IsTooManyOpenFilesError(output));
    }
}
