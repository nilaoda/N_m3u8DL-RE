using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class MuxSubtitleTimelineTests
{
    [Fact]
    public void BroadcastSrtUsesMediaOriginAndPreservesTheOpeningSilence()
    {
        var text = "1\n00:16:40,750 --> 00:16:41,250\n<i>Hello</i>\nsecond line\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 1000.25, 3);
        Assert.True(result.Changed);
        Assert.False(result.Unresolved);
        Assert.Equal(-1000.25, result.Offset);
        Assert.Contains("00:00:00,500 --> 00:00:01,000", result.Text);
        Assert.Contains("<i>Hello</i>\nsecond line", result.Text);
    }

    [Fact]
    public void RelativeSrtIsKeptExactlyAsWritten()
    {
        var text = "\uFEFF1\r\n00:00:00,500 --> 00:00:01,000\r\nHello\r\n\r\n";
        var result = MuxSubtitleTimeline.Repair(text, 1000, 3);
        Assert.False(result.Changed);
        Assert.False(result.Unresolved);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void AmbiguousCueTimingIsNotShifted()
    {
        var text = "1\n00:00:20,500 --> 00:00:21,000\nHello\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 10, 60);
        Assert.False(result.Changed);
        Assert.True(result.Unresolved);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void LostSourceClockIsReportedWithoutGuessingFromTheFirstCue()
    {
        var text = "1\n00:16:40,500 --> 00:16:41,000\nHello\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 0, 3);
        Assert.False(result.Changed);
        Assert.True(result.Unresolved);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void VttUsesLocalAndMpegtsMappingAndPreservesStyles()
    {
        var text = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:05.000,MPEGTS:90000000\n\n" +
                   "STYLE\n::cue(.green) { color: lime; }\n\n" +
                   "cue-id\n00:00:07.500 --> 00:00:08.000 align:start position:10%\n<c.green>Hello</c>\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 1000, 5);
        Assert.True(result.Changed);
        Assert.Contains("00:00:02.500 --> 00:00:03.000 align:start position:10%", result.Text);
        Assert.Contains("cue-id", result.Text);
        Assert.Contains("::cue(.green) { color: lime; }", result.Text);
        Assert.Contains("<c.green>Hello</c>", result.Text);
        Assert.DoesNotContain("X-TIMESTAMP-MAP", result.Text);
    }

    [Fact]
    public void VttClockWrapIsExpandedBeforeNormalization()
    {
        var text = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:90000000\n\n" +
                   "00:00:02.500 --> 00:00:03.000\nHello\n\n";
        var result = MuxSubtitleTimeline.Repair(text, (1L << 33) / 90000d + 1000.25, 5);
        Assert.True(result.Changed);
        Assert.Contains("00:00:02.250 --> 00:00:02.750", result.Text);
    }

    [Fact]
    public void ShiftedCuesAreClippedToTheMediaInterval()
    {
        var text = "1\n00:16:38,000 --> 00:16:39,000\nBefore\n \n" +
                   "2\n00:16:39,750 --> 00:16:40,750\nCrossing\n\n" +
                   "3\n00:16:42,500 --> 00:16:44,000\nTail\n\n" +
                   "4\n00:16:45,000 --> 00:16:46,000\nAfter\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 1000, 3);
        Assert.True(result.Changed);
        Assert.DoesNotContain("Before", result.Text);
        Assert.DoesNotContain("After", result.Text);
        Assert.Contains("00:00:00,000 --> 00:00:00,750", result.Text);
        Assert.Contains("00:00:02,500 --> 00:00:03,000", result.Text);
    }

    [Fact]
    public void BroadcastHoursBeyondOneDayAreNotWrapped()
    {
        var text = "1\n773:03:32,732 --> 773:03:33,232\nHello\n\n";
        var result = MuxSubtitleTimeline.Repair(text, 2783012.232, 3);
        Assert.True(result.Changed);
        Assert.Contains("00:00:00,500 --> 00:00:01,000", result.Text);
    }
}
