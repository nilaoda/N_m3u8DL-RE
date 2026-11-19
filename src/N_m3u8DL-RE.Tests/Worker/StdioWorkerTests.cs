using WorkerClient = N_m3u8DL_RE.Tests.TestSupport.StdioWorkerClient;
using System.Net;
using System.Globalization;
using System.Text;
using System.Text.Json;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Tests.TestSupport;
using N_m3u8DL_RE.Worker;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.Worker;

[Collection("Download console")]
public class StdioWorkerTests
{
    [Fact]
    public void RequestUsesCliDefaultsAndPreservesExplicitTimeout()
    {
        var defaults = ProtocolMapper.ToOptions(new TaskRequest { Input = "https://example.test/video.m3u8" });
        Assert.Equal(100, defaults.HttpRequestTimeout);
        Assert.False(defaults.HttpRequestTimeoutSpecified);
        Assert.True(defaults.CheckSegmentsCount);
        Assert.True(defaults.AutoSubtitleFix);
        var configured = ProtocolMapper.ToOptions(new TaskRequest
        {
            Input = "https://example.test/video.m3u8", HttpRequestTimeout = 3.5, MaxSpeed = 12345
        });
        Assert.Equal(3.5, configured.HttpRequestTimeout);
        Assert.True(configured.HttpRequestTimeoutSpecified);
        Assert.Equal(12345, configured.MaxSpeed);
        Assert.Throws<ArgumentException>(() => ProtocolMapper.ToOptions(new TaskRequest { Input = "https://example.test/a", ThreadCount = 0 }));
    }

    [Fact]
    public void RequestValuesAreNotInterpretedAsCommandLineOptions()
    {
        var options = ProtocolMapper.ToOptions(new TaskRequest
        {
            Input = "https://example.test/video.m3u8", SaveName = "--help", SaveDir = "--config", TmpDir = "--",
            AutoSelect = false
        });
        Assert.Equal("--help", options.SaveName);
        Assert.Equal("--config", options.SaveDir);
        Assert.Equal("--", options.TmpDir);
        Assert.False(options.AutoSelect);
    }

    [Fact]
    public void RequestUsesCliParsersForComplexAndRepeatedOptions()
    {
        var options = ProtocolMapper.ToOptions(new TaskRequest
        {
            Input = "https://example.test/video.m3u8",
            SelectVideo = "res=1920x1080:for=best",
            SelectAudio = "lang=en:for=all",
            SelectSubtitle = "lang=zh:for=all",
            ConcurrentDownload = true,
            MuxAfterDone = "format=mkv:muxer=mkvmerge:keep=true",
            CustomRange = "10-20",
            LiveRecordLimit = "00:02:00",
            Headers = new() { ["Authorization"] = "Bearer fixture", ["X-Test"] = "a=b" },
            Keys = ["00112233445566778899aabbccddeeff", "ffeeddccbbaa99887766554433221100"],
            AdKeywords = ["advert", "sponsor"]
        });

        Assert.Equal("1920x1080", options.VideoFilter?.ResolutionReg?.ToString());
        Assert.Equal("en", options.AudioFilter?.LanguageReg?.ToString());
        Assert.Equal("all", options.AudioFilter?.For);
        Assert.Equal("zh", options.SubtitleFilter?.LanguageReg?.ToString());
        Assert.True(options.ConcurrentDownload);
        Assert.True(options.MuxAfterDone);
        Assert.True(options.MuxOptions?.UseMkvmerge);
        Assert.True(options.MuxOptions?.KeepFiles);
        Assert.Equal(10L, options.CustomRange?.StartSegIndex);
        Assert.Equal(20L, options.CustomRange?.EndSegIndex);
        Assert.Equal(TimeSpan.FromMinutes(2), options.LiveRecordLimit);
        Assert.Equal("Bearer fixture", options.Headers["Authorization"]);
        Assert.Equal("a=b", options.Headers["X-Test"]);
        Assert.Equal(new[] { "00112233445566778899aabbccddeeff", "ffeeddccbbaa99887766554433221100" }, options.Keys);
        Assert.Equal(new[] { "advert", "sponsor" }, options.AdKeywords);
    }

    [Fact]
    public async Task AdvancedRequestUsesCliParsersAndPreservesExplicitFalse()
    {
        var root = Directory.CreateTempSubdirectory("worker-options-").FullName;
        try
        {
            var key = Path.Combine(root, "key.bin");
            var subtitle = Path.Combine(root, "english.srt");
            var keyBytes = Convert.FromHexString("00112233445566778899AABBCCDDEEFF");
            await File.WriteAllBytesAsync(key, keyBytes);
            await File.WriteAllTextAsync(subtitle, "1\n00:00:00,000 --> 00:00:01,000\nHello\n");
            var request = new TaskRequest
            {
                Input = "https://example.test/video.m3u8", UrlProcessorArgs = "--custom-value=a=b",
                CustomHlsMethod = "AES_128", CustomHlsKey = key,
                CustomHlsIv = "ffeeddccbbaa99887766554433221100", CustomHlsScope = "VIDEO",
                DropVideo = "res=640x360:for=all", DropAudio = "lang=fr:for=all", DropSubtitle = "lang=de:for=all",
                MuxAfterDone = "format=mkv", MuxImports = [$"path={subtitle}:lang=en:name=English", subtitle],
                SubFormat = "VTT", FfmpegConcatMode = "DEMUXER", Mp4RealTimeDecryption = true,
                LivePipeMux = true, LiveFixVttByAudio = true, AllowHlsMultiExtMap = true,
                AutoSubtitleFix = false, CheckSegmentsCount = false, NoDateInfo = true,
                TaskStartAt = "20300102030405", VodListParts = true, LogLevel = "DEBUG"
            };
            var json = JsonSerializer.Serialize(request, WorkerJsonContext.Default.TaskRequest);
            var options = ProtocolMapper.ToOptions(JsonSerializer.Deserialize(json, WorkerJsonContext.Default.TaskRequest)!);
            Assert.Equal(request.UrlProcessorArgs, options.UrlProcessorArgs);
            Assert.Equal(EncryptMethod.AES_128, options.CustomHLSMethod);
            Assert.Equal(keyBytes, options.CustomHLSKey);
            Assert.Equal(Convert.FromHexString(request.CustomHlsIv), options.CustomHLSIv);
            Assert.Equal(CustomHlsScope.VIDEO, options.CustomHLSScope);
            Assert.Equal("640x360", options.DropVideoFilter?.ResolutionReg?.ToString());
            Assert.Equal("fr", options.DropAudioFilter?.LanguageReg?.ToString());
            Assert.Equal("de", options.DropSubtitleFilter?.LanguageReg?.ToString());
            Assert.Equal(new[] { subtitle, subtitle }, options.MuxImports?.Select(file => file.FilePath));
            Assert.Equal("en", options.MuxImports?[0].LangCode);
            Assert.Equal("English", options.MuxImports?[0].Description);
            Assert.Equal(SubtitleFormat.VTT, options.SubtitleFormat);
            Assert.Equal(FFmpegConcatMode.DEMUXER, options.EffectiveConcatMode);
            Assert.True(options.MP4RealTimeDecryption);
            Assert.True(options.LivePipeMux);
            Assert.True(options.LiveFixVttByAudio);
            Assert.True(options.AllowHlsMultiExtMap);
            Assert.True(options.NoDateInfo);
            Assert.True(options.VodListParts);
            Assert.False(options.AutoSubtitleFix);
            Assert.False(options.CheckSegmentsCount);
            Assert.Equal(new DateTime(2030, 1, 2, 3, 4, 5), options.TaskStartAt);
            Assert.Equal(LogLevel.DEBUG, options.LogLevel);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CancelInterruptsScheduledStart()
    {
        await using var worker = new WorkerClient();
        await worker.Read("ready");
        await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
            request = new { input = "https://example.test/video.m3u8", noLog = true,
                taskStartAt = DateTime.Now.AddDays(1).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) } });
        await worker.Read("accepted");
        await worker.Read("log");
        await worker.Send(new { type = "cancel", id = "cancel" });
        var result = (await worker.Read("result")).GetProperty("result");
        Assert.Equal("cancelled", result.GetProperty("status").GetString());
        Assert.Empty(result.GetProperty("outputs").EnumerateArray());
        await worker.Exit(2);
    }

    [Fact]
    public async Task SegmentedLiveWithoutRealtimeMergeReportsRetainedFiles()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("worker-live-files-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifest = await File.ReadAllTextAsync(Path.Combine(root, "source.m3u8"));
            await using var server = new MediaFixtureServer(root, (path, revision) => path == "live.m3u8"
                ? revision == 0 ? manifest.Replace("#EXT-X-ENDLIST", "") : manifest : null);
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "live.m3u8", autoSelect = true, liveWaitTime = 1,
                    saveDir = Path.Combine(root, "out"), tmpDir = Path.Combine(root, "tmp"), noLog = true } });
            var result = (await worker.Read("result")).GetProperty("result");
            Assert.Equal("succeeded", result.GetProperty("status").GetString());
            var outputs = result.GetProperty("outputs").EnumerateArray().Select(o => o.GetProperty("path").GetString()!).ToArray();
            Assert.NotEmpty(outputs);
            Assert.All(outputs, path => Assert.True(File.Exists(path)));
            var segment = await File.ReadAllBytesAsync(Path.Combine(root, "media-0.m4s"));
            Assert.Contains(outputs, path => File.ReadAllBytes(path).AsSpan().SequenceEqual(segment));
            await worker.Exit(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CancelInterruptsLivePipeConnectionWait()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("worker-pipe-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifest = (await File.ReadAllTextAsync(Path.Combine(root, "source.m3u8"))).Replace("#EXT-X-ENDLIST", "");
            await using var server = new MediaFixtureServer(root, (path, _) => path == "live.m3u8" ? manifest : null);
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "live.m3u8", autoSelect = true, livePipeMux = true,
                    saveDir = root, tmpDir = Path.Combine(root, "tmp"), noLog = true } });
            while (true)
            {
                var message = (await worker.Read("log")).GetProperty("message").GetString()!;
                if (message.Contains("RE_pipe_", StringComparison.Ordinal))
                    break;
            }
            await worker.Send(new { type = "cancel", id = "cancel" });
            Assert.Equal("cancelled", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
            await worker.Exit(2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StoppingPipeMuxFailsPromptlyWhenASelectedTrackHasNoMedia()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("worker-pipe-empty-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifest = (await File.ReadAllTextAsync(Path.Combine(root, "source.m3u8"))).Replace("#EXT-X-ENDLIST", "");
            const string master = """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID="a",NAME="English",LANGUAGE="en",DEFAULT=YES,URI="audio.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=160x90,AUDIO="a"
                video.m3u8
                """;
            const string audio = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXT-X-MEDIA-SEQUENCE:0\n";
            await using var server = new MediaFixtureServer(root, (path, _) => path switch
            {
                "master.m3u8" => master, "video.m3u8" => manifest, "audio.m3u8" => audio, _ => null
            });
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "master.m3u8", autoSelect = true, livePipeMux = true,
                    saveDir = root, tmpDir = Path.Combine(root, "tmp"), noLog = true } });

            while (true)
            {
                var message = (await worker.Read("log")).GetProperty("message").GetString()!;
                if (message.Contains("RE_pipe_", StringComparison.Ordinal))
                    break;
            }
            await worker.Send(new { type = "stop", id = "stop" });
            Assert.Equal("stop", (await worker.Read("accepted")).GetProperty("commandId").GetString());
            var result = (await worker.Read("result")).GetProperty("result");
            Assert.Equal("failed", result.GetProperty("status").GetString());
            Assert.Empty(result.GetProperty("outputs").EnumerateArray());
            await worker.Exit(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task BinaryDownloadProducesCleanProtocolAndFinalArtifact()
    {
        var root = Directory.CreateTempSubdirectory("worker-binary-").FullName;
        try
        {
            var bytes = Enumerable.Repeat((byte)0xab, 16384).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(root, "file.bin"), bytes);
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            await using var worker = new WorkerClient();
            Assert.Equal(1, (await worker.Read("ready")).GetProperty("protocolVersion").GetInt32());
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "file.bin", saveDir = root, saveName = "output.bin", noLog = true } });
            await worker.Read("accepted");
            var result = await worker.Read("result");
            Assert.Equal("succeeded", result.GetProperty("result").GetProperty("status").GetString());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(root, "output.bin")));
            Assert.Single(result.GetProperty("result").GetProperty("outputs").EnumerateArray());
            Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "progress" &&
                message.GetProperty("progress").GetProperty("downloadedBytes").GetInt64() == bytes.Length);
            Assert.Contains(worker.Messages, message => message.GetProperty("type").GetString() == "log");
            await worker.Exit(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InvalidMessagesAreRejectedAndWorkerCanStillAcceptATask()
    {
        await using var worker = new WorkerClient();
        await worker.Read("ready");
        await worker.SendRaw("{invalid}");
        Assert.Equal("invalid_message", (await worker.Read("error")).GetProperty("error").GetProperty("code").GetString());
        await worker.Send(new { type = "start", id = "v", protocolVersion = 99, request = new { input = "https://example.test/a" } });
        await worker.Read("error");
        await worker.Send(new { type = "start", id = "u", protocolVersion = 1, request = new { input = "https://example.test/a", unknown = true } });
        await worker.Read("error");
        await worker.Send(new { type = "start", id = "o", protocolVersion = 1,
            request = new { input = "https://example.test/a", httpVersion = "3" } });
        Assert.Equal("invalid_command", (await worker.Read("error")).GetProperty("error").GetProperty("code").GetString());
        await worker.Send(new { type = "start", id = "date", protocolVersion = 1,
            request = new { input = "https://example.test/a", taskStartAt = "tomorrow" } });
        Assert.Equal("invalid_command", (await worker.Read("error")).GetProperty("error").GetProperty("code").GetString());
        var root = Directory.CreateTempSubdirectory("worker-failure-").FullName;
        try
        {
            await using var server = new MediaFixtureServer(root, (_, _) => null, responseStatus: (_, _) => HttpStatusCode.NotFound);
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1, request = new { input = server.Url + "missing", noLog = true } });
            var result = await worker.Read("result");
            Assert.Equal("failed", result.GetProperty("result").GetProperty("status").GetString());
            await worker.Exit(1);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NonUtf8InputEndsTheSessionWithAProtocolError(bool utf16)
    {
        await using var worker = new WorkerClient();
        await worker.Read("ready");
        await worker.SendBytes(utf16
            ? [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("{\"type\":\"cancel\",\"id\":\"c\"}\n")]
            : [0xff, 0x0a]);
        Assert.Equal("invalid_message", (await worker.Read("error")).GetProperty("error").GetProperty("code").GetString());
        await worker.Exit(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelOrInputEofInterruptsManifestRead(bool closeInput)
    {
        var root = Directory.CreateTempSubdirectory("worker-cancel-").FullName;
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using var server = new MediaFixtureServer(root, (_, _) => null,
                responseWriter: async (_, _, stream, token) =>
                {
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 100000\r\n\r\n"u8.ToArray(), token);
                    connected.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                });
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1, request = new { input = server.Url + "live.m3u8", noLog = true } });
            await connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (closeInput)
                worker.CloseInput();
            else
                await worker.Send(new { type = "cancel", id = "c" });
            Assert.Equal("cancelled", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
            await worker.Exit(2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SelectionValidatesRepliesAndRejectsASecondTask()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("worker-select-").FullName;
        try
        {
            const string master = "#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=100000,RESOLUTION=160x90\na.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=200000,RESOLUTION=320x180\nb.m3u8\n";
            const string media = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nsegment.ts\n#EXT-X-ENDLIST\n";
            await using var server = new MediaFixtureServer(root, (path, _) => path == "master.m3u8" ? master : media);
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "master.m3u8", skipDownload = true, writeMetaJson = false, noLog = true } });
            var interaction = (await worker.Read("interaction")).GetProperty("interaction");
            Assert.Equal("streams", interaction.GetProperty("kind").GetString());
            await worker.Send(new { type = "start", id = "s2", protocolVersion = 1, request = new { input = server.Url + "master.m3u8" } });
            await worker.Read("error");
            await worker.Send(new { type = "respond", id = "bad", interactionId = interaction.GetProperty("id").GetString(), selectedIds = new[] { "unknown" } });
            await worker.Read("error");
            await worker.Send(new { type = "respond", id = "r", interactionId = interaction.GetProperty("id").GetString(),
                selectedIds = new[] { interaction.GetProperty("choices")[0].GetProperty("id").GetString() } });
            Assert.Equal("succeeded", (await worker.Read("result")).GetProperty("result").GetProperty("status").GetString());
            await worker.Exit(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StopHttpLiveRecordingPublishesCompletedOutput()
    {
        var root = Directory.CreateTempSubdirectory("worker-stop-").FullName;
        try
        {
            var bytes = new byte[188 * 60];
            for (var i = 0; i < bytes.Length; i += 188)
                bytes[i] = 0x47;
            await using var server = new MediaFixtureServer(root, (_, _) => null,
                responseWriter: async (_, _, stream, token) =>
                {
                    await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\n\r\n"u8.ToArray(), token);
                    await stream.WriteAsync(bytes, token);
                    await Task.Delay(Timeout.Infinite, token);
                    return true;
                });
            await using var worker = new WorkerClient();
            await worker.Read("ready");
            await worker.Send(new { type = "start", id = "s", protocolVersion = 1,
                request = new { input = server.Url + "live.ts", saveDir = root, saveName = "recording", noLog = true } });
            await worker.Read("progress");
            await worker.Send(new { type = "stop", id = "stop" });
            var result = (await worker.Read("result")).GetProperty("result");
            Assert.Equal("succeeded", result.GetProperty("status").GetString());
            var output = Assert.Single(result.GetProperty("outputs").EnumerateArray());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(output.GetProperty("path").GetString()!));
            await worker.Exit(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
