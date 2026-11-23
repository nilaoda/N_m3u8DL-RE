using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class HlsAacGapDetectorTests
{
    [Fact]
    public void DistinguishesSourceGapsFromRoundingAndEndOfStream()
    {
        Assert.True(HlsAacGapDetector.HasSparseClock([new(1, 1024), new(1, 1624), new(1, 1024), new(1, 1624), new(1, 1024)], 44100));
        Assert.False(HlsAacGapDetector.HasSparseClock([new(1, 1023), new(1, 1025), new(2, 1024), new(1, 7021)], 44100));
        Assert.False(HlsAacGapDetector.HasSparseClock([new(1, 1024), new(1, 500000), new(1, 5022), new(1, 1024)], 44100));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SustainedClockDriftIsNotAnIsolatedHole(bool withGap)
    {
        var runs = Enumerable.Range(0, 24000).Select(i =>
            new HlsAacGapDetector.DurationRun(1, i % 12 == 0 ? 1025u : 1024u)).ToArray();
        if (withGap)
            runs[^3] = new(1, 5022);
        Assert.False(HlsAacGapDetector.HasSparseClock(runs, 48000));
    }

    [Fact]
    public void QuantizedClockCanStillContainAHole()
    {
        var runs = Enumerable.Range(0, 5000).Select(i =>
            new HlsAacGapDetector.DurationRun(1, i % 2 == 0 ? 1023u : 1025u)).ToArray();
        runs[^3] = new(1, 5022);
        Assert.True(HlsAacGapDetector.HasSparseClock(runs, 44100));
    }
}
