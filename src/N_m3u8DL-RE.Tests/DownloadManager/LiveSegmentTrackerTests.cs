using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.DownloadManager;
using Shouldly;
using System.Text.Json;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public class LiveSegmentTrackerTests
{
    [Fact]
    public void SequenceReset_KeepsSourceIndexesAndUniqueRecordingFiles()
    {
        var tracker = new LiveSegmentTracker();
        var first = new[] { Segment(98, "old/98"), Segment(99, "old/99"), Segment(100, "old/100") }.ToList();
        tracker.Record(first);

        // 刷新列表中有旧片段，媒体序号却已从 1 重新开始。
        var overlap = new[] { Segment(1, "old/99"), Segment(2, "old/100"), Segment(3, "new/1"), Segment(4, "new/2") }.ToList();
        var second = tracker.Filter(overlap, true, s => s.Index.ToString());
        second.Select(s => s.Url).ShouldBe(new[] { "new/1", "new/2" });
        tracker.Record(second);
        second.Select(s => s.Index).ShouldBe(new long[] { 3, 4 });

        // 上一片已经滑出列表时，也要继续使用新的录制顺序。
        var reset = new[] { Segment(1, "reset/1"), Segment(2, "reset/2") }.ToList();
        var third = tracker.Filter(reset, true, s => s.Index.ToString());
        tracker.Record(third);
        third.Select(s => s.Index).ShouldBe(new long[] { 1, 2 });

        var recorded = first.Concat(second).Concat(third).ToList();
        recorded.Select(s => s.RecordingIndex).ShouldBe(Enumerable.Range(1, recorded.Count).Select(i => (long?)i));
        recorded.Select(s => LiveSegmentTracker.GetFileName(s, s.Index.ToString())).Distinct().Count().ShouldBe(recorded.Count);
        recorded.ToHashSet().Count.ShouldBe(recorded.Count);
    }

    [Fact]
    public void ReusedIndexWithDifferentUrl_DoesNotMatchOldSegment()
    {
        var tracker = new LiveSegmentTracker();
        tracker.Record(new[] { Segment(3, "old/3") }.ToList());

        var reset = new[] { Segment(1, "new/1"), Segment(2, "new/2"), Segment(3, "new/3") }.ToList();
        tracker.Filter(reset, true, s => s.Index.ToString()).ShouldBe(reset);
    }

    [Fact]
    public void ReusedUrls_DoNotMoveOverlapPastRecordedSourceIndex()
    {
        var tracker = new LiveSegmentTracker();
        tracker.Record(new[] { Segment(12, "A.ts") }.ToList());

        var refreshed = new[]
        {
            Segment(11, "B.ts"), Segment(12, "A.ts"),
            Segment(13, "B.ts"), Segment(14, "A.ts")
        }.ToList();
        var newSegments = tracker.Filter(refreshed, true, s => s.Index.ToString());

        newSegments.Select(s => s.Index).ShouldBe(new long[] { 13, 14 });
    }

    [Fact]
    public void ReusedUrlsWithResetIndex_DoNotGuessAmbiguousOverlap()
    {
        var tracker = new LiveSegmentTracker();
        tracker.Record(new[] { Segment(100, "A.ts") }.ToList());

        var reset = new[]
        {
            Segment(1, "B.ts"), Segment(2, "A.ts"),
            Segment(3, "B.ts"), Segment(4, "A.ts")
        }.ToList();

        tracker.Filter(reset, true, s => s.Index.ToString()).ShouldBe(reset);
    }

    [Fact]
    public void SameTimestamp_UsesUrlToFindOverlap()
    {
        var tracker = new LiveSegmentTracker();
        var dateTime = new DateTime(2026, 9, 28, 12, 0, 0, 1, DateTimeKind.Utc);
        var old = Segment(10, "old/10", dateTime);
        tracker.Record(new[] { old }.ToList());

        var refreshed = new[] { Segment(1, "old/10", dateTime), Segment(2, "new/2", dateTime) }.ToList();
        tracker.Filter(refreshed, true, s => s.Index.ToString()).Select(s => s.Url).ShouldBe(new[] { "new/2" });
    }

    [Fact]
    public void SameTimestampWithChangedSignature_DoesNotSkipFollowingSegment()
    {
        var tracker = new LiveSegmentTracker();
        var dateTime = new DateTime(2026, 9, 28, 12, 0, 0, 1, DateTimeKind.Utc);
        tracker.Record(new[] { Segment(12, "https://example.com/A.ts?token=old", dateTime) }.ToList());

        var refreshed = new[]
        {
            Segment(12, "https://example.com/A.ts?token=new", dateTime),
            Segment(13, "https://example.com/B.ts?token=new", dateTime)
        }.ToList();

        tracker.Filter(refreshed, true, s => s.Index.ToString())
            .Select(s => s.Index).ShouldBe(new long[] { 13 });
    }

    [Fact]
    public void ChangedSignature_FindsOverlapByPathAndSourceIndex()
    {
        var tracker = new LiveSegmentTracker();
        tracker.Record(new[] { Segment(20, "https://example.com/20.ts?token=old") }.ToList());

        var refreshed = new[]
        {
            Segment(20, "https://example.com/20.ts?token=new"),
            Segment(21, "https://example.com/21.ts?token=new")
        }.ToList();
        tracker.Filter(refreshed, true, s => s.Index.ToString()).Select(s => s.Index).ShouldBe(new long[] { 21 });
    }

    [Fact]
    public void RepeatedSourceIdentity_HasDistinctDictionaryKeysAndFileNames()
    {
        var tracker = new LiveSegmentTracker();
        var first = Segment(1, "segment.ts");
        var second = Segment(1, "segment.ts");
        tracker.Record(new[] { first }.ToList());
        tracker.Record(new[] { second }.ToList());

        first.Equals(second).ShouldBeFalse();
        new Dictionary<MediaSegment, string> { [first] = "old", [second] = "new" }.Count.ShouldBe(2);
        LiveSegmentTracker.GetFileName(first, "1").ShouldNotBe(LiveSegmentTracker.GetFileName(second, "1"));

        var json = GlobalUtil.ConvertToJson(new List<MediaSegment> { first, second });
        using var document = JsonDocument.Parse(json);
        document.RootElement.EnumerateArray().Select(item => item.EnumerateObject()
            .Single(property => property.Name.Equals("Index", StringComparison.OrdinalIgnoreCase)).Value.GetInt64())
            .ShouldBe(new long[] { 1, 1 });
        json.ToLowerInvariant().ShouldNotContain("recordingindex");
    }

    [Fact]
    public void CheckingForNewSegments_DoesNotAdvanceRecordingBoundary()
    {
        var tracker = new LiveSegmentTracker();
        tracker.Record([Segment(20, "https://example.com/20.ts?token=old")]);
        var unchanged = new List<MediaSegment> { Segment(20, "https://example.com/20.ts?token=new") };
        tracker.Filter(unchanged, true, s => s.Index.ToString()).ShouldBeEmpty();
        tracker.Filter([], true, s => s.Index.ToString()).ShouldBeEmpty();

        var refreshed = new List<MediaSegment> { unchanged[0], Segment(21, "https://example.com/21.ts") };
        tracker.Filter(refreshed, true, s => s.Index.ToString()).Count.ShouldBe(1);
        var pending = tracker.Filter(refreshed, true, s => s.Index.ToString());
        pending.Select(s => s.Index).ShouldBe(new long[] { 21 });
        pending[0].RecordingIndex.ShouldBeNull();
        tracker.Record(pending);
        tracker.Filter(refreshed, true, s => s.Index.ToString()).ShouldBeEmpty();
    }

    private static MediaSegment Segment(long index, string url, DateTime? dateTime = null) => new()
    {
        Index = index,
        Url = url,
        DateTime = dateTime,
    };
}
