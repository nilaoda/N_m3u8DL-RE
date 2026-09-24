using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Tests.Common.Entity;

public class WebVttSubTests
{
    [Fact]
    public void Parse_WithoutClassTags_PreservesContent()
    {
        var vtt = "WEBVTT\n\n00:12:18.195 --> 00:12:22.032 line:85.19%,end\nかけまくもかしこき日不見の神よ\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Single(sub.Cues);
        Assert.Equal("かけまくもかしこき日不見の神よ", sub.Cues[0].Payload);
    }

    [Fact]
    public void Parse_WithClassTags_PreservesSurroundingContent()
    {
        var vtt = "WEBVTT\n\n00:12:18.195 --> 00:12:22.032 line:85.19%,end\n遠つ<ruby>御祖<c.dssrtu><rt>みおや</rt></c></ruby>の<ruby>産土<c.dssrtu><rt>うぶすな</rt></c></ruby>よ\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Single(sub.Cues);
        // <c.dssrtu> and </c> should be stripped, but surrounding text preserved
        Assert.Equal("遠つ<ruby>御祖<rt>みおや</rt></ruby>の<ruby>産土<rt>うぶすな</rt></ruby>よ", sub.Cues[0].Payload);
    }

    [Fact]
    public void Parse_WithClassTagsAndPlainTextBefore_PreservesAllContent()
    {
        var vtt = "WEBVTT\n\n00:12:18.195 --> 00:12:22.032 line:85.19%,end\nかけまくもかしこき<ruby>日不見<rt>ひみず</rt></ruby>の神よ 遠つ<ruby>御祖<c.dssrtu><rt>みおや</rt></c></ruby>の<ruby>産土<c.dssrtu><rt>うぶすな</rt></c></ruby>よ\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Single(sub.Cues);
        // The <c.dssrtu> tags should be removed but everything else preserved
        var expected = "かけまくもかしこき<ruby>日不見<rt>ひみず</rt></ruby>の神よ 遠つ<ruby>御祖<rt>みおや</rt></ruby>の<ruby>産土<rt>うぶすな</rt></ruby>よ";
        Assert.Equal(expected, sub.Cues[0].Payload);
    }

    [Fact]
    public void Parse_MultipleCuesWithClassTags_AllPreserved()
    {
        var vtt = "WEBVTT\n\n00:01:00.000 --> 00:01:05.000\nFirst line with <c.myClass>inner</c> text\n\n00:02:00.000 --> 00:02:05.000\nSecond <c.other>content</c> here\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Equal(2, sub.Cues.Count);
        Assert.Equal("First line with inner text", sub.Cues[0].Payload);
        Assert.Equal("Second content here", sub.Cues[1].Payload);
    }

    [Fact]
    public void Parse_CueWithoutPayload_Skipped()
    {
        var vtt = "WEBVTT\n\n00:01:00.000 --> 00:01:05.000\n\n00:02:00.000 --> 00:02:05.000\nValid payload\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Single(sub.Cues);
        Assert.Equal("Valid payload", sub.Cues[0].Payload);
    }

    [Fact]
    public void Parse_WithTimestampMap_ExtractsMpegtsTimestamp()
    {
        var vtt = "WEBVTT\nX-TIMESTAMP-MAP=MPEGTS:90000\n\n00:01:00.000 --> 00:01:05.000\nHello\n";
        var sub = WebVttSub.Parse(vtt);
        Assert.Equal(90000, sub.MpegtsTimestamp);
        Assert.Single(sub.Cues);
    }

    [Fact]
    public void ToVtt_And_ToSrt_RoundTrip()
    {
        var vtt = "WEBVTT\n\n00:01:00.000 --> 00:01:05.000\nHello world\n\n00:02:00.000 --> 00:02:05.000\nSecond cue\n";
        var sub = WebVttSub.Parse(vtt);
        var vttOutput = sub.ToVtt();
        Assert.Contains("WEBVTT", vttOutput);
        Assert.Contains("Hello world", vttOutput);

        var srtOutput = sub.ToSrt();
        Assert.Contains("1", srtOutput);
        Assert.Contains("00:01:00,000", srtOutput);
    }

    [Fact]
    public void LeftShiftTime_ShiftsCorrectly()
    {
        var vtt = "WEBVTT\n\n00:01:10.000 --> 00:01:15.000\nTest\n";
        var sub = WebVttSub.Parse(vtt);
        sub.LeftShiftTime(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(60), sub.Cues[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(65), sub.Cues[0].EndTime);
    }

    [Fact]
    public void LeftShiftTime_DoesNotGoNegative()
    {
        var vtt = "WEBVTT\n\n00:00:05.000 --> 00:00:10.000\nTest\n";
        var sub = WebVttSub.Parse(vtt);
        sub.LeftShiftTime(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(0), sub.Cues[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(0), sub.Cues[0].EndTime);
    }

    [Fact]
    public void AddCuesFromOne_CombinesCorrectly()
    {
        var vtt1 = "WEBVTT\n\n00:01:00.000 --> 00:01:05.000\nFirst\n";
        var vtt2 = "WEBVTT\n\n00:02:00.000 --> 00:02:05.000\nSecond\n";
        var sub1 = WebVttSub.Parse(vtt1);
        var sub2 = WebVttSub.Parse(vtt2);
        sub1.AddCuesFromOne(sub2);
        Assert.Equal(2, sub1.Cues.Count);
        Assert.Equal("First", sub1.Cues[0].Payload);
        Assert.Equal("Second", sub1.Cues[1].Payload);
    }
}
