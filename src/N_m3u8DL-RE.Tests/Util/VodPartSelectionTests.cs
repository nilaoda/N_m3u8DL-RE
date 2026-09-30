using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class VodPartSelectionTests
{
    private static StreamSpec Track(MediaType? type, string codec, params (long Id, double Duration)[] sections) => new()
    {
        MediaType = type, Codecs = codec, Resolution = type == null ? "1920x1080" : null,
        Playlist = new Playlist { MediaParts = sections.Select(s => new MediaPart {
            DiscontinuitySequence = s.Id, MediaInit = new MediaSegment { Url = $"https://example.test/{s.Id}/init" },
            MediaSegments = [new MediaSegment { Duration = s.Duration }] }).ToList() }
    };

    [Fact]
    public void RepeatedConfigsCollapseAcrossDifferentInitUrlsAndBitrates()
    {
        var first = Track(null, "avc1.640028", (1, 3), (3, 15));
        var second = Track(null, "avc1.640028", (2, 20));
        first.Bandwidth = 100000; second.Bandwidth = 200000;
        first.GroupId = "first"; second.GroupId = "second";
        var audio = Track(MediaType.AUDIO, "mp4a.40.2", (1, 2.98), (2, 19.99), (3, 14.98));
        var subs = Track(MediaType.SUBTITLES, "vtt", (1, 3), (3, 15));
        var group = Assert.Single(VodPartSelector.BuildGroups([first, second, audio, subs]));
        Assert.Equal(new long[] { 1, 2, 3 }, group.Ids);
        Assert.Equal(38, group.Duration); // 音视频不重复累加，字幕缺失不产生新组。
        Assert.DoesNotContain("https://", group.Display);
        Assert.DoesNotContain("init", group.Display);
    }

    [Fact]
    public void KeepingOneConfigRemovesAllMatchingSectionsAcrossTracks()
    {
        var video = Track(null, "avc1", (1, 3), (2, 100), (3, 3));
        var audio = Track(MediaType.AUDIO, "aac", (1, 3), (2, 100), (3, 3));
        var subs = Track(MediaType.SUBTITLES, "vtt", (1, 3), (2, 100), (3, 3));
        var streams = new List<StreamSpec> { video, audio, subs };
        var groups = VodPartSelector.BuildGroups(streams, (s, p) => new(
            $"{s.MediaType}:{(p.DiscontinuitySequence == 2 ? "body" : "ad")}", s.Codecs!));
        VodPartSelector.KeepGroups(streams, groups, [groups[1]]);
        Assert.All(streams, s => Assert.Equal(2, Assert.Single(s.Playlist!.MediaParts).DiscontinuitySequence));
        Assert.Throws<ArgumentException>(() => VodPartSelector.KeepGroups(streams, groups, []));
    }

    [Fact]
    public void SameCodecWithClearDurationGapOffersCompactShortAndLongChoices()
    {
        var video = Track(null, "avc1", (1, 3), (2, 15), (3, 732), (4, 30), (5, 527));
        var audio = Track(MediaType.AUDIO, "aac", (1, 2.98), (2, 14.98), (3, 731.99), (4, 29.98), (5, 526.99));
        var streams = new List<StreamSpec> { video, audio };
        var groups = VodPartSelector.BuildGroups(streams);
        Assert.Equal(2, groups.Count);
        Assert.Equal(new long[] { 1, 2, 4 }, groups[0].Ids);
        Assert.Equal(new long[] { 3, 5 }, groups[1].Ids);
        Assert.Equal(48, groups[0].Duration);
        Assert.Equal(1259, groups[1].Duration);
        VodPartSelector.KeepGroups(streams, groups, [groups[1]]);
        Assert.All(streams, s => Assert.Equal(new long[] { 3, 5 }, s.Playlist!.MediaParts.Select(VodPartSelector.Id)));
    }

    [Fact]
    public void SimilarDurationOrTinySectionsAreNotSplitHeuristically()
    {
        Assert.Single(VodPartSelector.BuildGroups([Track(null, "avc1", (1, 30), (2, 120), (3, 60))]));
        Assert.Single(VodPartSelector.BuildGroups([Track(null, "avc1", (1, 0.1), (2, 20))]));
    }

    [Fact]
    public void CodecResolutionAndAudioChannelsDefineDistinctChoices()
    {
        var first = Track(null, "avc1.640028", (1, 10));
        var second = Track(null, "hvc1.1", (2, 10));
        var third = Track(null, "avc1.640028", (3, 10)); third.Resolution = "1280x720";
        var fourth = Track(null, "avc1.640028", (4, 10));
        var audio = Track(MediaType.AUDIO, "aac", (1, 10), (2, 10), (3, 10)); audio.Channels = "2";
        var surround = Track(MediaType.AUDIO, "aac", (4, 10)); surround.Channels = "6";
        Assert.Equal(4, VodPartSelector.BuildGroups([first, second, third, fourth, audio, surround]).Count);
    }

    [Fact]
    public void AllMapsWithinASectionAreSelectedTogether()
    {
        var track = Track(null, "avc1", (1, 3), (1, 4), (2, 7));
        var groups = VodPartSelector.BuildGroups([track], (_, p) => new("map" + p.MediaSegments[0].Duration, "config"));
        Assert.Equal(2, groups.Count);
        Assert.Equal(7, groups[0].Duration);
        VodPartSelector.KeepGroups([track], groups, [groups[1]]);
        Assert.Equal(2, Assert.Single(track.Playlist!.MediaParts).DiscontinuitySequence);
    }

    [Fact]
    public void PeriodSplitByUrlAdsDoesNotRepeatItsFullDuration()
    {
        var video = Track(null, "avc1", (1, 2), (1, 3));
        foreach (var part in video.Playlist!.MediaParts) { part.PeriodIndex = 7; part.PeriodDuration = 100; }
        var group = Assert.Single(VodPartSelector.BuildGroups([video]));
        Assert.Equal(new long[] { 7 }, group.Ids);
        Assert.Equal(5, group.Duration);
    }

    [Fact]
    public void ProbeIgnoresIdsAndBitratesButHonorsActualResolution()
    {
        var first = VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Video", BaseInfo = "h264 (avc1)",
            Resolution = "384x216", Id = "[0x1]", Bitrate = "300 kb/s" }]);
        var second = VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Video", BaseInfo = "h264 (avc1)",
            Resolution = "384x216", Id = "[0x2]", Bitrate = "500 kb/s" }]);
        var third = VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Video", BaseInfo = "h264 (avc1)",
            Resolution = "1920x1080" }]);
        Assert.Equal(first, second);
        Assert.NotEqual(first, third);
        Assert.Null(VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Unknown" }]));
        Assert.Null(VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Video", BaseInfo = "h264" }]));
        Assert.Null(VodPartSelector.ProbeConfiguration([new Mediainfo { Type = "Audio", BaseInfo = "aac", Text = "aac" }]));
    }

    [Theory]
    [InlineData("48000 Hz, stereo", "48000 Hz, stereo", true)]
    [InlineData("48000 Hz, stereo", "44100 Hz, stereo", false)]
    [InlineData("48000 Hz, stereo", "48000 Hz, 5.1(side)", false)]
    [InlineData("48000 Hz, 5.1", "48000 Hz, 5.1(side)", false)]
    public void AudioProbeUsesSampleRateAndChannelLayout(string first, string second, bool same)
    {
        ConfigurationCheck(first, second, same);
        static void ConfigurationCheck(string first, string second, bool same)
        {
            VodPartSelector.Configuration? Config(string text, int bitrate) => VodPartSelector.ProbeConfiguration([
                new Mediainfo { Type = "Audio", BaseInfo = "aac (mp4a)", Text = $"aac (mp4a), {text}, fltp, {bitrate} kb/s" }]);
            Assert.Equal(same, Config(first, 128) == Config(second, 130));
        }
    }

    [Fact]
    public void DashQualityChoicesCollapsePeriodsAndPreferLongestSeed()
    {
        var ad = Track(null, "avc1", (0, 3));
        var body = Track(null, "avc1", (1, 100));
        var low = Track(null, "avc1", (1, 100)); low.Resolution = "640x360";
        var choices = VodPartSelector.QualityChoices([ad, body, low]);
        Assert.Equal(2, choices.Count);
        Assert.Same(body, choices[0]);
    }

    [Fact]
    public void QualityChoiceKeepsDifferentBitratesAtTheSameResolution()
    {
        var first = Track(null, "avc1", (0, 3)); first.Bandwidth = 100000;
        var repeated = Track(null, "avc1", (1, 100)); repeated.Bandwidth = 100000;
        var higher = Track(null, "avc1", (1, 100)); higher.Bandwidth = 200000;
        Assert.Equal(new[] { repeated, higher }, VodPartSelector.QualityChoices([first, repeated, higher]));
    }

    [Fact]
    public void ScriptOptionsDoNotAutomaticallyPromptButExplicitSelectionDoes()
    {
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { AutoSelect = true }));
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { SubOnly = true }));
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { VideoFilter = new StreamFilter() }));
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { VodDropParts = "0" }));
        Assert.True(VodPartSelector.ShouldPrompt(new MyOption { AutoSelect = true, VodSelectParts = true }));
    }

    [Fact]
    public void ExplicitFalseDisablesPartSelectionInInteractiveAndScriptModes()
    {
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { VodSelectParts = false }));
        Assert.False(VodPartSelector.ShouldPrompt(new MyOption { AutoSelect = true, VodSelectParts = false }));
        Assert.True(VodPartSelector.ShouldPrompt(new MyOption
        {
            AutoSelect = true, SubOnly = true, VideoFilter = new StreamFilter(),
            VodDropParts = "0", VodSelectParts = true
        }));
    }

    [Fact]
    public void UrlAdPreviewDoesNotChangeOriginalPartsOrTimeRange()
    {
        var video = Track(null, "avc1", (1, 3), (2, 100));
        video.Playlist!.MediaParts[0].MediaInit!.Url = "https://example.test/ad-init";
        var preview = VodPartSelector.PreviewStreams([video], ["ad-init"]);
        Assert.Equal(2, Assert.Single(preview[0].Playlist!.MediaParts).DiscontinuitySequence);
        Assert.Equal(2, video.Playlist.MediaParts.Count);
        Assert.NotNull(video.Playlist.MediaParts[0].MediaInit);
        Assert.Single(video.Playlist.MediaParts[0].MediaSegments);
        FilterUtil.ApplyCustomRange([video], new CustomRange { StartSec = 3, EndSec = 4, InputStr = "00:00:03-00:00:04" });
        Assert.Equal(2, Assert.Single(video.Playlist.MediaParts).DiscontinuitySequence);
        Assert.Equal(3, video.SkippedDuration);
    }

    [Fact]
    public void IdRangesKeepOriginalSequenceNumbers() => Assert.Equal("1-3,6,9-10",
        VodPartSelector.FormatIds([10, 2, 1, 6, 3, 9, 2]));
}
