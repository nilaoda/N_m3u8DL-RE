using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Util;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class HlsSubtitleTimelineTests
{
    [Theory]
    [InlineData(false, "none", "MP4DECRYPT", false)]
    [InlineData(false, "relative", "MP4DECRYPT", false)]
    [InlineData(true, "none", "MP4DECRYPT", false)]
    [InlineData(true, "none", "MP4DECRYPT", true)]
    [InlineData(true, "aes", "MP4DECRYPT", false)]
    [InlineData(true, "missing", "MP4DECRYPT", false)]
    [InlineData(true, "cancel", "MP4DECRYPT", true)]
    [InlineData(true, "cenc", "MP4DECRYPT", false)]
    [InlineData(true, "cenc", "FFMPEG", false)]
    [InlineData(true, "refresh", "MP4DECRYPT", false)]
    [InlineData(true, "cenc-refresh", "FFMPEG", false)]
    [InlineData(true, "cenc", "SHAKA_PACKAGER", false)]
    public async Task LiveFmp4AudioClockSurvivesEncryptionAndSlowRequests(bool timestampMap, string encryption, string engine, bool slowFirst)
    {
        if (!HasTool("ffmpeg"))
            return;
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        var cenc = encryption.StartsWith("cenc", StringComparison.Ordinal);
        var refresh = encryption is "refresh" or "cenc-refresh";
        if (cenc && (!OnPath("mp4encrypt") || engine == "MP4DECRYPT" && !OnPath("mp4decrypt")))
            return;
        if (engine == "SHAKA_PACKAGER" && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("live-fmp4-subtitle-clock-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "sine=sample_rate=48000",
                "-t", "6", "-c:a", "aac", "-output_ts_offset", "36000", "-f", "hls", "-hls_time", "2",
                "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                "-hls_segment_filename", Path.Combine(root, "audio-%d.m4s"), Path.Combine(root, "audio.m3u8"));
            var plainAudio = Path.Combine(root, "plain-audio.mp4");
            MergeUtil.CombineMultipleFilesIntoSingleFile([Path.Combine(root, "init.mp4"),
                .. Directory.GetFiles(root, "audio-*.m4s").Order()], plainAudio);
            var expectedAudioHash = await Run("ffmpeg", "-v", "error", "-i", plainAudio, "-map", "0:a:0", "-f", "hash", "-hash", "sha256", "-");
            var subtitles = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
            for (var i = 0; i < 3; i++)
            {
                var start = timestampMap ? 2672424094L : encryption == "relative" ? 0L : 36000000L;
                var sub = new WebVttSub { Cues = [new SubCue
                {
                    StartTime = TimeSpan.FromTicks((start + i * 2000 + 250) * TimeSpan.TicksPerMillisecond),
                    EndTime = TimeSpan.FromTicks((start + i * 2000 + 1000) * TimeSpan.TicksPerMillisecond),
                    Payload = $"cue-{i}", Settings = "",
                }] };
                var text = sub.ToVtt();
                if (timestampMap)
                    text = text.Replace("WEBVTT", "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:742:20:24.094,MPEGTS:3240000000");
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"), text);
                subtitles += $"#EXTINF:2,\nsub-{i}.vtt\n";
            }
            await File.WriteAllTextAsync(Path.Combine(root, "subs.m3u8"), subtitles + "#EXT-X-ENDLIST\n");
            if (encryption == "aes" || cenc)
                await EncryptMedia(root, "audio.m3u8", "audio-*.m4s", cenc ? "cenc" : encryption);
            if (encryption == "aes")
                await EncryptMedia(root, "subs.m3u8", "sub-*.vtt", encryption);
            await File.WriteAllTextAsync(Path.Combine(root, "master.m3u8"), """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",NAME="English",LANGUAGE="en",URI="subs.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="mp4a.40.2",SUBTITLES="s"
                audio.m3u8
                """);
            await using var server = new MediaFixtureServer(root, (path, version) =>
            {
                if (path is not ("audio.m3u8" or "subs.m3u8"))
                    return null;
                var text = File.ReadAllText(Path.Combine(root, path)).Replace("#EXT-X-ENDLIST", "");
                if (refresh)
                {
                    // 每次刷新只发布一个新分片，验证跨批次时钟、字幕累积和 init 复用。
                    var entries = text.Split("#EXTINF:");
                    text = entries[0] + string.Concat(entries.Skip(1).Take(version + 1).Select(entry => "#EXTINF:" + entry));
                }
                return text + (version >= (refresh ? 3 : 1) ? "#EXT-X-ENDLIST\n" : "");
            },
                // 分别覆盖首片和后续音频慢请求，首批字幕都必须等待正确的源时钟。
                beforeResponse: path => encryption is "none" or "cancel" && timestampMap && path == (slowFirst ? "audio-0.m4s" : "audio-1.m4s")
                    ? Task.Delay(6000) : Task.CompletedTask,
                responseStatus: (path, _) => encryption == "missing" && path.StartsWith("audio-") ? HttpStatusCode.Gone : null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            await extractor.FetchPlayListAsync(streams);
            streams.Single(stream => stream.MediaType != MediaType.SUBTITLES).MediaType = MediaType.AUDIO;
            var options = CreateOptions(root);
            options.LiveFixVttByAudio = true;
            options.LiveRealTimeMerge = true;
            options.LiveWaitTime = 1;
            options.LiveIdleTimeout = 10;
            options.HttpRequestTimeout = 10;
            options.HttpRequestTimeoutSpecified = true;
            options.SubtitleFormat = SubtitleFormat.VTT;
            options.DecryptionEngine = engine switch
            {
                "FFMPEG" => DecryptEngine.FFMPEG, "SHAKA_PACKAGER" => DecryptEngine.SHAKA_PACKAGER, _ => DecryptEngine.MP4DECRYPT,
            };
            options.DecryptionBinaryPath = engine switch
            {
                "FFMPEG" => "ffmpeg", "SHAKA_PACKAGER" => packager, _ => "mp4decrypt",
            };
            if (cenc)
                options.Keys = [$"{new string('1', 32)}:{new string('a', 32)}"];
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            using var stop = new CancellationTokenSource();
            if (encryption == "cancel")
                stop.CancelAfter(TimeSpan.FromSeconds(2));
            Assert.Equal(encryption != "missing", await manager.StartRecordAsync(stop.Token).WaitAsync(TimeSpan.FromSeconds(15)));
            if (encryption == "cancel")
                return;
            var output = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt")));
            Assert.Equal(["cue-0", "cue-1", "cue-2"], output.Cues.Select(c => c.Payload));
            for (var i = 0; i < 3; i++)
            {
                var start = encryption == "missing" ? 2672424.344 : encryption == "relative" ? 0.25 : 0.271;
                Assert.InRange(output.Cues[i].StartTime.TotalSeconds, i * 2 + start - 0.002, i * 2 + start + 0.002);
                Assert.InRange(output.Cues[i].EndTime.TotalSeconds, i * 2 + start + 0.748, i * 2 + start + 0.752);
            }
            if (encryption != "missing")
            {
                var audio = Assert.Single(Directory.GetFiles(Path.Combine(root, "out")), file => file.EndsWith(".m4a"));
                Assert.False(MP4DecryptUtil.HasEncryptedTracks(audio));
                Assert.Equal(expectedAudioHash, await Run("ffmpeg", "-v", "error", "-xerror", "-i", audio,
                    "-map", "0:a:0", "-f", "hash", "-hash", "sha256", "-"));
            }
            Assert.Equal(1, server.RequestCount("init.mp4"));
            if (refresh)
            {
                Assert.True(server.RequestCount("audio.m3u8") >= 4);
                Assert.True(server.RequestCount("subs.m3u8") >= 4);
                for (var i = 0; i < 3; i++)
                    Assert.Equal(1, server.RequestCount($"sub-{i}.vtt"));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("none", "MP4DECRYPT")]
    [InlineData("aes", "MP4DECRYPT")]
    [InlineData("cenc", "MP4DECRYPT")]
    [InlineData("cenc", "FFMPEG")]
    [InlineData("cenc", "SHAKA_PACKAGER")]
    public async Task BroadcastClockMapsToMediaAndClipsCuesBeforeTheStart(string encryption, string engine)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        if (encryption == "cenc" && (!OnPath("mp4encrypt") || engine == "MP4DECRYPT" && !OnPath("mp4decrypt")))
            return;
        if (engine == "SHAKA_PACKAGER" && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("hls-broadcast-subtitles-").FullName;
        try
        {
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-output_ts_offset", "2758898.142",
                "-f", "hls", "-hls_time", "2", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                "-hls_segment_filename", Path.Combine(root, "media-%d.m4s"), Path.Combine(root, "video.m3u8"));
            var plainVideo = Path.Combine(root, "plain-video.mp4");
            MergeUtil.CombineMultipleFilesIntoSingleFile([Path.Combine(root, "init.mp4"),
                .. Directory.GetFiles(root, "media-*.m4s").Order()], plainVideo);
            var expectedFrames = await FrameHashes(plainVideo);
            if (encryption != "none")
                await EncryptMedia(root, "video.m3u8", "media-*.m4s", encryption);
            var subtitles = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
            for (var i = 0; i < 3; i++)
            {
                var sub = new WebVttSub();
                if (i == 0)
                {
                    AddCue(-2000, -1000, "before");
                    AddCue(-100, 100, "crossing");
                }
                AddCue(i * 2000 + 250, i * 2000 + 1000, $"cue-{i}");
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"), sub.ToVtt().Replace("WEBVTT",
                    "WEBVTT\nX-TIMESTAMP-MAP=LOCAL:742:20:24.094,MPEGTS:3000"));
                subtitles += $"#EXTINF:2,\nsub-{i}.vtt\n";

                void AddCue(long start, long end, string payload) => sub.Cues.Add(new SubCue
                {
                    StartTime = TimeSpan.FromTicks((2758898142L + start) * TimeSpan.TicksPerMillisecond),
                    EndTime = TimeSpan.FromTicks((2758898142L + end) * TimeSpan.TicksPerMillisecond),
                    Payload = payload,
                    Settings = "",
                });
            }
            await File.WriteAllTextAsync(Path.Combine(root, "subs.m3u8"), subtitles + "#EXT-X-ENDLIST\n");
            var master = Path.Combine(root, "master.m3u8");
            await File.WriteAllTextAsync(master, """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",NAME="English",LANGUAGE="en",URI="subs.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e",SUBTITLES="s"
                video.m3u8
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(master);
            var streams = await extractor.ExtractStreamsAsync();
            await extractor.FetchPlayListAsync(streams);
            VodStreamPlanner.CaptureHlsTimeline(streams);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            var options = CreateOptions(root);
            options.SubtitleFormat = SubtitleFormat.VTT;
            options.ConcurrentDownload = true;
            options.DecryptionEngine = engine switch
            {
                "FFMPEG" => DecryptEngine.FFMPEG, "SHAKA_PACKAGER" => DecryptEngine.SHAKA_PACKAGER, _ => DecryptEngine.MP4DECRYPT,
            };
            options.DecryptionBinaryPath = engine switch
            {
                "FFMPEG" => "ffmpeg", "SHAKA_PACKAGER" => packager, _ => "mp4decrypt",
            };
            if (encryption == "cenc")
                options.Keys = [$"{new string('1', 32)}:{new string('a', 32)}"];

            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartDownloadAsync());
            var output = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt")));
            Assert.Equal(["crossing", "cue-0", "cue-1", "cue-2"], output.Cues.Select(c => c.Payload));
            Assert.Equal(TimeSpan.Zero, output.Cues[0].StartTime);
            Assert.InRange(output.Cues[0].EndTime.TotalSeconds, 0.133, 0.136);
            for (var i = 0; i < 3; i++)
            {
                Assert.InRange(output.Cues[i + 1].StartTime.TotalSeconds, i * 2 + 0.283, i * 2 + 0.286);
                Assert.InRange(output.Cues[i + 1].EndTime.TotalSeconds, i * 2 + 1.033, i * 2 + 1.036);
            }
            var video = Path.Combine(root, "out", "result.mp4");
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(video));
            Assert.Equal(expectedFrames, await FrameHashes(video));
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
                "-show_entries", "stream=nb_read_frames", "-of", "json", video));
            Assert.Equal("150", probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task EncryptMedia(string root, string playlist, string pattern, string encryption)
    {
        var files = Directory.GetFiles(root, pattern).Order().ToArray();
        var key = Convert.FromHexString(new string('a', 32));
        await File.WriteAllBytesAsync(Path.Combine(root, "key.bin"), key);
        string keyTag;
        if (encryption == "aes")
        {
            using var aes = Aes.Create();
            aes.Key = key;
            var hasInit = (await File.ReadAllTextAsync(Path.Combine(root, playlist))).Contains("#EXT-X-MAP:");
            foreach (var file in files.Concat(hasInit ? [Path.Combine(root, "init.mp4")] : []))
                await File.WriteAllBytesAsync(file, aes.EncryptCbc(await File.ReadAllBytesAsync(file), new byte[16], PaddingMode.PKCS7));
            keyTag = "#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x00000000000000000000000000000000";
        }
        else
        {
            var full = Path.Combine(root, "plain.mp4");
            var encrypted = Path.Combine(root, "encrypted.mp4");
            MergeUtil.CombineMultipleFilesIntoSingleFile([Path.Combine(root, "init.mp4"), ..files], full);
            await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{new string('a', 32)}:random",
                "--property", $"1:KID:{new string('1', 32)}", full, encrypted);
            var bytes = await File.ReadAllBytesAsync(encrypted);
            var offsets = new List<int>();
            for (var offset = 0; offset < bytes.Length;)
            {
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                    offsets.Add(offset);
                offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            }
            Assert.Equal(files.Length, offsets.Count);
            await File.WriteAllBytesAsync(Path.Combine(root, "init.mp4"), bytes[..offsets[0]]);
            for (var i = 0; i < files.Length; i++)
                await File.WriteAllBytesAsync(files[i], bytes[offsets[i]..(i == files.Length - 1 ? bytes.Length : offsets[i + 1])]);
            keyTag = "#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,URI=\"key.bin\"";
        }
        var path = Path.Combine(root, playlist);
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("#EXTM3U", "#EXTM3U\n" + keyTag));
    }
}
