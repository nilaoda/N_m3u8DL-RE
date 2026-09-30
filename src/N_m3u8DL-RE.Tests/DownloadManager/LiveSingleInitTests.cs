using N_m3u8DL_RE.Common.Enum;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public partial class VodMultiInitTests
{
    [Theory]
    [InlineData("hls-ts", "clear", true, "normal")]
    [InlineData("hls-ts", "clear", false, "normal")]
    [InlineData("hls-ts", "aes", true, "normal")]
    [InlineData("hls-ts", "aes", false, "normal")]
    [InlineData("hls-mp4", "clear", true, "normal")]
    [InlineData("hls-mp4", "clear", false, "normal")]
    [InlineData("hls-mp4", "aes", true, "normal")]
    [InlineData("hls-mp4", "aes", false, "normal")]
    [InlineData("hls-mp4", "cenc", true, "normal")]
    [InlineData("hls-mp4", "cenc", false, "normal")]
    [InlineData("hls-mp4", "clear", true, "initial-empty")]
    [InlineData("hls-mp4", "clear", true, "end-empty")]
    [InlineData("dash-mp4", "clear", true, "normal")]
    [InlineData("dash-mp4", "cenc", true, "normal")]
    public async Task LiveRefreshKeepsSingleInitAndRecordsEverySegment(string format, string encryption, bool realtimeMerge, string scenario)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        if (encryption == "cenc" && (!OnPath("mp4encrypt") || !OnPath("mp4decrypt")))
            return;
        var root = Directory.CreateTempSubdirectory("live-single-init-").FullName;
        try
        {
            var ts = format == "hls-ts";
            var dash = format == "dash-mp4";
            var extension = ts ? "ts" : "m4s";
            var kid = new string('1', 32); var key = new string('a', 32);
            if (encryption == "cenc")
            {
                var full = Path.Combine(root, "full.mp4");
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                    "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-video_track_timescale", "1000",
                    "-movflags", "+frag_keyframe+empty_moov+default_base_moof", full);
                var encrypted = Path.Combine(root, "encrypted.mp4");
                await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{key}:random", "--property", $"1:KID:{kid}", full, encrypted);
                var bytes = await File.ReadAllBytesAsync(encrypted);
                var offsets = new List<int>();
                for (var offset = 0; offset < bytes.Length;)
                {
                    if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                        offsets.Add(offset);
                    offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                }
                Assert.Equal(3, offsets.Count);
                await File.WriteAllBytesAsync(Path.Combine(root, "init.mp4"), bytes[..offsets[0]]);
                for (var i = 0; i < 3; i++)
                    await File.WriteAllBytesAsync(Path.Combine(root, $"media-{i}.m4s"), bytes[offsets[i]..(i == 2 ? bytes.Length : offsets[i + 1])]);
            }
            else
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                    "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-t", "6", "-c:v", "libx264",
                    "-g", "50", "-bf", "0", "-c:a", "aac", "-f", "hls", "-hls_time", "2",
                    "-hls_segment_type", ts ? "mpegts" : "fmp4", "-hls_fmp4_init_filename", "init.mp4",
                    "-hls_segment_filename", Path.Combine(root, $"media-%d.{extension}"), Path.Combine(root, "source.m3u8"));
            Assert.Equal(3, Directory.GetFiles(root, $"media-*.{extension}").Length);
            var plaintext = Directory.GetFiles(root, $"media-*.{extension}")
                .Concat(ts ? [] : [Path.Combine(root, "init.mp4")])
                .ToDictionary(f => Path.GetFileName(f)!, File.ReadAllBytes);
            if (encryption == "aes")
            {
                var aesKey = Enumerable.Repeat((byte)0x42, 16).ToArray();
                await File.WriteAllBytesAsync(Path.Combine(root, "key.bin"), aesKey);
                using var aes = Aes.Create(); aes.Key = aesKey;
                foreach (var file in plaintext)
                    await File.WriteAllBytesAsync(Path.Combine(root, file.Key!), aes.EncryptCbc(file.Value, new byte[16], PaddingMode.PKCS7));
            }
            if (encryption == "cenc")
                await File.WriteAllBytesAsync(Path.Combine(root, "key.bin"), Convert.FromHexString(key));
            await using var server = new LiveFixtureServer(root, (path, version) =>
            {
                if (path != (dash ? "live.mpd" : "live.m3u8"))
                    return null;
                // HTTP 请求驱动滑动窗口：首轮 0，刷新 0/1，最后 1/2。
                // 这样验证真实刷新/去重/收尾，不依赖计时写文件或直接注入分片队列。
                var step = scenario == "initial-empty" ? version - 1 : version;
                var start = step >= 2 ? 1 : 0;
                var end = Math.Min(step, scenario == "end-empty" ? 1 : 2);
                var ended = step >= 2;
                if (dash)
                    return $"""
                        <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" xmlns:cenc="urn:mpeg:cenc:2013" type="dynamic" minimumUpdatePeriod="PT1S" mediaPresentationDuration="PT6S">
                          <Period id="p0" start="PT0S" duration="PT6S"><AdaptationSet mimeType="video/mp4">
                            {(encryption == "cenc" ? $"<ContentProtection cenc:default_KID=\"{kid}\"/>" : "")}
                            <Representation id="v" codecs="avc1.64001e"><SegmentList duration="2"><Initialization sourceURL="init.mp4"/>
                              {string.Join('\n', Enumerable.Range(0, Math.Min(version + 1, 3)).Select(i => $"<SegmentURL media=\"media-{i}.m4s\"/>"))}
                            </SegmentList></Representation>
                          </AdaptationSet></Period>
                        </MPD>
                        """;
                var content = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:" + start + "\n";
                if (encryption == "aes")
                    content += "#EXT-X-KEY:METHOD=AES-128,URI=\"key.bin\",IV=0x00000000000000000000000000000000\n";
                if (encryption == "cenc")
                    content += "#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,URI=\"key.bin\"\n";
                if (!ts)
                    content += "#EXT-X-MAP:URI=\"init.mp4\"\n";
                if (!(scenario == "end-empty" && ended))
                    for (var i = start; i <= end; i++) content += $"#EXTINF:2,\nmedia-{i}.{extension}\n";
                if (ended)
                    content += "#EXT-X-ENDLIST\n";
                return content;
            });
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + (dash ? "live.mpd" : "live.m3u8"));
            var streams = await extractor.ExtractStreamsAsync();
            Assert.True(streams[0].Playlist!.IsLive);
            var init = streams[0].Playlist!.MediaParts[0].MediaInit;
            Assert.Equal(!ts, init != null);
            FilterUtil.CleanAd(streams, null); // 与 Program 的直播初始化流程一致。
            var options = LegacyOptions(root);
            options.LiveRealTimeMerge = realtimeMerge;
            options.LiveWaitTime = 1;
            options.LiveTakeCount = 16;
            options.LiveIdleTimeout = 6;
            options.LiveRecordLimit = TimeSpan.FromSeconds(6);
            options.DecryptionBinaryPath = "mp4decrypt";
            if (encryption == "cenc")
                options.Keys = [$"{kid}:{key}"];
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            var mediaCount = scenario == "end-empty" ? 2 : 3;
            Assert.True(server.RequestCount(dash ? "live.mpd" : "live.m3u8") >= 3);
            Assert.Equal(ts ? 0 : 1, server.RequestCount("init.mp4"));
            for (var i = 0; i < mediaCount; i++) Assert.Equal(1, server.RequestCount($"media-{i}.{extension}"));
            if (!ts && scenario != "end-empty")
            {
                Assert.Equal(init!.Url, streams[0].Playlist!.MediaParts[0].MediaInit!.Url);
                // 明文/固定 IV 的 AES MAP 刷新复用对象；DASH 会重建对象。
                // CENC 未声明 IV 时，解析器的默认 IV 随窗口序号变化，也可能重建对象；
                // 消费者须继续持有已下载 init，以上请求计数及下方像素比较验证实际行为。
                if (!dash && encryption != "cenc")
                    Assert.Same(init, streams[0].Playlist!.MediaParts[0].MediaInit);
            }
            if (realtimeMerge)
            {
                var output = Assert.Single(Directory.GetFiles(Path.Combine(root, "out")));
                await AssertVideo(output, mediaCount * 2, mediaCount * 50);
                if (encryption == "cenc")
                    Assert.Equal(await LiveFrameHashes(Path.Combine(root, "full.mp4")), await LiveFrameHashes(output));
                if (encryption != "cenc")
                {
                    using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-show_entries", "stream=codec_type", "-of", "json", output));
                    Assert.Contains(probe.RootElement.GetProperty("streams").EnumerateArray(), s => s.GetProperty("codec_type").GetString() == "audio");
                }
            }
            else
            {
                Assert.Empty(Directory.GetFiles(Path.Combine(root, "out")));
                var files = Directory.GetFiles(Path.Combine(root, "tmp"), "*", SearchOption.AllDirectories);
                // 默认录制保留分片；用保留的 init 和媒体组装检查，不把测试组装当作程序最终输出。
                var media = files.Where(f => !Path.GetFileName(f).StartsWith("_init")).OrderBy(f => f).ToArray();
                Assert.Equal(3, media.Length);
                if (encryption != "cenc")
                    for (var i = 0; i < 3; i++) Assert.Equal(plaintext[$"media-{i}.{extension}"], await File.ReadAllBytesAsync(media[i]));
                var initPath = ts ? null : Assert.Single(files, f => Path.GetFileNameWithoutExtension(f).StartsWith("_init") && f.Contains("_dec") == (encryption == "cenc"));
                var check = Path.Combine(root, "retained-check." + (ts ? "ts" : "mp4"));
                MergeUtil.CombineMultipleFilesIntoSingleFile(initPath == null ? media : [initPath, ..media], check);
                await AssertVideo(check, 6, 150);
                if (encryption == "cenc")
                    Assert.Equal(await LiveFrameHashes(Path.Combine(root, "full.mp4")), await LiveFrameHashes(check));
            }
        }
        finally { Directory.Delete(root, true); }
    }


    [Fact]
    public async Task LiveMssGeneratedInitStillMatchesDownloadedTrack()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-mss-init-").FullName;
        try
        {
            var full = Path.Combine(root, "full.mp4");
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "color=s=160x90:r=25",
                "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-video_track_timescale", "1000",
                "-movflags", "+frag_keyframe+empty_moov+default_base_moof", full);
            var bytes = await File.ReadAllBytesAsync(full);
            var offsets = new List<int>();
            for (var offset = 0; offset < bytes.Length;)
            {
                if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                    offsets.Add(offset);
                offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
            }
            Assert.Equal(3, offsets.Count);
            for (var i = 0; i < 3; i++)
                await File.WriteAllBytesAsync(Path.Combine(root, $"media-{i * 2000}.m4s"), bytes[offsets[i]..(i == 2 ? bytes.Length : offsets[i + 1])]);
            // 从真实编码的 avcC 提取 SPS/PPS，生成 Smooth Streaming CodecPrivateData。
            // 原 init 的 track ID 为 1；MSS 生成器默认值为 2，录制器须按首片重建 init。
            var avcc = bytes.AsSpan().IndexOf("avcC"u8) + 4;
            Assert.True(avcc >= 4);
            var codecData = new StringBuilder();
            var nalOffset = avcc + 6;
            void ReadNals(int count)
            {
                for (var i = 0; i < count; i++)
                {
                    var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(nalOffset, 2));
                    nalOffset += 2;
                    codecData.Append("00000001").Append(Convert.ToHexString(bytes.AsSpan(nalOffset, length)));
                    nalOffset += length;
                }
            }
            ReadNals(bytes[avcc + 5] & 0x1f);
            var ppsCount = bytes[nalOffset++];
            ReadNals(ppsCount);
            await using var server = new LiveFixtureServer(root, (path, version) => path != "live.ism" ? null : $$"""
                <SmoothStreamingMedia MajorVersion="2" MinorVersion="1" IsLive="TRUE" Duration="6000" TimeScale="1000">
                  <StreamIndex Type="video" Name="video" TimeScale="1000" Url="media-{start time}.m4s">
                    <QualityLevel Index="0" Bitrate="10000" FourCC="H264" MaxWidth="160" MaxHeight="90" CodecPrivateData="{{codecData}}"/>
                    <c t="0" d="2000" r="{{Math.Min(version + 1, 3)}}"/>
                  </StreamIndex>
                </SmoothStreamingMedia>
                """);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.ism");
            var streams = await extractor.ExtractStreamsAsync();
            Assert.True(streams[0].Playlist!.IsLive);
            Assert.StartsWith("base64://", streams[0].Playlist!.MediaParts[0].MediaInit!.Url);
            FilterUtil.CleanAd(streams, null);
            var options = LegacyOptions(root);
            options.LiveRealTimeMerge = true; options.LiveWaitTime = 1; options.LiveTakeCount = 16;
            options.LiveRecordLimit = TimeSpan.FromSeconds(6); options.LiveIdleTimeout = 6;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            Assert.True(server.RequestCount("live.ism") >= 3);
            for (var i = 0; i < 3; i++) Assert.Equal(1, server.RequestCount($"media-{i * 2000}.m4s"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 150);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task LiveNoInitTextSubtitlesKeepAllCuesAfterRefresh()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("live-text-vtt-").FullName;
        try
        {
            for (var i = 0; i < 3; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"),
                    $"WEBVTT\n\n00:00:0{i * 2}.250 --> 00:00:0{i * 2 + 1}.000\ncue-{i}\n\n");
            await using var server = new LiveFixtureServer(root, (path, version) => path != "live.m3u8" ? null :
                "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n" +
                string.Join('\n', Enumerable.Range(0, Math.Min(version + 1, 3)).Select(i => $"#EXTINF:2,\nsub-{i}.vtt")) +
                (version >= 2 ? "\n#EXT-X-ENDLIST\n" : "\n"));
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "live.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            streams[0].MediaType = MediaType.SUBTITLES;
            streams[0].Extension = "vtt";
            Assert.Null(streams[0].Playlist!.MediaParts[0].MediaInit);
            FilterUtil.CleanAd(streams, null);
            var options = LegacyOptions(root);
            options.SubtitleFormat = SubtitleFormat.VTT;
            options.LiveRealTimeMerge = true; options.LiveWaitTime = 1; options.LiveTakeCount = 16;
            options.LiveRecordLimit = TimeSpan.FromSeconds(6); options.LiveIdleTimeout = 6;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(12)));
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.vtt")));
            Assert.Equal(3, sub.Cues.Count);
            for (var i = 0; i < 3; i++)
            {
                Assert.Equal($"cue-{i}", sub.Cues[i].Payload);
                Assert.Equal(i * 2 + 0.25, sub.Cues[i].StartTime.TotalSeconds);
                Assert.Equal(i * 2 + 1, sub.Cues[i].EndTime.TotalSeconds);
                Assert.Equal(1, server.RequestCount($"sub-{i}.vtt"));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<string[]> LiveFrameHashes(string path)
    {
        // 加密用例还比较解码后的逐帧像素，避免简单画面使漏解密的问题未被发现。
        var output = await Run("ffmpeg", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-f", "framemd5", "-");
        return output.Split('\n').Where(line => !line.StartsWith('#') && line.Contains(','))
            .Select(line => line.Split(',')[^1].Trim()).ToArray();
    }

    private sealed class LiveFixtureServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource stop = new();
        private readonly ConcurrentDictionary<string, int> requests = new();
        private readonly List<Task> handlers = [];
        private readonly Task loop;
        public string Url { get; }
        public int RequestCount(string path) => requests.GetValueOrDefault(path);

        public LiveFixtureServer(string root, Func<string, int, string?> manifest, Func<string, Task>? beforeResponse = null)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            loop = Task.Run(async () =>
            {
                try
                {
                    while (!stop.IsCancellationRequested)
                    {
                        var client = await listener.AcceptTcpClientAsync(stop.Token);
                        handlers.Add(Serve(client));
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            });
            async Task Serve(TcpClient client)
            {
                using (client)
                {
                    await using var stream = client.GetStream();
                    using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                    var request = await reader.ReadLineAsync(stop.Token);
                    if (request == null)
                        return;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                    var path = new Uri(Url + request.Split(' ')[1].TrimStart('/')).AbsolutePath.TrimStart('/');
                    var count = requests.AddOrUpdate(path, 1, (_, old) => old + 1);
                    if (beforeResponse != null)
                        await beforeResponse(path);
                    var text = manifest(path, count - 1);
                    var bytes = text != null ? Encoding.UTF8.GetBytes(text) : await File.ReadAllBytesAsync(Path.Combine(root, path), stop.Token);
                    var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(headers, stop.Token);
                    await stream.WriteAsync(bytes, stop.Token);
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            stop.Cancel(); listener.Stop();
            await loop;
            await Task.WhenAll(handlers);
            stop.Dispose();
        }
    }
}
