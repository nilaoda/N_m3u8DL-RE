using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class HlsAacPacketClockTests
{
    [Fact]
    public void UsesAbsolutePtsAndKeepsOriginalAudioIndexAndStart()
    {
        HlsAacPacketClock[] clocks = [new(2)];
        foreach (var line in new[]
        {
            "#tb 0: 1/90000", "#sample_rate 0: 44100", "#channel_layout_name 0: stereo",
            "0, 90000, 90000, 2089, 192, 0xab, S=1, New Extradata, 2, 0x00340022",
            "0, 92090, 92090, 2089, 175, 0xcd", "0, 94179, 94179, 2089, 192, 0xef"
        }) HlsAacPacketClock.ReadLine(clocks, line);

        var track = Assert.IsType<HlsAacGapDetector.Track>(clocks[0].Finish());
        Assert.Equal(2, track.AudioIndex);
        Assert.Equal(1, track.StartSeconds);
        Assert.Equal([new HlsAacGapDetector.DurationRun(3, 1024)], track.Durations);
    }

    [Theory]
    [InlineData("#extradata 0: 2, 0x00340021")]
    [InlineData("0, 2000, 2100, 2089, 192, 0xcd")]
    [InlineData("0, 2000000, 2000000, 2089, 192, 0xcd")]
    public void UncertainConfigurationOrClockIsNotPadded(string changed)
    {
        HlsAacPacketClock[] clocks = [new(0)];
        foreach (var line in new[]
        {
            "#tb 0: 1/90000", "#sample_rate 0: 44100", "#channel_layout_name 0: stereo",
            "#extradata 0: 2, 0x00340022", "0, 0, 0, 2089, 192, 0xab",
            "0, 2090, 2090, 2089, 192, 0xcd", changed
        }) HlsAacPacketClock.ReadLine(clocks, line);

        Assert.Null(clocks[0].Finish());
    }

    [Theory]
    [InlineData(1920, true, true)]
    [InlineData(1920, false, false)]
    [InlineData(1024, true, false)]
    public void OnlySmallOverlapsAtVerifiedPartBoundariesFollowTheMp4Clock(int sourcePts, bool newPart, bool supported)
    {
        HlsAacPacketClock[] clocks = [new(0, concatList: true)];
        foreach (var line in new[] { "#tb 0: 1/44100", "#sample_rate 0: 44100",
            "#channel_layout_name 0: stereo", "#extradata 0: 5, 0x0366015d" })
            HlsAacPacketClock.ReadLine(clocks, line);
        void Packet(int muxPts, int originalPts, int part)
        {
            var duration = HlsAacPacketClock.TimestampBias + originalPts;
            HlsAacPacketClock.ReadLine(clocks,
                $"0, {muxPts}, {muxPts}, {duration}, 192, 0xab, S=1, Strings Metadata, 63, 0x{part:x8}");
        }
        Packet(1024, 1024, 1);
        Packet(2048, 2048, 1);
        Packet(2048, sourcePts, newPart ? 2 : 1);
        var track = clocks[0].Finish();
        if (supported)
            Assert.Equal([new(1, 1024), new(1, 1), new HlsAacGapDetector.DurationRun(1, 1024)], track!.Durations);
        else
            Assert.Null(track);
    }
}
