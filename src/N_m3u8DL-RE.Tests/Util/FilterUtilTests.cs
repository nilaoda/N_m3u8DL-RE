using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Util;

public class FilterUtilTests
{
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
