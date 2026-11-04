using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class PartNormalizationTests
{
    [Theory]
    [InlineData(1000, 1000, true)]
    [InlineData(1000, 90000, false)]
    public async Task MatchingTimeBasesSkipNormalizationAndKeepPeriodOffsets(int firstScale, int secondScale, bool canSkip)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe")) return;
        var root = Directory.CreateTempSubdirectory("part-time-bases-").FullName;
        try
        {
            var files = new[] { Path.Combine(root, "first.mp4"), Path.Combine(root, "second.mp4") };
            var scales = new[] { firstScale, secondScale };
            for (var i = 0; i < files.Length; i++)
            {
                await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=96x64:r=25",
                    "-f", "lavfi", "-i", "sine=sample_rate=48000", "-t", "1", "-c:v", "libx264", "-bf", "0",
                    "-c:a", "aac", "-video_track_timescale", scales[i].ToString(),
                    "-output_ts_offset", (i * 5).ToString(), files[i]);
            }
            Assert.Equal(canSkip, MergeUtil.CanSkipPartsNormalization(files));
            var output = Path.Combine(root, "output.mp4");
            MediaPart[] parts = Enumerable.Range(0, 2).Select(i => new MediaPart
            {
                PeriodIndex = i, OutputStart = i, OutputDuration = 1, OutputInpoint = i * 5,
                MediaSegments = [new MediaSegment { Duration = 1 }]
            }).ToArray();

            Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", files, parts, output));

            await AssertVideo(output, 2, 50);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DifferentAudioTimeBasesStillRequireNormalization()
    {
        if (!HasTool("ffmpeg")) return;
        var root = Directory.CreateTempSubdirectory("part-audio-time-bases-").FullName;
        try
        {
            var files = new[] { Path.Combine(root, "first.m4a"), Path.Combine(root, "second.m4a") };
            for (var i = 0; i < files.Length; i++)
                await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i",
                    $"sine=sample_rate={(i == 0 ? 48000 : 44100)}:duration=1", "-c:a", "aac", files[i]);
            Assert.False(MergeUtil.CanSkipPartsNormalization(files));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void InvalidMetadataKeepsNormalization()
    {
        var root = Directory.CreateTempSubdirectory("part-invalid-time-bases-").FullName;
        try
        {
            var path = Path.Combine(root, "invalid.mp4");
            File.WriteAllText(path, "not an mp4");
            Assert.False(MergeUtil.CanSkipPartsNormalization([path]));
        }
        finally { Directory.Delete(root, true); }
    }
}
