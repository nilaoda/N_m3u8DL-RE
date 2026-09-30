using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.DownloadManager;

public partial class VodMultiInitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EncryptedWebmIsDecryptedBeforeConcatOrPreservedWithoutKeys(bool withKey)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var packager = Environment.GetEnvironmentVariable("VOD_TEST_PACKAGER") ?? "packager";
        if (!File.Exists(packager) && !OnPath(packager))
            return;
        var root = Directory.CreateTempSubdirectory("vod-encrypted-webm-").FullName;
        try
        {
            var plain = Path.Combine(root, "plain.webm");
            var encrypted = Path.Combine(root, "encrypted.webm");
            var kid = new string('1', 32);
            var key = new string('a', 32);
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                "-t", "2", "-c:v", "libvpx-vp9", "-g", "50", plain);
            await Run(packager, $"input={plain},stream=video,output={encrypted}",
                "--enable_raw_key_encryption", "--keys", $"key_id={kid}:key={key}");
            Assert.True(MP4DecryptUtil.HasEncryptedTracks(encrypted));
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(plain));
            var bytes = await File.ReadAllBytesAsync(encrypted);
            var cluster = bytes.AsSpan().IndexOf<byte>([0x1f, 0x43, 0xb6, 0x75]);
            Assert.True(cluster > 0);
            await File.WriteAllBytesAsync(Path.Combine(root, "init.webm"), bytes[..cluster]);
            await File.WriteAllBytesAsync(Path.Combine(root, "media.webm"), bytes[cluster..]);
            var periods = Enumerable.Range(0, 2).Select(i => $$"""
                <Period duration="PT2S"><AdaptationSet mimeType="video/webm">
                  <ContentProtection cenc:default_KID="{{kid}}"/>
                  <Representation id="v{{i}}" codecs="vp9" bandwidth="100000">
                    <SegmentList duration="2"><Initialization sourceURL="init.webm"/>
                      <SegmentURL media="media.webm"/>
                    </SegmentList>
                  </Representation></AdaptationSet></Period>
                """);
            var manifest = Path.Combine(root, "vod.mpd");
            await File.WriteAllTextAsync(manifest,
                $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" xmlns:cenc=\"urn:mpeg:cenc:2013\" type=\"static\" mediaPresentationDuration=\"PT4S\">{string.Join('\n', periods)}</MPD>");
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var sources = await extractor.ExtractStreamsAsync();
            var streams = VodStreamPlanner.Build(sources, [sources[0]]);
            VodStreamPlanner.AlignPeriods(streams);
            var options = new MyOption
            {
                SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
                DecryptionEngine = withKey ? DecryptEngine.SHAKA_PACKAGER : DecryptEngine.MP4DECRYPT,
                DecryptionBinaryPath = packager, Keys = withKey ? [$"{kid}:{key}"] : null,
                ThreadCount = 2, CheckSegmentsCount = true, DelAfterDone = true, NoAnsiColor = true, LogLevel = LogLevel.OFF,
            };
            var manager = new SimpleDownloadManager(new DownloaderConfig
                { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
            Assert.Equal(withKey, await manager.StartDownloadAsync());
            var output = Path.Combine(root, "out", "result.webm");
            if (!withKey)
            {
                Assert.False(File.Exists(output));
                var preserved = Assert.Single(Directory.GetFiles(Path.Combine(root, "tmp"), "part.webm", SearchOption.AllDirectories));
                Assert.Equal(bytes, await File.ReadAllBytesAsync(preserved));
                Assert.True(MP4DecryptUtil.HasEncryptedTracks(preserved));
                return;
            }
            Assert.False(MP4DecryptUtil.HasEncryptedTracks(output));
            await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
            using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
                "-show_entries", "format=duration:stream=nb_read_frames", "-of", "json", output));
            Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!, CultureInfo.InvariantCulture), 3.99, 4.05);
            Assert.Equal("100", probe.RootElement.GetProperty("streams")[0].GetProperty("nb_read_frames").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(true, "url", true)]
    [InlineData(true, "url", false)]
    [InlineData(true, "drop", true)]
    [InlineData(true, "drop", false)]
    [InlineData(true, "keep-one", false)]
    [InlineData(false, "url", true)]
    [InlineData(false, "url", false)]
    public async Task DashCliRangesUseSourcePositionsBeforeAdAndPartRemoval(bool multiPeriod, string removal, bool timeRange)
    {
        var root = Directory.CreateTempSubdirectory("vod-review-range-").FullName;
        try
        {
            var periods = Enumerable.Range(0, multiPeriod ? 3 : 1).Select(p => $$"""
                <Period duration="PT{{(multiPeriod ? 4 : 12)}}S"><AdaptationSet mimeType="video/mp4">
                  <Representation id="v{{p}}" bandwidth="100000" codecs="{{(multiPeriod && p == 0 ? "hev1.1.6.L93" : "avc1.64001e")}}">
                    <SegmentList duration="2"><Initialization sourceURL="{{(multiPeriod && p == 0 ? "ad-" : "")}}init-{{p}}.mp4"/>
                      {{string.Join('\n', Enumerable.Range(0, multiPeriod ? 2 : 6).Select(i =>
                          $"<SegmentURL media=\"{(!multiPeriod && i < 2 ? "ad-" : "")}p{p}-{i}.m4s\"/>"))}}
                    </SegmentList>
                  </Representation></AdaptationSet></Period>
                """);
            var manifest = Path.Combine(root, "vod.mpd");
            await File.WriteAllTextAsync(manifest,
                $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT12S\">{string.Join('\n', periods)}</MPD>");
            string[] removalArgs = removal == "url" ? ["--ad-keyword", "ad-"]
                : ["--vod-drop-parts", removal == "keep-one" ? "0,2" : "0"];
            var result = await RunVodCli([manifest, "--auto-select", "--skip-download", "--custom-range",
                timeRange ? "00:00:04-00:00:07" : "2-3", "--tmp-dir", Path.Combine(root, "tmp"),
                "--save-name", "result", ..removalArgs]);
            Assert.True(result.ExitCode == 0, result.Log);
            using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "tmp", "result", "meta_selected.json")));
            var part = Assert.Single(meta.RootElement[0].GetProperty("Playlist").GetProperty("MediaParts").EnumerateArray());
            Assert.Equal(multiPeriod ? 1 : 0, part.GetProperty("PeriodIndex").GetInt32());
            var segments = part.GetProperty("MediaSegments").EnumerateArray().ToList();
            Assert.Equal([2, 3], segments.Select(s => s.GetProperty("Index").GetInt64()));
            Assert.Equal([4, 6], segments.Select(s => s.GetProperty("SourceTime").GetDouble()));
            Assert.All(segments, s => Assert.Contains(multiPeriod ? "p1-" : "p0-", s.GetProperty("Url").GetString()));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashDefaultTracksFollowSurvivingQualityRepresentatives(bool dropFirstPeriod)
    {
        var root = Directory.CreateTempSubdirectory("vod-review-default-").FullName;
        try
        {
            var manifest = Path.Combine(root, "vod.mpd");
            var periods = Enumerable.Range(0, 2).Select(p => $$"""
                <Period duration="PT{{(p == 0 ? 2 : 4)}}S">
                  <AdaptationSet mimeType="video/mp4"><Representation id="v{{p}}" bandwidth="100000" codecs="avc1.64001e">
                    <SegmentTemplate duration="2" initialization="v-init" media="v-$Number$"/>
                  </Representation></AdaptationSet>
                  <AdaptationSet mimeType="audio/mp4" lang="en"><Representation id="a{{p}}" bandwidth="128000" codecs="mp4a.40.2">
                    <SegmentTemplate duration="2" initialization="a-init" media="a-$Number$"/>
                  </Representation></AdaptationSet>
                  <AdaptationSet mimeType="text/vtt" lang="en"><Representation id="s{{p}}">
                    <SegmentTemplate duration="2" media="s-$Number$.vtt"/>
                  </Representation></AdaptationSet>
                </Period>
                """);
            await File.WriteAllTextAsync(manifest,
                $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT6S\">{string.Join('\n', periods)}</MPD>");
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(manifest);
            var streams = await extractor.ExtractStreamsAsync();
            var source = VodPartSelector.SnapshotStreams(streams);
            foreach (var video in streams.Where(s => s.MediaType is null or MediaType.VIDEO))
            {
                video.AudioId = "a0";
                video.SubtitleId = "s0";
            }
            if (dropFirstPeriod)
            {
                VodPartSelector.Apply(streams, "0");
                streams.RemoveAll(s => s.SegmentsCount == 0);
            }
            var choices = VodPartSelector.QualityChoices(streams, source);
            var first = choices.First(s => s.MediaType is null or MediaType.VIDEO);
            Assert.Equal("a1", first.AudioId);
            Assert.Equal("s1", first.SubtitleId);
            Assert.Equal("a1", FilterUtil.DefaultTrack(choices.Where(s => s.MediaType == MediaType.AUDIO).ToList(), first.AudioId!)!.GroupId);
            Assert.Equal("s1", FilterUtil.DefaultTrack(choices.Where(s => s.MediaType == MediaType.SUBTITLES).ToList(), first.SubtitleId!)!.GroupId);
            Assert.Null(FilterUtil.DefaultTrack([], "removed"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("drop", "continuous", "VTT")]
    [InlineData("drop", "continuous", "SRT")]
    [InlineData("drop", "reset", "VTT")]
    [InlineData("drop", "reset", "SRT")]
    [InlineData("drop", "mapped", "VTT")]
    [InlineData("drop", "mapped", "SRT")]
    [InlineData("range", "continuous", "VTT")]
    [InlineData("range", "reset", "VTT")]
    [InlineData("url", "continuous", "VTT")]
    [InlineData("url", "mapped", "VTT")]
    [InlineData("drop-first", "mapped", "VTT")]
    [InlineData("drop-last", "continuous", "VTT")]
    [InlineData("drop-last", "continuous", "SRT")]
    [InlineData("drop", "origin-offset", "VTT")]
    public async Task HlsSubtitleOnlyCliCutsCompressContinuousAndResetClocks(string cut, string clock, string format)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-review-sub-only-").FullName;
        try
        {
            var subtitles = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
            for (var i = 0; i < 3; i++)
            {
                if (i > 0 && cut != "url")
                    subtitles += "#EXT-X-DISCONTINUITY\n";
                var name = $"{(i == 1 ? "ad-" : "")}sub-{i}.vtt";
                var start = clock == "mapped" ? 10 : clock == "reset" ? 0 : i * 2;
                var map = clock == "mapped" ? $"LOCAL:00:00:10.000,MPEGTS:{i * 180000}"
                    : $"LOCAL:00:00:00.000,MPEGTS:{(clock == "origin-offset" ? 126000 : 0)}";
                // 删尾也要裁剪跨越分片边界的 cue，不能仅在后续段发生位移时启用映射。
                var end = start + (cut == "drop-last" && i == 1 ? 4 : 1);
                await File.WriteAllTextAsync(Path.Combine(root, name),
                    $"WEBVTT\nX-TIMESTAMP-MAP={map}\n\n00:00:{start:00}.250 --> 00:00:{end:00}.000\ncue-{i}\n\n");
                subtitles += $"#EXTINF:2,\n{name}\n";
            }
            await File.WriteAllTextAsync(Path.Combine(root, "subs.m3u8"), subtitles + "#EXT-X-ENDLIST\n");
            var master = Path.Combine(root, "master.m3u8");
            await File.WriteAllTextAsync(master, """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",NAME="English",LANGUAGE="en",URI="subs.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e",SUBTITLES="s"
                unused.m3u8
                """);
            string[] cutArgs = cut switch
            {
                "drop" => ["--vod-drop-parts", "1"],
                "drop-first" => ["--vod-drop-parts", "0"],
                "drop-last" => ["--vod-drop-parts", "2"],
                "range" => ["--custom-range", "1-2"],
                _ => ["--ad-keyword", "ad-"],
            };
            var result = await RunVodCli([master, "--sub-only", "--sub-format", format,
                "--save-dir", Path.Combine(root, "out"), "--tmp-dir", Path.Combine(root, "tmp"),
                "--save-name", "result", ..cutArgs]);
            Assert.True(result.ExitCode == 0, result.Log);
            var text = await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en." + format.ToLowerInvariant()));
            var sub = WebVttSub.Parse(format == "SRT" ? "WEBVTT\n\n" + text : text);
            string[] cues = cut switch
            {
                "range" or "drop-first" => ["cue-1", "cue-2"],
                "drop-last" => ["cue-0", "cue-1"],
                _ => ["cue-0", "cue-2"],
            };
            Assert.Equal(cues, sub.Cues.Select(c => c.Payload));
            Assert.Equal([0.25, 2.25], sub.Cues.Select(c => c.StartTime.TotalSeconds));
            Assert.Equal([1d, cut == "drop-last" ? 4d : 3d], sub.Cues.Select(c => c.EndTime.TotalSeconds));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task<(int ExitCode, string Log)> RunVodCli(params string[] args)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        info.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var arg in args.Concat(["--no-log", "--disable-update-check", "--ui-language", "zh-CN"]))
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout + await stderr);
    }
}
