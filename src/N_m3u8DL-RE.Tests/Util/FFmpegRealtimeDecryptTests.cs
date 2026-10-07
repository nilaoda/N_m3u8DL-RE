using System.Buffers.Binary;
using System.Globalization;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Util;

public class FFmpegRealtimeDecryptTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 36000)]
    [InlineData(true, 0)]
    [InlineData(true, 36000)]
    public async Task DecryptedFragmentsPreserveSamplesAndTimestampsAfterBinaryMerge(bool video, int origin)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || !OnPath("mp4encrypt"))
            return;
        var root = Directory.CreateTempSubdirectory("ffmpeg-realtime-decrypt-").FullName;
        try
        {
            // 覆盖音频、带 B 帧的视频，以及零起点和非零起点的独立加密分片。
            string[] codecOptions = video ? ["-c:v", "libx264", "-g", "50"] : ["-c:a", "aac"];
            await Run("ffmpeg", ["-v", "error", "-y", "-f", "lavfi", "-i",
                video ? "testsrc2=s=160x90:r=25" : "sine=sample_rate=48000", "-t", "6",
                ..codecOptions, "-output_ts_offset", origin.ToString(CultureInfo.InvariantCulture),
                "-f", "hls", "-hls_time", "2", "-hls_segment_type", "fmp4",
                "-hls_fmp4_init_filename", "init.mp4", "-hls_segment_filename",
                Path.Combine(root, "media-%d.m4s"), Path.Combine(root, "source.m3u8")]);
            var plain = Path.Combine(root, "plain.mp4");
            MergeUtil.CombineMultipleFilesIntoSingleFile([Path.Combine(root, "init.mp4"),
                .. Directory.GetFiles(root, "media-*.m4s").Order()], plain);
            var encrypted = Path.Combine(root, "encrypted.mp4");
            var kid = new string('1', 32);
            var key = new string('a', 32);
            await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random",
                "--property", $"1:KID:{kid}", plain, encrypted);
            var bytes = await File.ReadAllBytesAsync(encrypted);
            var offsets = new List<int>();
            for (var offset = 0; offset < bytes.Length;)
            {
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                    offsets.Add(offset);
                offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            }
            Assert.True(offsets.Count >= 3);
            var init = Path.Combine(root, "encrypted-init.mp4");
            await File.WriteAllBytesAsync(init, bytes[..offsets[0]]);
            var decrypted = new List<string>();
            for (var i = 0; i < offsets.Count; i++)
            {
                var source = Path.Combine(root, $"encrypted-{i}.m4s");
                var dest = Path.Combine(root, $"decrypted-{i}.m4s");
                await File.WriteAllBytesAsync(source, bytes[offsets[i]..(i + 1 < offsets.Count ? offsets[i + 1] : bytes.Length)]);
                Assert.True(await MP4DecryptUtil.DecryptAsync(DecryptEngine.FFMPEG, "ffmpeg", [$"{kid}:{key}"], source, dest, kid, init));
                decrypted.Add(dest);
            }
            var output = Path.Combine(root, "merged.mp4");
            MergeUtil.CombineMultipleFilesIntoSingleFile([..decrypted], output);
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(output));
            // 逐包比较解密数据、PTS/DTS 和时长，避免只验证可播放而漏掉分片时钟归零。
            Assert.Equal(await Packets(plain), await Packets(output));
            Assert.Equal(await DecodedHash(plain), await DecodedHash(output));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<string> Packets(string path) => Run("ffprobe", "-v", "error", "-show_packets",
        "-show_data_hash", "sha256", "-show_entries", "packet=pts_time,dts_time,duration_time,data_hash", "-of", "compact", path);

    private static Task<string> DecodedHash(string path) => Run("ffmpeg", "-v", "error", "-xerror", "-i", path,
        "-f", "hash", "-hash", "sha256", "-");
}
