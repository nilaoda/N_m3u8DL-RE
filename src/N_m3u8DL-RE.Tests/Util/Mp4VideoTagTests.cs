using System.Text.Json;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class Mp4VideoTagTests
{
    [Theory]
    [InlineData(false, null, "hev1", "hvc1")]
    [InlineData(true, 5, "hev1", "dvh1")]
    [InlineData(true, 8, "dvh1", "hvc1")]
    [InlineData(true, 7, "dvh1", "dvh1")]
    [InlineData(true, 7, "hev1", "hvc1")]
    [InlineData(true, null, "dvhe", "dvh1")]
    public void TagsRespectProfileContainerAndOutputVideoIndex(bool dolbyVision, int? profile, string sourceTag, string expected)
    {
        Mediainfo[] infos =
        [
            new() { Type = "Video", BaseInfo = "h264" },
            new() { Type = "Video", BaseInfo = "hevc", DolbyVison = dolbyVision, DolbyVisionProfile = profile, CodecTag = sourceTag },
            new() { Type = "Audio", BaseInfo = "aac" },
            new() { Type = "Video", BaseInfo = "av1", DolbyVison = true, DolbyVisionProfile = 10 },
            new() { Type = "Video", BaseInfo = "dvvideo", CodecTag = "dvh1" }
        ];
        Assert.Equal(new[] { "-tag:v:2", expected }, MergeUtil.VideoTags(".mp4", infos, videoIndex: 1));
        Assert.Equal(dolbyVision ? ["-tag:v:2", "hvc1"] : Array.Empty<string>(),
            MergeUtil.VideoTags(".mkv", infos, videoIndex: 1));
        Assert.Empty(MergeUtil.VideoTags(".ts", infos));
    }

    [Theory]
    [InlineData("merge")]
    [InlineData("mux")]
    [InlineData("parts")]
    public async Task Mp4RemuxTagsHevcWithoutChangingOtherTracks(string operation)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("re-mp4-tags-").FullName;
        try
        {
            var source = Path.Combine(root, "source.mp4");
            await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "testsrc2=s=128x72:r=25",
                "-f", "lavfi", "-i", "color=s=128x72:r=25", "-f", "lavfi", "-i", "sine=sample_rate=48000",
                "-map", "1:v", "-map", "0:v", "-map", "2:a", "-t", "0.4",
                "-c:v:0", "libx264", "-c:v:1", "libx265", "-x265-params", "log-level=error:pools=1:frame-threads=1",
                "-bf", "0", "-c:a", "aac", "-tag:v:1", "hev1", source);
            var output = Path.Combine(root, "output.mp4");
            if (operation == "merge")
                Assert.Equal(0, await MergeUtil.MergeByFFmpegAsync("ffmpeg", [source], output, FFmpegConcatMode.LOCAL_HTTP));
            else if (operation == "parts")
                Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", [source, source],
                    [new MediaPart { MediaSegments = [new() { Duration = 0.4 }] },
                     new MediaPart { MediaSegments = [new() { Duration = 0.4 }] }], output));
            else
                Assert.Equal(0, await MergeUtil.MuxInputsAsync("ffmpeg", [new OutputFile { Index = 0, FilePath = source }], output));
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames", "-show_entries",
                "stream=codec_name,codec_tag_string,nb_read_frames", "-of", "json", output));
            var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
            Assert.Equal(new[] { "h264", "hevc", "aac" }, streams.Select(stream => stream.GetProperty("codec_name").GetString()));
            Assert.Equal(new[] { "avc1", "hvc1" }, streams.Take(2).Select(stream => stream.GetProperty("codec_tag_string").GetString()));
            Assert.All(streams.Take(2), stream => Assert.Equal(operation == "parts" ? "20" : "10", stream.GetProperty("nb_read_frames").GetString()));
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-map", "0:v", "-map", "0:a", "-f", "null", "-");
        }
        finally { Directory.Delete(root, true); }
    }
}
