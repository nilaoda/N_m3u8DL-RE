using System.Text.RegularExpressions;
using Shouldly;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Parser.Extractor;

public class DASHPeriodSelectorTests
{
    private static ParserConfig CreateTestConfig(string mpdFileName) => new ParserConfig
    {
        OriginalUrl = $"file:///fake/path/to/{mpdFileName}",
    };

    private static async Task<List<StreamSpec>> ExtractAsync(string mpdName)
    {
        var config = CreateTestConfig(mpdName);
        var content = ResourceHelper.Read(mpdName);
        var extractor = new DASHExtractor2(config);
        return await extractor.ExtractStreamsAsync(content);
    }

    [Fact]
    public async Task MultiPeriod_StreamsCarryPeriodId()
    {
        var results = await ExtractAsync("Dash.Manifest_MultiPeriod.mpd");

        results.ShouldNotBeNull();
        // ad-period: 1 video + 1 audio, main-period: 2 video + 1 audio
        results.Count.ShouldBe(5);

        results.Count(s => s.PeriodId == "ad-period").ShouldBe(2);
        results.Count(s => s.PeriodId == "main-period").ShouldBe(3);
        results.ShouldAllBe(s => s.PeriodId != null);
    }

    [Fact]
    public async Task PeriodIdReg_KeepsOnlyMatchingPeriod()
    {
        var results = await ExtractAsync("Dash.Manifest_MultiPeriod.mpd");

        var filter = new StreamFilter
        {
            PeriodIdReg = new Regex("main-period"),
            For = "all",
        };

        var kept = FilterUtil.DoFilterKeep(results, filter);

        kept.Count.ShouldBe(3);
        kept.ShouldAllBe(s => s.PeriodId == "main-period");
    }

    [Fact]
    public async Task PeriodIdReg_DropExcludesMatchingPeriod()
    {
        var results = await ExtractAsync("Dash.Manifest_MultiPeriod.mpd");

        var filter = new StreamFilter
        {
            PeriodIdReg = new Regex("ad-period"),
            For = "all",
        };

        var remaining = FilterUtil.DoFilterDrop(results, filter);

        remaining.Count.ShouldBe(3);
        remaining.ShouldAllBe(s => s.PeriodId == "main-period");
    }

    [Fact]
    public async Task SinglePeriod_NotAffectedWhenNoPeriodFilter()
    {
        var results = await ExtractAsync("Dash.Manifest_1080p.mpd");

        var filter = new StreamFilter
        {
            For = "all",
        };

        var kept = FilterUtil.DoFilterKeep(results, filter);

        kept.Count.ShouldBe(results.Count);
    }
}
