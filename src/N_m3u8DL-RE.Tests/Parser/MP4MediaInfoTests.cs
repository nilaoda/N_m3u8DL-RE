using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Mp4SubtitleParser;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Parser;

public sealed class MP4MediaInfoTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-mp4-info-").FullName;

    [Theory]
    [InlineData("video", 1000)]
    [InlineData("audio", 1000.2)]
    [InlineData("combined", 1000)]
    [InlineData("fragmented", 1000)]
    [InlineData("signed-offset", 1000)]
    [InlineData("no-edit", 1000)]
    [InlineData("faststart", 1000)]
    [InlineData("video", 2783012.232)]
    public async Task BoxTimingMatchesActualMediaPresentation(string mode, double origin)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var path = Path.Combine(directory, "source.mp4");
        List<string> arguments = ["-v", "error"];
        if (mode != "audio")
            arguments.AddRange(["-f", "lavfi", "-i", "testsrc2=s=160x90:r=25"]);
        if (mode is "audio" or "combined")
            arguments.AddRange(["-f", "lavfi", "-i", "sine=duration=2"]);
        arguments.AddRange(["-t", "2", "-c:v", "libx264", "-preset", "fast", "-c:a", "aac",
            "-output_ts_offset", origin.ToString(CultureInfo.InvariantCulture)]);
        if (mode == "fragmented")
            arguments.AddRange(["-movflags", "frag_keyframe+empty_moov+default_base_moof", "-g", "10"]);
        if (mode == "signed-offset")
            arguments.AddRange(["-movflags", "+negative_cts_offsets"]);
        if (mode == "no-edit")
            arguments.AddRange(["-use_editlist", "0"]);
        if (mode == "faststart")
            arguments.AddRange(["-movflags", "+faststart"]);
        arguments.Add(path);
        await Run("ffmpeg", arguments.ToArray());
        var timing = MP4MediaInfoUtil.ReadTiming(path);
        Assert.NotNull(timing);
        // ffprobe 仅用于测试中的独立比对，产品读取路径不会启动它。
        var reference = await Run("ffprobe", "-v", "error", "-show_entries", "format=start_time,duration", "-of", "json", path);
        using var json = JsonDocument.Parse(reference);
        var format = json.RootElement.GetProperty("format");
        var start = double.Parse(format.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture);
        var duration = double.Parse(format.GetProperty("duration").GetString()!, CultureInfo.InvariantCulture);
        Assert.InRange(timing.Value.Start, start - 0.002, start + 0.002);
        Assert.InRange(timing.Value.Duration, duration - 0.002, duration + 0.002);
    }

    [Fact]
    public async Task RepeatedIdenticalInitializationPreservesFragmentTiming()
    {
        if (!HasTool("ffmpeg"))
            return;
        var original = Path.Combine(directory, "fragmented.mp4");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=2", "-c:a", "aac",
            "-output_ts_offset", "1000", "-movflags", "frag_keyframe+empty_moov+default_base_moof",
            "-frag_duration", "500000", original);
        var expected = MP4MediaInfoUtil.ReadTiming(original);
        Assert.NotNull(expected);
        var bytes = File.ReadAllBytes(original);
        var moovOffset = 0;
        var moovSize = 0;
        var fragments = new List<int>();
        for (var offset = 0; offset < bytes.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moov"u8))
            {
                moovOffset = offset;
                moovSize = size;
            }
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                fragments.Add(offset);
            offset += size;
        }
        Assert.True(fragments.Count > 1);
        // 模拟直播拼接：中途再次写入 ftyp/moov，媒体数据和 tfdt 保持原样。
        var initialization = bytes[..(moovOffset + moovSize)];
        var repeated = bytes[..fragments[1]].Concat(initialization).Concat(bytes[fragments[1]..]).ToArray();
        var path = Path.Combine(directory, "repeated.mp4");
        File.WriteAllBytes(path, repeated);
        Assert.Equal(expected, MP4MediaInfoUtil.ReadTiming(path));

        // 修改第二份 moov 的 mvhd 创建时间，确认不同初始化信息仍被拒绝。
        repeated[fragments[1] + moovOffset + 20] ^= 1;
        File.WriteAllBytes(path, repeated);
        Assert.Null(MP4MediaInfoUtil.ReadTiming(path));
    }

    [Fact]
    public async Task ExtendedMdatIsSkippedAndMoovAtTheEndIsRead()
    {
        if (!HasTool("ffmpeg"))
            return;
        var original = Path.Combine(directory, "source.mp4");
        await Run("ffmpeg", "-v", "error", "-f", "lavfi", "-i", "sine=duration=1", "-c:a", "aac", original);
        var expected = MP4MediaInfoUtil.ReadTiming(original);
        Assert.NotNull(expected);
        var bytes = File.ReadAllBytes(original);
        var moov = Array.Empty<byte>();
        for (var offset = 0; offset < bytes.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moov"u8))
                moov = bytes.AsSpan(offset, size).ToArray();
            offset += size;
        }
        Assert.NotEmpty(moov);
        var path = Path.Combine(directory, "sparse.mp4");
        const long mdatSize = 128L * 1024 * 1024;
        using (var stream = File.Create(path))
        {
            var header = new byte[16];
            BinaryPrimitives.WriteUInt32BigEndian(header, 1);
            "mdat"u8.CopyTo(header.AsSpan(4));
            BinaryPrimitives.WriteUInt64BigEndian(header.AsSpan(8), (ulong)mdatSize);
            stream.Write(header);
            stream.Position = mdatSize;
            stream.Write(moov);
        }
        Assert.Equal(expected, MP4MediaInfoUtil.ReadTiming(path));
    }

    [Fact]
    public void IncompleteAndNonMp4InputsHaveNoTiming()
    {
        var path = Path.Combine(directory, "invalid.mp4");
        File.WriteAllBytes(path, [0, 0, 0, 100, 109, 111, 111, 118]);
        Assert.Null(MP4MediaInfoUtil.ReadTiming(path));
        File.WriteAllText(path, "This is not an MP4 file.");
        Assert.Null(MP4MediaInfoUtil.ReadTiming(path));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
