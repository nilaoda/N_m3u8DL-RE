using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Tests.Runtime;

public class SpeedSamplingTests
{
    [Fact]
    public void SamplingMaintainsRateLimitAndStallDetectionWithoutRendering()
    {
        var speed = new SpeedContainer();
        speed.Add(100);
        speed.Sample(TimeSpan.FromSeconds(2), detectStall: true);
        Assert.Equal(50, speed.NowSpeed);
        Assert.Equal(0, speed.Downloaded);
        Assert.Equal(100, speed.RDownloaded);
        Assert.Equal(100, speed.Received);
        for (var i = 0; i < 20; i++)
            speed.Sample(TimeSpan.FromSeconds(1), detectStall: true);
        Assert.True(speed.ShouldStop);
        speed.AddReceived(20);
        speed.Sample(TimeSpan.FromSeconds(1), detectStall: true);
        Assert.False(speed.ShouldStop);
        Assert.Equal(100, speed.RDownloaded);
        Assert.Equal(120, speed.Received);
        speed.Sample(TimeSpan.FromSeconds(1), detectStall: false);
        Assert.Equal(0, speed.LowSpeedCount);
    }
}
