using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using System.Globalization;
using System.Text;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.Enum;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodMultiInitTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HlsDownloadsCorrectInitForEachPartAndHandlesAesKeyChanges(bool encrypted, bool missingMedia)
    {
        // 跟随现有 FFmpeg 集成测试约定，无工具的环境只运行解析/过滤测试。
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-multi-init-").FullName;
        try
        {
            for (var i = 0; i < 2; i++)
            {
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
                    $"color=c={(i == 0 ? "red" : "blue")}:s={(i == 0 ? "160x90" : "128x72")}:r=25",
                    "-t", "2", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls",
                    "-hls_time", "2", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", $"init-{i}.mp4",
                    "-hls_segment_filename", Path.Combine(root, $"segment-{i}-%d.m4s"), Path.Combine(root, $"part-{i}.m3u8"));
                if (encrypted)
                {
                    var key = Enumerable.Repeat((byte)(i + 1), 16).ToArray();
                    await File.WriteAllBytesAsync(Path.Combine(root, $"key-{i}"), key);
                    using var aes = Aes.Create();
                    aes.Key = key;
                    string[] files = [$"init-{i}.mp4", $"segment-{i}-0.m4s"];
                    foreach (var file in files)
                    {
                        var path = Path.Combine(root, file);
                        var data = await File.ReadAllBytesAsync(path);
                        await File.WriteAllBytesAsync(path, aes.EncryptCbc(data, new byte[16], PaddingMode.PKCS7));
                    }
                }
            }
            if (missingMedia)
                File.Delete(Path.Combine(root, "segment-1-0.m4s"));
            string Key(int i) => encrypted
                ? $"#EXT-X-KEY:METHOD=AES-128,URI=\"{new Uri(Path.Combine(root, $"key-{i}")).AbsoluteUri}\",IV=0x00000000000000000000000000000000" : "";
            var playlist = Path.Combine(root, "vod.m3u8");
            await File.WriteAllTextAsync(playlist, $"""
                #EXTM3U
                #EXT-X-TARGETDURATION:2
                {Key(0)}
                #EXT-X-MAP:URI="init-0.mp4"
                #EXTINF:2,
                segment-0-0.m4s
                #EXT-X-DISCONTINUITY
                {Key(1)}
                #EXT-X-MAP:URI="init-1.mp4"
                #EXTINF:2,
                segment-1-0.m4s
                #EXT-X-ENDLIST
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(playlist);
            var streams = await extractor.ExtractStreamsAsync();
            var options = new MyOption
            {
                SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
                ThreadCount = 2, CheckSegmentsCount = !missingMedia, DelAfterDone = true, DownloadRetryCount = 0,
                NoAnsiColor = true, LogLevel = LogLevel.OFF, AutoSubtitleFix = true,
            };
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                MyOptions = options, DirPrefix = Path.Combine(root, "tmp"),
            }, streams, extractor);
            Assert.Equal(!missingMedia, await manager.StartDownloadAsync());
            // 共 100 帧、4 秒，不能仅检查退出码：旧字节拼接也可能返回成功但只有 2 秒。
            var output = Path.Combine(root, "out", "result.mp4");
            if (missingMedia)
            {
                Assert.False(File.Exists(output));
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "tmp"), "*.mp4", SearchOption.AllDirectories));
                return;
            }
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
                "-show_entries", "format=duration:stream=nb_read_frames", "-of", "json", output));
            Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
                CultureInfo.InvariantCulture), 3.99, 4.05);
            Assert.Equal("100", probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString());
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
            Assert.False(options.BinaryMerge); // 子段的自动合并策略不能修改共享选项。
            Assert.Empty(Directory.Exists(Path.Combine(root, "tmp"))
                ? Directory.GetFiles(Path.Combine(root, "tmp"), "*.mp4", SearchOption.AllDirectories) : []);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(2, false, false, false)]
    [InlineData(1, true, false, false)]
    [InlineData(0, false, true, false)]
    [InlineData(0, false, false, true)]
    [InlineData(0, true, false, true)]
    public async Task DashDecryptsEachPeriodWithOwnKidAndPreservesPto(int engineValue, bool realtime, bool missingKey, bool noKeys)
    {
        var engine = (DecryptEngine)engineValue;
        if (!HasTool("ffmpeg") || !HasTool("ffprobe") || !OnPath("mp4encrypt") || !OnPath("mp4decrypt"))
            return;
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        if (engine == DecryptEngine.SHAKA_PACKAGER && !File.Exists(packager) && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("dash-multi-init-").FullName;
        try
        {
            var periods = new List<string>();
            var keys = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                var full = Path.Combine(root, $"full-{i}.mp4");
                // 后两个 Period 的源时间从 5 秒开始，解密不能把它归零后再套用 PTO。
                var pto = i == 0 ? 0 : 5;
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                    "-t", "2", "-c:v", "libx264", "-g", "50", "-bf", "0", "-video_track_timescale", "1000",
                    "-movflags", "+frag_keyframe+empty_moov+default_base_moof", full);
                if (pto > 0)
                {
                    // FFmpeg 的 empty_moov 会忽略 output_ts_offset；直接构造有效的 tfdt 偏移。
                    var shifted = await File.ReadAllBytesAsync(full);
                    var tfdt = shifted.AsSpan().IndexOf("tfdt"u8);
                    Assert.True(tfdt >= 0);
                    if (shifted[tfdt + 4] == 1)
                        BinaryPrimitives.WriteUInt64BigEndian(shifted.AsSpan(tfdt + 8, 8), (ulong)pto * 1000);
                    else
                        BinaryPrimitives.WriteUInt32BigEndian(shifted.AsSpan(tfdt + 8, 4), (uint)pto * 1000);
                    await File.WriteAllBytesAsync(full, shifted);
                }
                var kid = new string((char)('1' + i), 32);
                var key = new string((char)('a' + i), 32);
                if (i > 0)
                {
                    var enc = Path.Combine(root, $"enc-{i}.mp4");
                    await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random",
                        "--property", $"1:KID:{kid}", full, enc);
                    full = enc;
                    if (!missingKey || i != 2)
                        keys.Add($"{kid}:{key}");
                }
                var bytes = await File.ReadAllBytesAsync(full);
                var offset = 0;
                while (Encoding.ASCII.GetString(bytes, offset + 4, 4) != "moof")
                    offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                await File.WriteAllBytesAsync(Path.Combine(root, $"init-{i}.mp4"), bytes[..offset]);
                await File.WriteAllBytesAsync(Path.Combine(root, $"media-{i}.m4s"), bytes[offset..]);
                periods.Add($"""
                    <Period duration="PT2S"><AdaptationSet mimeType="video/mp4">
                      {(i == 0 ? "" : $"<ContentProtection cenc:default_KID=\"{kid}\" />")}
                      <Representation id="v{i}" bandwidth="10000" codecs="avc1.64001e">
                        <SegmentList duration="2" presentationTimeOffset="{pto}">
                          <Initialization sourceURL="init-{i}.mp4"/><SegmentURL media="media-{i}.m4s"/>
                        </SegmentList>
                      </Representation>
                    </AdaptationSet></Period>
                    """);
            }
            var manifest = Path.Combine(root, "vod.mpd");
            await File.WriteAllTextAsync(manifest, $"""
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="static" mediaPresentationDuration="PT6S">
                  {string.Join('\n', periods)}
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var sources = await extractor.ExtractStreamsAsync();
            var streams = VodStreamPlanner.Build(sources, [sources[0]]);
            VodStreamPlanner.AlignPeriods(streams);
            var options = new MyOption
            {
                SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
                DecryptionEngine = engine, DecryptionBinaryPath = engine switch
                {
                    DecryptEngine.FFMPEG => "ffmpeg", DecryptEngine.SHAKA_PACKAGER => packager, _ => "mp4decrypt",
                },
                Keys = noKeys ? null : keys.ToArray(), MP4RealTimeDecryption = realtime, ThreadCount = 2, CheckSegmentsCount = true,
                DelAfterDone = true, NoAnsiColor = true, LogLevel = LogLevel.OFF,
            };
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                MyOptions = options, DirPrefix = Path.Combine(root, "tmp"),
            }, streams, extractor);
            var expectedSuccess = !missingKey && !noKeys;
            Assert.Equal(expectedSuccess, await manager.StartDownloadAsync());
            var output = Path.Combine(root, "out", "result.mp4");
            if (!expectedSuccess)
            {
                Assert.False(File.Exists(output));
                Assert.NotEmpty(Directory.GetFiles(Path.Combine(root, "tmp"), "*.mp4", SearchOption.AllDirectories));
                Assert.Contains(Directory.GetFiles(Path.Combine(root, "tmp"), "*.mp4", SearchOption.AllDirectories),
                    file => MP4DecryptUtil.GetMP4Info(file, log: false).Scheme == "cenc");
                return;
            }
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
                "-show_entries", "format=duration:stream=nb_read_frames", "-of", "json", output));
            Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
                CultureInfo.InvariantCulture), 5.99, 6.05);
            Assert.Equal("150", probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString());
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
        }
        finally { Directory.Delete(root, true); }
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemovingInternalAdsKeepsLaterFramesAndSharedTrackGaps(bool dash, bool keepAudioGap)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-ad-hole-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls", "-hls_time", "2",
                "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                "-hls_segment_filename", Path.Combine(root, "segment-%d.m4s"), Path.Combine(root, "source.m3u8"));
            var manifest = Path.Combine(root, dash ? "vod.mpd" : "vod.m3u8");
            if (dash)
                await File.WriteAllTextAsync(manifest, """
                    <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6S">
                      <Period duration="PT6S"><AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                        <SegmentList duration="2"><Initialization sourceURL="init.mp4"/>
                          <SegmentURL media="segment-0.m4s"/><SegmentURL media="segment-1.m4s"/><SegmentURL media="segment-2.m4s"/>
                        </SegmentList>
                      </Representation></AdaptationSet></Period>
                    </MPD>
                    """);
            else
                File.Copy(Path.Combine(root, "source.m3u8"), manifest);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            FilterUtil.CleanAd(streams, ["segment-1"]);
            Assert.Equal(2, streams[0].Playlist!.MediaParts.Count);
            if (dash)
            {
                // 第二轨道保留全区间时，视频中间缺片必须保留两秒空白。
                var alignment = streams.ToList();
                if (keepAudioGap)
                    alignment.Add(new StreamSpec
                {
                    Playlist = new() { MediaParts = [new() { PeriodIndex = 0, PeriodDuration = 6,
                        MediaSegments = [new() { PresentationTime = 0, Duration = 6 }] }] },
                });
                VodStreamPlanner.AlignPeriods(alignment);
            }
            else
                VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                DirPrefix = Path.Combine(root, "tmp"), MyOptions = new MyOption
                {
                    SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
                    ThreadCount = 2, CheckSegmentsCount = true, NoAnsiColor = true, LogLevel = LogLevel.OFF,
                },
            }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var output = Path.Combine(root, "out", "result.mp4");
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
                "-show_entries", "format=duration:stream=nb_read_frames", "-of", "json", output));
            var expectedDuration = keepAudioGap ? 6 : 4;
            Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
                CultureInfo.InvariantCulture), expectedDuration - 0.01, expectedDuration + 0.05);
            Assert.Equal("100", probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString());
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
        }
        finally { Directory.Delete(root, true); }
    }


    [Fact]
    public async Task LateAudioKeepsItsPeriodOffsetThroughFinalFfmpegMux()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-late-audio-").FullName;
        try
        {
            var video = Path.Combine(root, "video.mp4");
            var audio = Path.Combine(root, "audio.m4a");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                "-t", "4", "-c:v", "libx264", "-bf", "0", video);
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000",
                "-t", "2", "-c:a", "aac", audio);
            var delayed = Path.Combine(root, "delayed.m4a");
            Assert.True(MergeUtil.ConcatMediaParts("ffmpeg", [audio], [new()
            {
                PeriodIndex = 1, OutputStart = 2, OutputDuration = 2, OutputInpoint = 0,
                MediaSegments = [new() { Duration = 2 }],
            }], delayed));
            var muxed = Path.Combine(root, "muxed");
            Assert.True(MergeUtil.MuxInputsByFFmpeg("ffmpeg", [new() { Index = 0, FilePath = video },
                new() { Index = 1, FilePath = delayed, PreserveTimestamp = true, MediaType = MediaType.AUDIO }],
                muxed, MuxFormat.MP4, false));
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error",
                "-show_entries", "stream=codec_type,start_time,duration", "-of", "json", muxed + ".mp4"));
            var track = probe.RootElement.GetProperty("streams").EnumerateArray().Single(t => t.GetProperty("codec_type").GetString() == "audio");
            Assert.InRange(double.Parse(track.GetProperty("start_time").GetString()!, CultureInfo.InvariantCulture), 1.95, 2.05);
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", muxed + ".mp4", "-f", "null", "-");
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AudioConfigurationChangesFailAndKeepPartOutputs(bool changeChannels)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-audio-config-").FullName;
        try
        {
            for (var i = 0; i < 2; i++)
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=frequency=440",
                    "-t", "1", "-c:a", "aac", "-ac", changeChannels && i == 1 ? "2" : "1",
                    "-ar", !changeChannels && i == 1 ? "48000" : "44100", "-f", "hls", "-hls_time", "1",
                    "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", $"init-{i}.mp4",
                    "-hls_segment_filename", Path.Combine(root, $"media-{i}-%d.m4s"), Path.Combine(root, $"part-{i}.m3u8"));
            var manifest = Path.Combine(root, "vod.m3u8");
            await File.WriteAllTextAsync(manifest, """
                #EXTM3U
                #EXT-X-MAP:URI="init-0.mp4"
                #EXTINF:1,
                media-0-0.m4s
                #EXT-X-DISCONTINUITY
                #EXT-X-MAP:URI="init-1.mp4"
                #EXTINF:1,
                media-1-0.m4s
                #EXT-X-ENDLIST
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                DirPrefix = Path.Combine(root, "tmp"), MyOptions = new MyOption
                {
                    SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
                    ThreadCount = 2, CheckSegmentsCount = true, DelAfterDone = true, NoAnsiColor = true, LogLevel = LogLevel.OFF,
                },
            }, streams, extractor);
            await Assert.ThrowsAsync<NotSupportedException>(() => manager.StartDownloadAsync());
            Assert.False(File.Exists(Path.Combine(root, "out", "result.mp4")));
            Assert.Equal(2, Directory.GetFiles(Path.Combine(root, "tmp"), "part.*", SearchOption.AllDirectories)
                .Count(path => Path.GetExtension(path) is ".mp4" or ".m4a"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DashSubtitlesPreserveSilentTailsAndUseEachPeriodsPto()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-subtitles-").FullName;
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "first.vtt"), "WEBVTT\n\n00:00:00.250 --> 00:00:01.000\nfirst\n\n");
            await File.WriteAllTextAsync(Path.Combine(root, "second.vtt"), "WEBVTT\n\n00:00:05.500 --> 00:00:08.000\nsecond\n\n");
            var manifest = Path.Combine(root, "vod.mpd");
            await File.WriteAllTextAsync(manifest, """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT4S">
                  <Period duration="PT2S"><AdaptationSet mimeType="text/vtt" lang="en"><Representation id="s0">
                    <SegmentList duration="2"><SegmentURL media="first.vtt"/></SegmentList>
                  </Representation></AdaptationSet></Period>
                  <Period duration="PT2S"><AdaptationSet mimeType="text/vtt" lang="en"><Representation id="s1">
                    <SegmentList duration="2" presentationTimeOffset="5"><SegmentURL media="second.vtt"/></SegmentList>
                  </Representation></AdaptationSet></Period>
                </MPD>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var sources = await extractor.ExtractStreamsAsync();
            var streams = VodStreamPlanner.Build(sources, [sources[0]]);
            VodStreamPlanner.AlignPeriods(streams);
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                DirPrefix = Path.Combine(root, "tmp"), MyOptions = new MyOption
                {
                    SaveDir = Path.Combine(root, "out"), SaveName = "subs", FFmpegBinaryPath = "ffmpeg",
                    AutoSubtitleFix = true, SubtitleFormat = SubtitleFormat.VTT, NoAnsiColor = true,
                    ThreadCount = 2, CheckSegmentsCount = true,
                },
            }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "subs.en.vtt")));
            Assert.Equal(2, sub.Cues.Count);
            Assert.Equal(0.25, sub.Cues[0].StartTime.TotalSeconds);
            Assert.Equal(2.5, sub.Cues[1].StartTime.TotalSeconds);
            Assert.Equal(4, sub.Cues[1].EndTime.TotalSeconds);
        }
        finally { Directory.Delete(root, true); }
    }
}
