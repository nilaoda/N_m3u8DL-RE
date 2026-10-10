using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class LiveNetworkRecoveryTests
{
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 1, 2, true)]
    [InlineData(HttpStatusCode.Forbidden, 3, 2, false)]
    [InlineData(HttpStatusCode.Forbidden, 1, 0, false)]
    [InlineData(HttpStatusCode.Unauthorized, 1, 2, false)]
    public async Task LiveDownloadRetriesForbiddenWithinConfiguredLimit(HttpStatusCode status, int failures, int retries, bool succeeds)
    {
        var root = Directory.CreateTempSubdirectory("live-forbidden-retry-").FullName;
        try
        {
            await using var server = new MediaFixtureServer(root, (_, _) => "media",
                responseStatus: (_, count) => count <= failures ? status : null);
            var options = CreateOptions(root);
            options.DownloadRetryCount = retries;
            var downloader = new SimpleDownloader(new DownloaderConfig { DirPrefix = root, MyOptions = options });
            var segment = new MediaSegment { Url = server.Url + "media.ts" };
            var download = downloader.DownloadSegmentAsync(segment, Path.Combine(root, "media.ts.tmp"), new SpeedContainer(),
                throwOnFailure: true, networkTimeout: TimeSpan.FromSeconds(2));
            if (succeeds)
            {
                var result = await download.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.True(result?.Success);
                Assert.Equal("media", await File.ReadAllTextAsync(result!.ActualFilePath));
            }
            else
            {
                var error = await Assert.ThrowsAsync<HttpRequestException>(() => download.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.Equal(status, error.StatusCode);
            }
            Assert.Equal(status == HttpStatusCode.Unauthorized ? 1 : Math.Min(failures, retries) + 1,
                server.RequestCount("media.ts"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("manifest", 5, false)]
    [InlineData("init", 5, false)]
    [InlineData("media", 5, false)]
    [InlineData("key", 5, false)]
    [InlineData("manifest", 1, true)]
    [InlineData("init", 1, true)]
    [InlineData("media", 1, true)]
    [InlineData("key", 1, true)]
    public async Task TransientFailureRecoversWithoutLosingOrDuplicatingMedia(string target, int failures, bool stalledBody)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-network-recovery-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var failedPath = target switch { "manifest" => "live.m3u8", "init" => "init.mp4", "key" => "key-b.bin", _ => "media-1.m4s" };
            if (target == "key")
            {
                var keyA = Enumerable.Repeat((byte)0x42, 16).ToArray();
                var keyB = Enumerable.Repeat((byte)0x43, 16).ToArray();
                await File.WriteAllBytesAsync(Path.Combine(root, "key-a.bin"), keyA);
                await File.WriteAllBytesAsync(Path.Combine(root, "key-b.bin"), keyB);
                string[] mediaFiles = ["init.mp4", "media-0.m4s", "media-1.m4s", "media-2.m4s"];
                foreach (var name in mediaFiles)
                {
                    using var aes = Aes.Create();
                    aes.Key = name is "init.mp4" or "media-0.m4s" ? keyA : keyB;
                    var file = Path.Combine(root, name);
                    await File.WriteAllBytesAsync(file, aes.EncryptCbc(await File.ReadAllBytesAsync(file), new byte[16], PaddingMode.PKCS7));
                }
            }
            var offset = target == "manifest" ? 1 : 0;
            await using var server = new MediaFixtureServer(root, (path, version) =>
            {
                var text = Manifest(path, version);
                if (target != "key" || text == null)
                    return text;
                return text.Replace("#EXT-X-MAP:", "#EXT-X-KEY:METHOD=AES-128,URI=\"key-a.bin\",IV=0x00000000000000000000000000000000\n#EXT-X-MAP:")
                    .Replace("#EXTINF:2,\nmedia-1.m4s", "#EXT-X-KEY:METHOD=AES-128,URI=\"key-b.bin\",IV=0x00000000000000000000000000000000\n#EXTINF:2,\nmedia-1.m4s");
            },
                responseStatus: (path, count) => !stalledBody && path == failedPath && count > offset && count <= failures + offset
                    ? HttpStatusCode.ServiceUnavailable : null,
                responseWriter: async (path, count, stream, token) =>
                {
                    if (!stalledBody || path != failedPath || count != offset + 1)
                        return false;
                    // 响应头已成功收到，但正文停滞；关闭进度刷新也须检测并恢复。
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 1024\r\nConnection: close\r\n\r\n"), token);
                    await stream.WriteAsync(new byte[1], token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return true;
                });
            using var extractor = await Load(server);
            var streams = await extractor.ExtractStreamsAsync();
            var manager = CreateManager(root, streams, extractor);
            // 临时错误或正文停滞后应及时恢复，并完整保留清单窗口内的媒体。
            using var stop = new CancellationTokenSource();
            var recording = manager.StartRecordAsync(stop.Token);
            try
            {
                Assert.True(await recording.WaitAsync(TimeSpan.FromSeconds(12)));
            }
            finally
            {
                stop.Cancel();
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.Equal(failures + offset + 1, server.RequestCount(failedPath));
            if (target != "init")
                Assert.Equal(1, server.RequestCount("init.mp4"));
            for (var i = 0; i < 3; i++)
                if (!(target == "media" && i == 1))
                    Assert.Equal(1, server.RequestCount($"media-{i}.m4s"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 150);
            Assert.False(Directory.Exists(Path.Combine(root, "tmp")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FatalRefreshFailureReturnsFailureInsteadOfSuccess(bool malformed)
    {
        var root = Directory.CreateTempSubdirectory("live-network-fatal-").FullName;
        try
        {
            await using var server = new MediaFixtureServer(root,
                (path, version) => malformed && path == "live.m3u8" && version > 0 ? "invalid manifest" : Manifest(path, version),
                responseStatus: (path, count) => !malformed && path == "live.m3u8" && count > 1 ? HttpStatusCode.Forbidden : null);
            using var extractor = await Load(server);
            var streams = await extractor.ExtractStreamsAsync();
            // 空初始清单即可验证生产者失败，避免引入下载或媒体探测错误。
            streams[0].Playlist!.MediaParts[0].MediaSegments.Clear();
            var manager = CreateManager(root, streams, extractor);
            Assert.False(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, server.RequestCount("live.m3u8"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedirectLoopStopsRecordingInsteadOfRetryingForever(bool manifest)
    {
        var root = Directory.CreateTempSubdirectory("live-redirect-loop-").FullName;
        using var stop = new CancellationTokenSource();
        Task<bool>? recording = null;
        try
        {
            var failedPath = manifest ? "live.m3u8" : "media.ts";
            await using var server = new MediaFixtureServer(root,
                (_, _) => "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nmedia.ts\n",
                responseWriter: async (path, count, stream, token) =>
                {
                    if (path != "redirect" && (path != failedPath || manifest && count == 1))
                        return false;
                    await Task.Delay(10, token);
                    var location = path == "redirect" ? failedPath : "redirect";
                    await stream.WriteAsync(Encoding.ASCII.GetBytes(
                        $"HTTP/1.1 302 Found\r\nLocation: /{location}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), token);
                    return true;
                });
            using var extractor = await Load(server);
            var streams = await extractor.ExtractStreamsAsync();
            if (manifest)
                streams[0].Playlist!.MediaParts[0].MediaSegments.Clear();
            recording = CreateManager(root, streams, extractor).StartRecordAsync(stop.Token);
            Assert.False(await recording.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.InRange(server.RequestCount(failedPath), 1, 7);
        }
        finally
        {
            stop.Cancel();
            if (recording != null)
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopInterruptsPendingRefreshRequest(bool idleTimeout)
    {
        var root = Directory.CreateTempSubdirectory("live-network-stop-").FullName;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var server = new MediaFixtureServer(root, Manifest, async path =>
            {
                if (path == "live.m3u8" && pending.Task.IsCompleted)
                    await release.Task;
            });
            using var extractor = await Load(server);
            var streams = await extractor.ExtractStreamsAsync();
            streams[0].Playlist!.MediaParts[0].MediaSegments.Clear();
            using var stop = new CancellationTokenSource();
            var options = CreateOptions(root);
            options.LiveWaitTime = 1;
            options.LiveTakeCount = 16;
            options.LiveIdleTimeout = idleTimeout ? 2 : null;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Task<bool>? recording = null;
            try
            {
                pending.SetResult();
                recording = manager.StartRecordAsync(stop.Token);
                // 请求计数在服务器等待响应前更新，确认取消的是实际 HTTP 请求。
                var timer = Stopwatch.StartNew();
                while (server.RequestCount("live.m3u8") < 2 && timer.Elapsed < TimeSpan.FromSeconds(3))
                    await Task.Delay(20);
                Assert.Equal(2, server.RequestCount("live.m3u8"));
                if (!idleTimeout)
                    stop.Cancel();
                Assert.True(await recording.WaitAsync(TimeSpan.FromSeconds(3)));
                release.SetResult();
            }
            finally
            {
                stop.Cancel();
                release.TrySetResult();
                if (recording != null)
                    await recording.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            release.TrySetResult();
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("init.mp4", false)]
    [InlineData("init.mp4", true)]
    [InlineData("media-1.m4s", false)]
    [InlineData("media-1.m4s", true)]
    public async Task StopInterruptsPendingDownloadEvenAfterEndlist(string path, bool idleTimeout)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-download-stop-").FullName;
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manifestRequests = 0;
        try
        {
            await GenerateCutMedia(root);
            await using var server = new MediaFixtureServer(root, Manifest, async requested =>
            {
                if (path == "media-1.m4s" && requested == "live.m3u8" && Interlocked.Increment(ref manifestRequests) > 1)
                {
                    var output = Path.Combine(root, "out", "result.mp4");
                    var timer = Stopwatch.StartNew();
                    while ((!File.Exists(output) || new FileInfo(output).Length == 0) && timer.Elapsed < TimeSpan.FromSeconds(5))
                        await Task.Delay(20);
                }
                if (requested == path)
                {
                    pending.TrySetResult();
                    await release.Task;
                }
            });
            using var extractor = await Load(server);
            using var stop = new CancellationTokenSource();
            var options = CreateOptions(root);
            options.LiveRealTimeMerge = true;
            options.LiveWaitTime = 1;
            options.LiveTakeCount = 16;
            options.LiveIdleTimeout = idleTimeout ? 2 : null;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, await extractor.ExtractStreamsAsync(), extractor);
            Task<bool>? recording = null;
            try
            {
                recording = manager.StartRecordAsync(stop.Token);
                await pending.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (!idleTimeout)
                    stop.Cancel();
                Assert.True(await recording.WaitAsync(TimeSpan.FromSeconds(4)));
                if (path == "media-1.m4s")
                    await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 2, 50);
            }
            finally
            {
                stop.Cancel();
                release.TrySetResult();
                if (recording != null)
                    await recording.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
        finally
        {
            release.TrySetResult();
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExpiredMediaDoesNotStopRemainingDownloadsAndMarksRecordingIncomplete()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-network-expired-").FullName;
        try
        {
            await GenerateCutMedia(root);
            await using var server = new MediaFixtureServer(root, Manifest,
                responseStatus: (path, _) => path == "media-1.m4s" ? HttpStatusCode.Gone : null);
            using var extractor = await Load(server);
            var manager = CreateManager(root, await extractor.ExtractStreamsAsync(), extractor);
            Assert.False(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, server.RequestCount("media-2.m4s"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 100);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task FatalParallelDownloadCancelsAnotherSegmentsNetworkRetry()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("live-parallel-fatal-").FullName;
        using var stop = new CancellationTokenSource();
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? recording = null;
        try
        {
            await GenerateCutMedia(root);
            await using var server = new MediaFixtureServer(root,
                (path, _) => Manifest(path, 1)?.Replace("#EXT-X-ENDLIST\n", ""), async path =>
                {
                    if (path == "media-1.m4s")
                        retryStarted.TrySetResult();
                    if (path == "media-2.m4s")
                        await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
                }, responseStatus: (path, _) => path switch
                {
                    "media-1.m4s" => HttpStatusCode.ServiceUnavailable,
                    "media-2.m4s" => HttpStatusCode.Forbidden,
                    _ => null
                });
            using var extractor = await Load(server);
            var manager = CreateManager(root, await extractor.ExtractStreamsAsync(), extractor);
            recording = manager.StartRecordAsync(stop.Token);
            Assert.False(await recording.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(retryStarted.Task.IsCompleted);
            Assert.Equal(1, server.RequestCount("media-2.m4s"));
        }
        finally
        {
            stop.Cancel();
            if (recording != null)
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExpiredFirstSegmentStillProbesAndFixesFollowingTextSubtitles()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("live-expired-first-").FullName;
        try
        {
            for (var i = 1; i < 3; i++)
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"),
                    $"WEBVTT\n\n00:00:0{i * 2}.250 --> 00:00:0{i * 2 + 1}.000\ncue-{i}\n\n");
            await using var server = new MediaFixtureServer(root, (path, version) => path != "live.m3u8" ? null :
                "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n" +
                string.Join('\n', Enumerable.Range(0, 3).Select(i => $"#EXTINF:2,\nsub-{i}.vtt")) +
                (version == 0 ? "\n" : "\n#EXT-X-ENDLIST\n"),
                responseStatus: (path, _) => path == "sub-0.vtt" ? HttpStatusCode.Gone : null);
            using var extractor = await Load(server);
            var streams = await extractor.ExtractStreamsAsync();
            var manager = CreateManager(root, streams, extractor);
            Assert.False(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(MediaType.SUBTITLES, streams[0].MediaType);
            var subtitle = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.vtt")));
            Assert.Equal(["cue-1", "cue-2"], subtitle.Cues.Select(cue => cue.Payload).ToArray());
            for (var i = 0; i < 3; i++)
                Assert.Equal(1, server.RequestCount($"sub-{i}.vtt"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CancellationInterruptsSegmentSpeedLimitWait()
    {
        var root = Directory.CreateTempSubdirectory("live-speed-limit-stop-").FullName;
        using var stop = new CancellationTokenSource();
        Task<DownloadResult?>? downloading = null;
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(root, "media.bin"), new byte[128 * 1024]);
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            var speed = new SpeedContainer { SpeedLimit = 1 };
            var options = CreateOptions(root);
            options.DownloadRetryCount = 0;
            var downloader = new SimpleDownloader(new DownloaderConfig { DirPrefix = root, MyOptions = options });
            downloading = downloader.DownloadSegmentAsync(new MediaSegment { Url = server.Url + "media.bin" },
                Path.Combine(root, "segment.tmp"), speed, cancellationToken: stop.Token, throwOnFailure: true);
            var timer = Stopwatch.StartNew();
            while (speed.RDownloaded <= 16 * 1024 && timer.Elapsed < TimeSpan.FromSeconds(3))
                await Task.Delay(20);
            Assert.True(speed.RDownloaded > 16 * 1024);
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await downloading.WaitAsync(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            stop.Cancel();
            if (downloading != null)
            {
                try
                {
                    await downloading.WaitAsync(TimeSpan.FromSeconds(3));
                }
                catch (OperationCanceledException) { }
            }
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("hls", false)]
    [InlineData("hls", true)]
    [InlineData("dash", false)]
    [InlineData("dash", true)]
    public async Task Tail404WaitsForPublicationAndPermanent404StillFinishes(string format, bool permanent)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-tail-404-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifestPath = format == "hls" ? "live.m3u8" : "live.mpd";
            await using var server = new MediaFixtureServer(root,
                (path, version) => path == manifestPath
                    ? PublicationManifest(format, 0, version == 0 ? 1 : 3, version == 0) : null,
                responseStatus: (path, count) => path == "media-2.m4s" && (permanent || count <= 4)
                    ? HttpStatusCode.NotFound : null);
            using var extractor = await Load(server, manifestPath);
            using var stop = new CancellationTokenSource();
            var recording = CreateManager(root, await extractor.ExtractStreamsAsync(), extractor,
                recordLimit: format == "dash" ? TimeSpan.FromSeconds(6) : null).StartRecordAsync(stop.Token);
            try
            {
                Assert.Equal(!permanent, await recording.WaitAsync(TimeSpan.FromSeconds(12)));
            }
            finally
            {
                stop.Cancel();
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.InRange(server.RequestCount("media-2.m4s"), permanent ? 6 : 5, permanent ? 7 : 5);
            Assert.Equal(1, server.RequestCount("media-0.m4s"));
            Assert.Equal(1, server.RequestCount("media-1.m4s"));
            Assert.Equal(1, server.RequestCount("init.mp4"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), permanent ? 4 : 6, permanent ? 100 : 150);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("hls")]
    [InlineData("dash")]
    public async Task WindowAdvancementStopsWaitingForMissingTail(string format)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-window-404-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifestPath = format == "hls" ? "live.m3u8" : "live.mpd";
            await using var server = new MediaFixtureServer(root,
                (path, version) => path == manifestPath
                    ? PublicationManifest(format, version == 0 ? 0 : 1, version == 0 ? 1 : 2, version == 0) : null,
                responseStatus: (path, _) => path == "media-0.m4s" ? HttpStatusCode.NotFound : null);
            using var extractor = await Load(server, manifestPath);
            using var stop = new CancellationTokenSource();
            var recording = CreateManager(root, await extractor.ExtractStreamsAsync(), extractor,
                recordLimit: format == "dash" ? TimeSpan.FromSeconds(6) : null).StartRecordAsync(stop.Token);
            try
            {
                Assert.False(await recording.WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                stop.Cancel();
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            }
            Assert.InRange(server.RequestCount("media-0.m4s"), 2, 3);
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 100);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Ordinary404UsesConfiguredRetries()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("live-middle-404-").FullName;
        try
        {
            await GenerateCutMedia(root);
            await using var server = new MediaFixtureServer(root,
                (path, _) => path == "live.m3u8" ? PublicationManifest("hls", 0, 3, false) : null,
                responseStatus: (path, count) => path == "media-0.m4s" && count <= 2 ? HttpStatusCode.NotFound : null);
            using var extractor = await Load(server);
            var options = CreateOptions(root);
            options.LiveRealTimeMerge = true;
            options.LiveWaitTime = 1;
            options.LiveTakeCount = 16;
            options.DownloadRetryCount = 2;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, await extractor.ExtractStreamsAsync(), extractor);
            Assert.True(await manager.StartRecordAsync().WaitAsync(TimeSpan.FromSeconds(8)));
            Assert.Equal(3, server.RequestCount("media-0.m4s"));
            await AssertVideo(Assert.Single(Directory.GetFiles(Path.Combine(root, "out"))), 6, 150);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopInterruptsTail404Wait(bool idleTimeout)
    {
        var root = Directory.CreateTempSubdirectory("live-404-stop-").FullName;
        using var stop = new CancellationTokenSource();
        Task<bool>? recording = null;
        try
        {
            await using var server = new MediaFixtureServer(root,
                (path, _) => path == "live.m3u8" ? "#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nmedia.ts\n" : null,
                responseStatus: (path, _) => path == "media.ts" ? HttpStatusCode.NotFound : null);
            using var extractor = await Load(server);
            var options = CreateOptions(root);
            options.LiveWaitTime = 1;
            options.LiveTakeCount = 16;
            options.LiveIdleTimeout = idleTimeout ? 2 : null;
            var manager = new SimpleLiveRecordManager2(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, await extractor.ExtractStreamsAsync(), extractor);
            recording = manager.StartRecordAsync(stop.Token);
            var timer = Stopwatch.StartNew();
            while (server.RequestCount("media.ts") < 1 && timer.Elapsed < TimeSpan.FromSeconds(3))
                await Task.Delay(20);
            Assert.True(server.RequestCount("media.ts") >= 1);
            if (!idleTimeout)
                stop.Cancel();
            Assert.True(await recording.WaitAsync(TimeSpan.FromSeconds(4)));
            Assert.InRange(server.RequestCount("media.ts"), 1, 3);
        }
        finally
        {
            stop.Cancel();
            if (recording != null)
                await recording.WaitAsync(TimeSpan.FromSeconds(5));
            Directory.Delete(root, true);
        }
    }

    private static string PublicationManifest(string format, int start, int count, bool live)
    {
        if (format == "hls")
            return $"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:{start}\n#EXT-X-MAP:URI=\"init.mp4\"\n" +
                string.Join('\n', Enumerable.Range(start, count).Select(i => $"#EXTINF:2,\nmedia-{i}.m4s")) +
                (live ? "\n" : "\n#EXT-X-ENDLIST\n");
        return $"""
            <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="dynamic" minimumUpdatePeriod="PT1S">
              <Period duration="PT6S"><AdaptationSet mimeType="video/mp4">
                <Representation id="v" bandwidth="1000" codecs="avc1.64001e">
                  <SegmentTemplate initialization="init.mp4" media="media-$Number$.m4s" timescale="1" startNumber="{start}">
                    <SegmentTimeline><S t="{start * 2}" d="2" r="{count - 1}"/></SegmentTimeline>
                  </SegmentTemplate>
                </Representation>
              </AdaptationSet></Period>
            </MPD>
            """;
    }

    private static async Task<StreamExtractor> Load(MediaFixtureServer server, string manifestPath = "live.m3u8")
    {
        var extractor = new StreamExtractor(new ParserConfig { KeyRetryCount = 0 });
        await extractor.LoadSourceFromUrlAsync(server.Url + manifestPath);
        return extractor;
    }

    private static SimpleLiveRecordManager2 CreateManager(string root, List<StreamSpec> streams, StreamExtractor extractor,
        TimeSpan? recordLimit = null)
    {
        var options = CreateOptions(root);
        options.LiveRealTimeMerge = true;
        options.LiveKeepSegments = false;
        options.LiveWaitTime = 1;
        options.LiveTakeCount = 16;
        options.DownloadRetryCount = 0;
        options.LiveRecordLimit = recordLimit;
        return new SimpleLiveRecordManager2(new DownloaderConfig
            { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
    }

    private static string? Manifest(string path, int version)
    {
        if (path != "live.m3u8")
            return null;
        // 首次只发布一片；故障恢复后补回完整窗口并结束，检查补片、去重和收尾。
        return "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n#EXT-X-MAP:URI=\"init.mp4\"\n" +
            string.Join('\n', Enumerable.Range(0, version == 0 ? 1 : 3).Select(i => $"#EXTINF:2,\nmedia-{i}.m4s")) +
            (version == 0 ? "\n" : "\n#EXT-X-ENDLIST\n");
    }
}
