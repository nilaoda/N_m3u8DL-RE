using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class HlsAacGapRepairTests
{
    [Theory]
    [InlineData("Requested output format 'framecrc' is not known.", true)]
    [InlineData("Unrecognized option 'segment_time_metadata'", true)]
    [InlineData("Error parsing bitstream filter sequence 'aac_adtstoasc,setts': Bitstream filter not found", true)]
    [InlineData("Error reading input: Input/output error", false)]
    [InlineData("Invalid data found when processing input", false)]
    public void OnlyAnUnavailablePreflightFeatureFallsBack(string error, bool unavailable) =>
        Assert.Equal(unavailable, HlsAacGapRepair.ScanUnavailable(error));

    [Fact]
    public void RoundingIsCarriedBetweenMissingIntervals()
    {
        // Each hole is 2.4 AAC frames; rounding each hole independently would lose two frames.
        var track = new HlsAacGapDetector.Track(0, 44100, 2, 0, [
            new(120, 1024), new(1, 3482), new(8, 1024), new(1, 3482), new(6, 1024),
            new(1, 3482), new(1, 3482), new(1, 3482), new(5, 1024)]);
        var gaps = HlsAacGapRepair.Plan(track, default);
        Assert.Equal([120L, 129, 136, 137, 138], gaps.Select(g => g.AfterPacket));
        Assert.Equal([2, 3, 2, 3, 2], gaps.Select(g => g.Count));
        var smaller = track with { Durations = [new(10, 1024), new(2, 1600), new(10, 1024)] };
        Assert.Equal(new HlsAacGapRepair.Gap(10, 1), Assert.Single(HlsAacGapRepair.Plan(smaller, default)));
        var driftThenGap = track with { Durations = [new(550, 1025), new(1, 1026), new(1, 3072), new(10, 1024)] };
        Assert.Equal(new HlsAacGapRepair.Gap(551, 3), Assert.Single(HlsAacGapRepair.Plan(driftThenGap, default)));
    }

    [Fact]
    public void SmallJitterTrailingDurationAndResetDoNotCreateSilence()
    {
        var track = new HlsAacGapDetector.Track(0, 48000, 1, 0, [
            new(32, 1025), new(32, 1023), new(100, 1024), new(1, 4096)]);
        Assert.Empty(HlsAacGapRepair.Plan(track, default));
        var reset = track with { Durations = [new(100, 1024), new(1, 600000), new(10, 1024)] };
        Assert.Empty(HlsAacGapRepair.Plan(reset, default));
    }

    [Theory]
    [InlineData(2, 512)]
    [InlineData(4, 768)]
    public void DelayedTimestampsDoNotManufactureSilence(int count, int duration)
    {
        var track = new HlsAacGapDetector.Track(0, 48000, 2, 0, [
            new(3, 1024), new(1, 2048), new(1, 1025), new(1, 1023),
            new((uint)count, (uint)duration), new(4, 1024)]);
        Assert.Empty(HlsAacGapRepair.Plan(track, default));
        // All later AAC packets retain their original position when the same
        // timestamp excursion happens immediately after a genuine four-frame gap.
        var mixed = track with { Durations = [new(1, 5120), .. track.Durations, new(1, 3072), new(1, 1024)] };
        Assert.Equal([new(0, 4), new HlsAacGapRepair.Gap(11 + count, 2)], HlsAacGapRepair.Plan(mixed, default));
    }

    [Fact]
    public void PartialRepaymentAndTheFinalContainerDurationAreDistinct()
    {
        var track = new HlsAacGapDetector.Track(0, 44100, 2, 0, [
            new(1, 4096), new(1, 1024), new(2, 512), new(1, 70)]);
        Assert.Equal(new HlsAacGapRepair.Gap(0, 2), Assert.Single(HlsAacGapRepair.Plan(track, default)));
        // A long-established timestamp level followed by a late cancellation:
        // the file does not need silence and there is no safe local insertion.
        var late = track with { Durations = [new(1, 2048), new(50, 1024), new(2, 512), new(1, 1024)] };
        Assert.Empty(HlsAacGapRepair.Plan(late, default));
    }

    [Fact]
    public void AnIndependentSpliceDoesNotRelocateAnEarlierGap()
    {
        // Representative 44.1 kHz VOD. A short packet at an ad boundary is over
        // a thousand packets after a genuine gap. The later gap carries the remainder.
        var track = new HlsAacGapDetector.Track(0, 44100, 2, 0, [
            new(1, 2098), new(1, 3400), new(1107, 1024), new(1, 70),
            new(115, 1024), new(1, 5022), new(1, 1024)]);
        Assert.Equal([new(0, 1), new(1, 2), new HlsAacGapRepair.Gap(1225, 3)], HlsAacGapRepair.Plan(track, default));
    }

    [Fact]
    public async Task OverlappingPartsRetainOriginalPacketsAndFixGapsOnBothSidesOfJoin()
    {
        if (!HasTool("ffmpeg")) return;
        var temp = Directory.CreateTempSubdirectory("aac-part-clock-");
        try
        {
            var raw = Path.Combine(temp.FullName, "source.aac");
            var part = Path.Combine(temp.FullName, "part.m4a");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=frequency=731:r=48000",
                "-frames:a", "96", "-c:a", "aac", "-f", "adts", raw);
            await Run("ffmpeg", "-v", "error", "-i", raw, "-map", "0:a", "-c", "copy",
                "-bsf:a", @"noise=drop=between(n\,18\,22)", part);
            // The second part starts 128 samples before the preceding part's last packet.
            var timing = await Run("ffmpeg", "-v", "error", "-i", part, "-map", "0:a", "-c", "copy", "-f", "framecrc", "-");
            var packets = timing.Split('\n').Where(line => line.StartsWith("0,")).ToArray();
            var lastPts = long.Parse(packets[^1].Split(',')[2]);
            MediaPart[] parts = [new() { OutputDuration = (lastPts - 128) / 48000d }, new() { OutputDuration = 2.048 }];
            var baseline = Path.Combine(temp.FullName, "baseline.m4a");
            var output = Path.Combine(temp.FullName, "result.m4a");
            var infos = await MediainfoUtil.ReadInfoAsync("ffmpeg", part);
            Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", [part, part], parts, baseline, mediaInfos: infos));
            Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", [part, part], parts, output, mediaInfos: infos, checkAacClock: true));
            static async Task<string[]> PacketHashes(string path)
            {
                var hashes = await Run("ffmpeg", "-v", "error", "-i", path, "-map", "0:a",
                    "-c", "copy", "-f", "framehash", "-");
                return hashes.Split('\n').Where(s => s.StartsWith("0,")).Select(s => s.Split(',')[5].Trim()).ToArray();
            }
            var source = await PacketHashes(baseline);
            var repaired = await PacketHashes(output);
            Assert.Equal(2 * packets.Length, source.Length);
            Assert.Equal(source.Length + 9, repaired.Length);
            var silence = repaired[18];
            Assert.DoesNotContain(silence, source);
            Assert.Equal(source, repaired.Where(hash => hash != silence));
        }
        finally { temp.Delete(recursive: true); }
    }

    [Fact]
    public async Task OnlySparseLanguageIsReplacedAndStreamOrderTagsAndClocksSurvive()
    {
        if (!HasTool("ffmpeg")) return;
        var temp = Directory.CreateTempSubdirectory("aac-bilingual-");
        try
        {
            var source = Path.Combine(temp.FullName, "source.ts");
            var sub = Path.Combine(temp.FullName, "sub.srt");
            var part = Path.Combine(temp.FullName, "part.mp4");
            await File.WriteAllTextAsync(sub, "1\n00:00:00,100 --> 00:00:01,600\nsubtitle\n");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=64x64:r=25", "-f", "lavfi",
                "-i", "sine=frequency=701:r=48000", "-f", "lavfi", "-i", "sine=frequency=1031:r=44100",
                "-map", "0:v", "-map", "1:a", "-map", "2:a", "-t", "2", "-c:v", "libx264", "-c:a", "aac", source);
            await Run("ffmpeg", "-v", "error", "-i", source, "-i", sub, "-map", "0:a:0", "-map", "0:v:0",
                "-map", "0:a:1", "-map", "1:s:0", "-c", "copy", "-c:s", "mov_text",
                "-bsf:a:1", @"noise=drop=between(n\,18\,22)", "-metadata", "title=Movie Title",
                "-metadata:s:a:0", "language=eng", "-metadata:s:a:1", "language=jpn",
                "-metadata:s:a:1", "handler_name=Japanese track", "-metadata:s:s:0", "language=fra",
                "-disposition:a:0", "0", "-disposition:a:1", "default", part);
            var infos = await MediainfoUtil.ReadInfoAsync("ffmpeg", part);
            var baseline = Path.Combine(temp.FullName, "baseline.mp4");
            var output = Path.Combine(temp.FullName, "output.mp4");
            Assert.Equal(0, await MergeUtil.MergeByFFmpegAsync("ffmpeg", [part], baseline,
                FFmpegConcatMode.DEMUXER, mediaInfos: infos));
            Assert.Equal(0, await MergeUtil.MergeByFFmpegAsync("ffmpeg", [part], output,
                FFmpegConcatMode.DEMUXER, mediaInfos: infos, checkAacClock: true));
            static async Task<string[]> Frames(string file, string map)
            {
                var hash = await Run("ffmpeg", "-v", "error", "-copyts", "-i", file, "-map", map, "-c", "copy",
                    "-f", "framehash", "-");
                return hash.Split('\n').Where(line => line.StartsWith("0,")).ToArray();
            }
            var before = await Frames(baseline, "0:a:1");
            static string Digest(string row) => row.Split(',')[5].Trim();
            foreach (var map in new[] { "0:v:0", "0:a:0", "0:s:0" })
                Assert.Equal(await Frames(baseline, map), await Frames(output, map));
            var after = await Frames(output, "0:a:1");
            Assert.Equal(before.Length + 5, after.Length);
            Assert.Equal(before.Select(Digest), after.Take(18).Concat(after.Skip(23)).Select(Digest));
            Assert.Equal(before[0].Split(',')[2], after[0].Split(',')[2]); // initial PTS
            var probe = await ProcessUtil.RunAsync("ffmpeg", ["-hide_banner", "-i", output], default);
            Assert.Contains("Japanese track", probe.Error);
            var streamLines = probe.Error.Split('\n').Where(l => l.Contains("Stream #0:")).ToArray();
            Assert.Collection(streamLines,
                s => Assert.Contains("Video:", s),
                s => { Assert.Contains("(eng): Audio:", s); Assert.DoesNotContain("(default)", s); },
                s => { Assert.Contains("(jpn): Audio:", s); Assert.Contains("(default)", s); },
                s => Assert.Contains("(fra): Subtitle:", s));
        }
        finally { temp.Delete(recursive: true); }
    }
}
