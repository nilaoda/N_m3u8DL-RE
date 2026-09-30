using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;
using Shouldly;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Tests.Util;

public class FilterUtilTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RangeAcrossPartsUsesOriginalTimelineAndRemovesUnusedInit(bool byIndex)
    {
        var init = new MediaSegment { Url = "content-init" };
        var stream = new StreamSpec { Playlist = new() { MediaParts =
        [
            new() { MediaInit = new() { Url = "discarded-init" },
                MediaSegments = [new() { Index = 0, Duration = 2 }, new() { Index = 1, Duration = 2 }] },
            new() { MediaInit = init,
                MediaSegments = [new() { Index = 2, Duration = 2 }, new() { Index = 3, Duration = 2 }] },
        ] } };
        var range = byIndex ? new CustomRange { InputStr = "2-2", StartSegIndex = 2, EndSegIndex = 2 }
            : new CustomRange { InputStr = "00:00:04-00:00:05", StartSec = 4, EndSec = 5 };
        FilterUtil.ApplyCustomRange([stream], range);
        var part = Assert.Single(stream.Playlist.MediaParts);
        Assert.Same(init, part.MediaInit);
        Assert.Equal(2, Assert.Single(part.MediaSegments).Index);
        Assert.Equal(4, stream.SkippedDuration);
    }

    [Fact]
    public void CleanAd_RemovesOrphanInitAndPreservesRemainingPartInit()
    {
        var contentInit = new MediaSegment { Url = "https://example.com/content-init.mp4" };
        var stream = new StreamSpec { Playlist = new Playlist { MediaParts =
        [
            new() { MediaInit = new() { Url = "https://example.com/init-1.mp4" },
                MediaSegments = [new() { Url = "https://example.com/ad/1.m4s" }] },
            new() { MediaInit = contentInit,
                MediaSegments = [new() { Url = "https://example.com/content/1.m4s" }] },
        ] } };
        FilterUtil.CleanAd([stream], ["/ad/"]);
        var part = Assert.Single(stream.Playlist.MediaParts);
        Assert.Same(contentInit, part.MediaInit);
        Assert.Single(part.MediaSegments);
    }

    [Fact]
    public void CleanAd_AllMediaRemovedLeavesNoInitEvenWhenInitUrlDoesNotMatch()
    {
        var stream = new StreamSpec { Playlist = new Playlist { MediaParts =
        [new() { MediaInit = new() { Url = "https://example.com/init.mp4" },
            MediaSegments = [new() { Url = "https://example.com/ad/1.m4s" }] }] } };
        FilterUtil.CleanAd([stream], ["/ad/"]);
        Assert.Empty(stream.Playlist.MediaParts);
        Assert.Equal(0, stream.SegmentsCount);
    }

    [Fact]
    public void CleanAd_InitMatchRemovesItsDependentMedia()
    {
        var stream = new StreamSpec { Playlist = new Playlist { MediaParts =
        [new() { MediaInit = new() { Url = "https://example.com/ad/init.mp4" },
            MediaSegments = [new() { Url = "https://example.com/video/1.m4s" }] }] } };
        FilterUtil.CleanAd([stream], ["/ad/"]);
        Assert.Empty(stream.Playlist.MediaParts);
    }

    [Fact]
    public void ParseAdKeywords_NullKeywords_ReturnsEmptyList()
    {
        FilterUtil.ParseAdKeywords(null).ShouldBeEmpty();
    }

    [Fact]
    public void ParseAdKeywords_CompilesEachKeyword()
    {
        var regList = FilterUtil.ParseAdKeywords(["/ad/", @"ccode=\d+"]);
        regList.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("https://cdn.example.com/ad/seg1.ts", true)]
    [InlineData("https://cdn.example.com/video/seg1.ts", false)]
    public void IsAd_MatchesAgainstKeywordRegexes(string url, bool expected)
    {
        var regList = FilterUtil.ParseAdKeywords(["/ad/", "advert"]);
        FilterUtil.IsAd(url, regList).ShouldBe(expected);
    }

    [Fact]
    public void CleanAdSegments_RemovesMatchingSegments()
    {
        var segments = new List<MediaSegment>
        {
            new() { Index = 0, Url = "https://cdn.example.com/video/0.ts" },
            new() { Index = 1, Url = "https://cdn.example.com/ad/1.ts" },
            new() { Index = 2, Url = "https://cdn.example.com/video/2.ts" },
        };
        var regList = FilterUtil.ParseAdKeywords(["/ad/"]);

        var result = FilterUtil.CleanAdSegments(segments, regList);

        result.Count.ShouldBe(2);
        result.ShouldAllBe(s => !s.Url.Contains("/ad/"));
    }

    [Fact]
    public void CleanAdSegments_EmptyRegexList_ReturnsInputUnchanged()
    {
        var segments = new List<MediaSegment>
        {
            new() { Index = 0, Url = "https://cdn.example.com/ad/0.ts" },
        };

        var result = FilterUtil.CleanAdSegments(segments, []);

        result.ShouldBeSameAs(segments);
    }
}
