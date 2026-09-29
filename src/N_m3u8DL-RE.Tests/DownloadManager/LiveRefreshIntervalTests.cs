using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.DownloadManager;
using Shouldly;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class LiveRefreshIntervalTests
{
    [Fact]
    public void DashUsesMinimumUpdatePeriodEvenWithLongPlaylist()
    {
        var stream = CreateStream(6, 1000);
        stream.Playlist!.MinimumUpdatePeriod = TimeSpan.FromSeconds(2.5);

        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(3);

        stream.Playlist.MinimumUpdatePeriod = TimeSpan.FromSeconds(60);
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(15);
    }

    [Fact]
    public void MissingUpdatePeriodUsesPlaylistDurationAndCapsAutomaticInterval()
    {
        var stream = CreateStream(4, 3);
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(4);

        stream.Playlist!.MediaParts[0].MediaSegments.AddRange(
            Enumerable.Range(0, 20).Select(_ => new MediaSegment { Duration = 4 }));
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(15);

        stream.Playlist.MediaParts[0].MediaSegments = [new MediaSegment { Duration = 4 }];
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(1);
    }

    [Fact]
    public void ExplicitWaitTimeKeepsPriorityOverAutomaticCap()
    {
        var stream = CreateStream(6, 1000);
        stream.Playlist!.MinimumUpdatePeriod = TimeSpan.FromSeconds(2);

        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, 30).ShouldBe(30);
    }

    [Fact]
    public void OtherFormatsUsePlaylistDuration()
    {
        var stream = CreateStream(4, 4);
        stream.Playlist!.MinimumUpdatePeriod = TimeSpan.FromSeconds(20);

        LiveRefreshInterval.GetSeconds([stream], ExtractorType.HLS, null).ShouldBe(6);
    }

    [Fact]
    public void MultipleStreamsUseShortestPlaylistDuration()
    {
        var video = CreateStream(4, 4);
        var audio = CreateStream(4, 2);

        LiveRefreshInterval.GetSeconds([video, audio], ExtractorType.HLS, null).ShouldBe(2);
    }

    [Fact]
    public void ZeroDurationUsesMinimumAndMissingPlaylistUsesDefault()
    {
        var stream = CreateStream(0, 1);
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(1);

        stream.Playlist!.MediaParts.Clear();
        LiveRefreshInterval.GetSeconds([stream], ExtractorType.MPEG_DASH, null).ShouldBe(5);
    }

    private static StreamSpec CreateStream(double duration, int count) => new()
    {
        Playlist = new Playlist
        {
            MediaParts = [new MediaPart
            {
                MediaSegments = Enumerable.Range(0, count)
                    .Select(_ => new MediaSegment { Duration = duration }).ToList()
            }]
        }
    };
}
