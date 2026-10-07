using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class VodOptimizationTests
{
    [Theory]
    [InlineData("0,2-4", new long[] { 0, 2, 3, 4 })]
    [InlineData("1,1-2,2", new long[] { 1, 2 })]
    public void PartIdsSupportRangesAndDuplicates(string value, long[] expected) =>
        Assert.Equal(expected, VodPartSelector.ParseIds(value).Order());

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("2-1")]
    [InlineData("a")]
    [InlineData("0-100001")]
    public void InvalidPartIdsAreRejected(string value) => Assert.Throws<ArgumentException>(() => VodPartSelector.ParseIds(value));

    [Fact]
    public void UnknownIdsAndLivePlaylistsAreRejectedBeforeMutation()
    {
        var stream = new StreamSpec { Playlist = new Playlist { MediaParts = [new MediaPart { PeriodIndex = 0,
            MediaInit = new MediaSegment { Url = "init" }, MediaSegments = [new MediaSegment { Duration = 2 }] }] } };
        Assert.Throws<ArgumentException>(() => VodPartSelector.Apply([stream], "0,1"));
        Assert.Single(stream.Playlist.MediaParts);
        stream.Playlist.IsLive = true;
        Assert.Throws<NotSupportedException>(() => VodPartSelector.Apply([stream], "0"));
    }

    [Theory]
    [InlineData("range")]
    [InlineData("key")]
    [InlineData("iv")]
    [InlineData("method")]
    public async Task InitReuseKeepsIndependentCopiesAndSeparatesEncryptionAndRanges(string change)
    {
        var root = Directory.CreateTempSubdirectory("vod-init-cache-").FullName;
        try
        {
            var cache = new VodInitCache(Path.Combine(root, "cache"));
            var first = new MediaSegment { Url = "https://example.test/init", StartRange = 0, ExpectLength = 4,
                EncryptInfo = new EncryptInfo { Method = EncryptMethod.AES_128, Key = [1], IV = [1] } };
            var second = new MediaSegment { Url = first.Url, StartRange = first.StartRange, ExpectLength = first.ExpectLength,
                EncryptInfo = new EncryptInfo { Method = first.EncryptInfo.Method, Key = [1], IV = [1] } };
            if (change == "range")
                second.StartRange = 4;
            if (change == "key")
                second.EncryptInfo.Key = [2];
            if (change == "iv")
                second.EncryptInfo.IV = [2];
            if (change == "method")
                second.EncryptInfo.Method = EncryptMethod.NONE;
            var downloads = 0;
            async Task<DownloadResult?> Download(string path)
            {
                downloads++;
                await File.WriteAllBytesAsync(path, [(byte)downloads]);
                return new DownloadResult { ActualFilePath = path, ActualContentLength = 1 };
            }
            var result = await cache.DownloadAsync(first, Path.Combine(root, "first.mp4.tmp"), new SpeedContainer(), Download);
            await File.WriteAllBytesAsync(result!.ActualFilePath, [99]); // 模拟 MSS 原地重建 init。
            File.Delete(result.ActualFilePath); // 模拟解密后清理源文件。
            var reused = await cache.DownloadAsync(first, Path.Combine(root, "reused.mp4.tmp"), new SpeedContainer(), Download);
            Assert.Equal([1], await File.ReadAllBytesAsync(reused!.ActualFilePath));
            var distinct = await cache.DownloadAsync(second, Path.Combine(root, "distinct.mp4.tmp"), new SpeedContainer(), Download);
            Assert.Equal(2, downloads);
            Assert.Equal([2], await File.ReadAllBytesAsync(distinct!.ActualFilePath));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void TimestampMapHonorsLocalTimeAndMpegtsWrap()
    {
        const double wrap = (1L << 33) / 90000d;
        var text = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:00:00:10.000,MPEGTS:90000\n\n00:00:10.250 --> 00:00:11.000\ncue\n\n";
        var sub = WebVttSub.Parse(text);
        HlsSubtitleTimeline.Normalize(sub, text, wrap - 0.5);
        Assert.InRange(sub.Cues[0].StartTime.TotalSeconds, 1.749999, 1.750001);
        Assert.InRange(sub.Cues[0].EndTime.TotalSeconds, 2.499999, 2.500001);
    }

    [Theory]
    [InlineData(2758898.142, 0, 0.0346222222)]
    [InlineData(2758898.142 - 100000, 100000, 100000.0346222222)]
    public void TimestampMapUsesCurrentCueToResolveOldBroadcastAnchor(double origin, double segmentStart, double expected)
    {
        // LOCAL 锚点与当前 cue 相隔约一天，媒体已累计多个 MPEGTS 周期。
        var text = "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:742:20:24.094,MPEGTS:3000\n\n" +
            "766:21:38.142 --> 766:21:44.148\ncue\n";
        var sub = WebVttSub.Parse(text);
        HlsSubtitleTimeline.Normalize(sub, text, origin, segmentStart);
        Assert.InRange(sub.Cues[0].StartTime.TotalSeconds, expected - 0.000001, expected + 0.000001);
        Assert.InRange(sub.Cues[0].EndTime.TotalSeconds, expected + 6.005999, expected + 6.006001);
        Assert.Equal(0, sub.MpegtsTimestamp);
    }
}
