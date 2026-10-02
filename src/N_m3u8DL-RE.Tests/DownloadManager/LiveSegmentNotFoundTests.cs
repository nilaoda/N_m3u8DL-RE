using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.DownloadManager;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class LiveSegmentNotFoundTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdvancingWindowKeepsWaitForPresentSegmentAndEndsWaitForRemovedSegment(bool hls)
    {
        var policy = new LiveSegmentNotFoundPolicy(hls, segment => segment.NameFromVar ?? segment.Url);
        var pending = Segment(2);
        policy.Update([Segment(0), Segment(1), pending], 1);
        var wait = policy.GetPublicationWait(pending);
        Assert.Equal(TimeSpan.FromSeconds(5), wait);
        // 刷新会重建对象并重置 DASH 的局部 Index，不能靠对象引用或局部序号判断窗口。
        var refreshed = Segment(2);
        refreshed.Index = hls ? 2 : 0;
        refreshed.Url += "?token=new";
        policy.Update([refreshed, Segment(3), Segment(4)], 1);
        Assert.True(policy.ShouldRetry(pending, 4, 3, TimeSpan.FromSeconds(2), wait));
        policy.Update([Segment(3), Segment(4)], 1);
        Assert.False(policy.ShouldRetry(pending, 4, 3, TimeSpan.FromSeconds(2), wait));
        // 即使目标已滑出窗口，仍保留用户指定的普通 404 重试次数。
        Assert.True(policy.ShouldRetry(pending, 3, 3, TimeSpan.FromSeconds(2), wait));
    }

    [Fact]
    public void EmptyWindowAndRepeatedRefreshDoNotResetWaitBudget()
    {
        var policy = new LiveSegmentNotFoundPolicy(false, segment => segment.NameFromVar ?? segment.Url);
        var pending = Segment(2);
        policy.Update([Segment(0), Segment(1), pending], 1);
        Assert.Equal(TimeSpan.Zero, policy.GetPublicationWait(Segment(0)));
        var wait = policy.GetPublicationWait(pending);
        policy.Update([], 1);
        Assert.True(policy.ShouldRetry(pending, 1, 0, TimeSpan.FromSeconds(2), wait));
        policy.Update([Segment(0), Segment(1), Segment(2)], 1);
        Assert.False(policy.ShouldRetry(pending, 6, 0, wait, wait));
    }

    [Theory]
    [InlineData(0.1, 3)]
    [InlineData(60, 30)]
    [InlineData(double.NaN, 11)]
    public void PublicationWaitIsBounded(double duration, double expected)
    {
        var policy = new LiveSegmentNotFoundPolicy(false, segment => segment.NameFromVar ?? segment.Url);
        var pending = Segment(0);
        pending.Duration = duration;
        policy.Update([pending], 1);
        Assert.Equal(TimeSpan.FromSeconds(expected), policy.GetPublicationWait(pending));
    }

    [Fact]
    public void WindowSnapshotDoesNotFollowProducerMutations()
    {
        var policy = new LiveSegmentNotFoundPolicy(true, segment => segment.Url);
        var pending = Segment(0);
        policy.Update([pending], 1);
        var original = pending.WithIndex(pending.Index);
        pending.Index = 100;
        pending.Url = "https://example.test/replaced.m4s";
        var wait = policy.GetPublicationWait(original);
        Assert.Equal(TimeSpan.FromSeconds(5), wait);
        Assert.True(policy.ShouldRetry(original, 1, 0, TimeSpan.Zero, wait));
        policy.Update([pending], 1);
        Assert.False(policy.ShouldRetry(original, 1, 0, TimeSpan.Zero, wait));
    }

    [Fact]
    public void HlsSequenceResetUsesUniqueUrlAndRejectsAmbiguousUrlsOrDifferentRanges()
    {
        var policy = new LiveSegmentNotFoundPolicy(true, segment => segment.Url);
        var pending = Segment(0);
        pending.StartRange = 0;
        pending.ExpectLength = 100;
        policy.Update([pending], 1);
        var wait = policy.GetPublicationWait(pending);
        var other = Segment(1);
        other.Url = pending.Url;
        other.StartRange = pending.StartRange;
        other.ExpectLength = pending.ExpectLength;
        policy.Update([other], 1);
        // 与录制去重一致：序号重置后，唯一的相同 URL 和字节范围仍可定位原分片。
        Assert.True(policy.ShouldRetry(pending, 1, 0, TimeSpan.Zero, wait));
        var duplicate = other.WithIndex(2);
        policy.Update([other, duplicate], 1);
        Assert.False(policy.ShouldRetry(pending, 1, 0, TimeSpan.Zero, wait));
        other.Index = pending.Index;
        other.StartRange = 100;
        policy.Update([other], 1);
        Assert.False(policy.ShouldRetry(pending, 1, 0, TimeSpan.Zero, wait));
        other.StartRange = pending.StartRange;
        other.Url = "https://example.test/reset/media-0.m4s";
        policy.Update([other], 1);
        Assert.False(policy.ShouldRetry(pending, 1, 0, TimeSpan.Zero, wait));
    }

    private static MediaSegment Segment(int number) => new()
    {
        Index = number, NameFromVar = number.ToString(), Duration = 2,
        Url = $"https://example.test/media-{number}.m4s"
    };
}
