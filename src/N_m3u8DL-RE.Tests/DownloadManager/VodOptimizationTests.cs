using N_m3u8DL_RE.Common.Resource;
using System.Buffers.Binary;
using System.Globalization;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.DownloadManager;

[Collection("Download console")]
public class VodOptimizationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(20)]
    public async Task ConfigPreviewBoundsConcurrencyAndGroupsIdenticalContent(int threads)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-config-parallel-").FullName;
        try
        {
            await GenerateCutMedia(root);
            await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=128x72:r=25",
                "-t", "2", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls",
                "-hls_time", "2", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "other-init.mp4",
                "-hls_segment_filename", Path.Combine(root, "other-%d.m4s"), Path.Combine(root, "other.m3u8"));
            for (var i = 0; i < 8; i++) File.Copy(Path.Combine(root, "init.mp4"), Path.Combine(root, $"init-{i}.mp4"));
            await File.WriteAllTextAsync(Path.Combine(root, "invalid-init.mp4"), "invalid init");
            var gate = new object();
            var active = 0;
            var peak = 0;
            await using var server = new MediaFixtureServer(root, (_, _) => null, async _ =>
            {
                lock (gate) { active++; peak = Math.Max(peak, active); }
                try { await Task.Delay(100); }
                finally { lock (gate) active--; }
            });
            var names = Enumerable.Range(0, 8).Select(i => $"init-{i}.mp4")
                .Concat(["other-init.mp4", "invalid-init.mp4", "init-0.mp4"]).ToList();
            var stream = new StreamSpec { Playlist = new Playlist { MediaParts = names.Select((name, i) => new MediaPart {
                DiscontinuitySequence = i, MediaInit = new MediaSegment { Url = server.Url + name },
                MediaSegments = [new MediaSegment { Url = server.Url + "media-0.m4s", Duration = 2 }]
            }).ToList() } };

            var groups = await VodPartSelector.InspectGroupsAsync([stream], new MyOption { FFmpegBinaryPath = "ffmpeg", ThreadCount = threads }, true);

            Assert.Equal(3, groups.Count);
            Assert.Equal(9, groups.Single(g => g.Configuration.Contains("160x90")).Ids.Count);
            Assert.Single(groups.Single(g => g.Configuration.Contains("128x72")).Ids);
            Assert.Single(groups.Single(g => g.Configuration.Contains(ResString.vodConfigUnknown)).Ids);
            foreach (var name in names.Distinct()) Assert.Equal(1, server.RequestCount(name));
            Assert.Equal(0, server.RequestCount("media-0.m4s"));
            Assert.InRange(peak, 1, Math.Clamp(threads, 1, 4));
            if (threads > 1)
                Assert.True(peak > 1);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ConfigPreviewProbesRepeatedInitOnceAndDoesNotDownloadMedia()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-config-preview-").FullName;
        try
        {
            await GenerateCutMedia(root);
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            var stream = new StreamSpec { Codecs = "avc1.64001e", Resolution = "999x999", Playlist = new Playlist {
                MediaParts = Enumerable.Range(0, 51).Select(i => new MediaPart {
                    DiscontinuitySequence = i + 1, MediaInit = new MediaSegment { Url = server.Url + "init.mp4" },
                    MediaSegments = [new MediaSegment { Url = server.Url + "media-0.m4s", Duration = 2 }]
                }).ToList() } };
            var groups = await VodPartSelector.InspectGroupsAsync([stream], new MyOption { FFmpegBinaryPath = "ffmpeg" }, true);
            var group = Assert.Single(groups);
            Assert.Equal(51, group.Ids.Count);
            Assert.Equal(102, group.Duration);
            Assert.Contains("160x90", group.Configuration);
            Assert.DoesNotContain("999x999", group.Configuration);
            Assert.Equal(1, server.RequestCount("init.mp4"));
            Assert.Equal(0, server.RequestCount("media-0.m4s"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("drop", false)]
    [InlineData("url-ad", false)]
    [InlineData("range", false)]
    [InlineData("same-group-ad", false)]
    [InlineData("drop", true)]
    [InlineData("same-group-ad", true)]
    [InlineData("coarse-subtitles", false)]
    [InlineData("reset-clock", false)]
    [InlineData("ts-range", false)]
    [InlineData("ts-binary-range", false)]
    [InlineData("cenc-drop", false)]
    public async Task HlsCutsKeepSubtitleClockAndReuseInit(string scenario, bool timestampMap)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        if (scenario == "cenc-drop" && (!OnPath("mp4encrypt") || !OnPath("mp4decrypt")))
            return;
        var root = Directory.CreateTempSubdirectory("vod-hls-cuts-").FullName;
        try
        {
            var ts = scenario.StartsWith("ts-");
            if (ts)
                await Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i", "testsrc2=s=160x90:r=25",
                    "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls", "-hls_time", "2",
                    "-hls_segment_filename", Path.Combine(root, "media-%d.ts"), Path.Combine(root, "source.m3u8"));
            else
                await GenerateCutMedia(root);
            if (scenario == "cenc-drop")
            {
                var full = Path.Combine(root, "full.mp4");
                MergeUtil.CombineMultipleFilesIntoSingleFile([Path.Combine(root, "init.mp4"),
                    .. Enumerable.Range(0, 3).Select(i => Path.Combine(root, $"media-{i}.m4s"))], full);
                var encrypted = Path.Combine(root, "encrypted.mp4");
                await Run("mp4encrypt", "--method", "MPEG-CENC", "--key", $"1:{new string('a', 32)}:random",
                    "--property", $"1:KID:{new string('1', 32)}", full, encrypted);
                var bytes = await File.ReadAllBytesAsync(encrypted);
                var moofs = new List<int>();
                for (var offset = 0; offset < bytes.Length;)
                {
                    if (bytes.AsSpan(offset + 4, 4).SequenceEqual("moof"u8))
                        moofs.Add(offset);
                    offset += checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                }
                Assert.Equal(3, moofs.Count);
                await File.WriteAllBytesAsync(Path.Combine(root, "init.mp4"), bytes[..moofs[0]]);
                for (var i = 0; i < 3; i++)
                    await File.WriteAllBytesAsync(Path.Combine(root, $"media-{i}.m4s"), bytes[moofs[i]..(i == 2 ? bytes.Length : moofs[i + 1])]);
                await File.WriteAllBytesAsync(Path.Combine(root, "key.bin"), Convert.FromHexString(new string('a', 32)));
            }
            var extension = ts ? "ts" : "m4s";
            var video = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n" + (ts ? "" : "#EXT-X-MAP:URI=\"init.mp4\"\n");
            if (scenario == "cenc-drop")
                video = video.Replace("#EXT-X-MAP", "#EXT-X-KEY:METHOD=SAMPLE-AES-CTR,URI=\"key.bin\"\n#EXT-X-MAP");
            var subtitles = "#EXTM3U\n#EXT-X-TARGETDURATION:2\n";
            for (var i = 0; i < 3; i++)
            {
                if (i > 0 && scenario is not ("same-group-ad" or "coarse-subtitles"))
                {
                    video += "#EXT-X-DISCONTINUITY\n";
                    subtitles += "#EXT-X-DISCONTINUITY\n";
                }
                var media = (i == 1 ? "ad-" : "") + $"media-{i}.{extension}";
                if (i == 1)
                    File.Move(Path.Combine(root, $"media-{i}.{extension}"), Path.Combine(root, media));
                if (scenario == "reset-clock" && i == 2)
                {
                    var path = Path.Combine(root, media);
                    var bytes = await File.ReadAllBytesAsync(path);
                    var tfdt = bytes.AsSpan().IndexOf("tfdt"u8);
                    Assert.True(tfdt >= 0);
                    bytes.AsSpan(tfdt + 8, bytes[tfdt + 4] == 1 ? 8 : 4).Clear();
                    await File.WriteAllBytesAsync(path, bytes);
                }
                video += $"#EXTINF:2,\n{media}\n";
                subtitles += $"#EXTINF:2,\nsub-{i}.vtt\n";
                // PTS 连续但 discontinuity 可以存在，不能假定每段从零开始；另覆盖 LOCAL 非零。
                var start = timestampMap ? 10 : scenario == "reset-clock" && i == 2 ? 0 : i * 2;
                var map = timestampMap ? $"X-TIMESTAMP-MAP=LOCAL:00:00:10.000,MPEGTS:{i * 180000}\n" : "";
                if (ts)
                    map = "X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:126000\n";
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"),
                    $"WEBVTT\n{map}\n00:00:{start:00}.250 --> 00:00:{start + 1:00}.000\ncue-{i}\n\n");
            }
            if (scenario == "coarse-subtitles")
            {
                subtitles = "#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\ncoarse.vtt\n";
                await File.WriteAllTextAsync(Path.Combine(root, "coarse.vtt"), "WEBVTT\n\n" +
                    string.Join('\n', Enumerable.Range(0, 3).Select(i => $"00:00:0{i * 2}.250 --> 00:00:0{i * 2 + 1}.000\ncue-{i}\n")));
            }
            await File.WriteAllTextAsync(Path.Combine(root, "video.m3u8"), video + "#EXT-X-ENDLIST\n");
            await File.WriteAllTextAsync(Path.Combine(root, "subs.m3u8"), subtitles + "#EXT-X-ENDLIST\n");
            await File.WriteAllTextAsync(Path.Combine(root, "master.m3u8"), """
                #EXTM3U
                #EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID="s",LANGUAGE="en",NAME="en",URI="subs.m3u8"
                #EXT-X-STREAM-INF:BANDWIDTH=100000,CODECS="avc1.64001e",RESOLUTION=160x90,SUBTITLES="s"
                video.m3u8
                """);
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "master.m3u8");
            var streams = await extractor.ExtractStreamsAsync();
            await extractor.FetchPlayListAsync(streams);
            VodStreamPlanner.CaptureHlsTimeline(streams);
            if (scenario is "drop" or "reset-clock" or "cenc-drop")
                VodPartSelector.Apply(streams, "1");
            if (scenario == "range" || ts)
                FilterUtil.ApplyCustomRange(streams,
                new CustomRange { StartSegIndex = 1, EndSegIndex = 2, InputStr = "1-2" });
            FilterUtil.CleanAd(streams, scenario is "url-ad" or "same-group-ad" or "coarse-subtitles" ? ["ad-"] : null);
            VodStreamPlanner.AlignHlsDiscontinuities(streams);
            await DownloadCutStreams(root, streams, extractor, binaryMerge: scenario == "ts-binary-range", cenc: scenario == "cenc-drop");
            var output = Assert.Single(Directory.GetFiles(Path.Combine(root, "out")), p => p.EndsWith(".mp4") || p.EndsWith(".ts"));
            await AssertVideo(output, 4, 100);
            if (ts)
            {
                var startTime = await Run("ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries", "stream=start_time", "-of", "default=noprint_wrappers=1:nokey=1", output);
                Assert.All(startTime.Split('\n', StringSplitOptions.RemoveEmptyEntries), time =>
                    Assert.InRange(double.Parse(time, CultureInfo.InvariantCulture), -0.001, 0.001));
            }
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt")));
            Assert.Equal(2, sub.Cues.Count);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal($"cue-{(scenario == "range" || ts ? i + 1 : i * 2)}", sub.Cues[i].Payload);
                Assert.InRange(sub.Cues[i].StartTime.TotalSeconds, i * 2 + 0.249, i * 2 + 0.251);
                Assert.InRange(sub.Cues[i].EndTime.TotalSeconds, i * 2 + 0.999, i * 2 + 1.001);
            }
            Assert.Equal(ts ? 0 : 1, server.RequestCount("init.mp4"));
            if (scenario != "range" && !ts)
                Assert.Equal(0, server.RequestCount("ad-media-1.m4s"));
            if (scenario == "cenc-drop")
            {
                var expected = await FrameHashes(Path.Combine(root, "full.mp4"));
                Assert.Equal(expected.Take(50).Concat(expected.Skip(100)), await FrameHashes(output));
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashDropsIncompatibleAdBeforePlanningAndMovesSubtitles(bool byUrl)
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-dash-cuts-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var periods = new List<string>();
            for (var i = 0; i < 3; i++)
            {
                await File.WriteAllTextAsync(Path.Combine(root, $"sub-{i}.vtt"),
                    $"WEBVTT\n\n00:00:0{i * 2}.250 --> 00:00:0{i * 2 + 1}.000\ncue-{i}\n\n");
                periods.Add($$"""
                    <Period duration="PT2S">
                      <AdaptationSet mimeType="video/mp4"><Representation id="v{{i}}" codecs="{{(i == 1 ? "hev1.1.6.L93" : "avc1.64001e")}}">
                        <SegmentList duration="2" presentationTimeOffset="{{i * 2}}"><Initialization sourceURL="{{(i == 1 ? "ad-init.mp4" : "init.mp4")}}"/>
                          <SegmentURL media="media-{{i}}.m4s"/></SegmentList>
                      </Representation></AdaptationSet>
                      <AdaptationSet mimeType="text/vtt" lang="en"><Representation id="s{{i}}">
                        <SegmentList duration="2" presentationTimeOffset="{{i * 2}}"><SegmentURL media="sub-{{i}}.vtt"/></SegmentList>
                      </Representation></AdaptationSet>
                    </Period>
                    """);
            }
            await File.WriteAllTextAsync(Path.Combine(root, "vod.mpd"),
                $"<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" type=\"static\" mediaPresentationDuration=\"PT6S\">{string.Join('\n', periods)}</MPD>");
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "vod.mpd");
            var sources = await extractor.ExtractStreamsAsync();
            StreamSpec[] seeds = [sources.First(s => s.MediaType is null or MediaType.VIDEO), sources.First(s => s.MediaType == MediaType.SUBTITLES)];
            Assert.Throws<NotSupportedException>(() => VodStreamPlanner.Build(sources, [seeds[0]]));
            if (byUrl)
                FilterUtil.CleanAd(sources, ["ad-init"]);
            else
                VodPartSelector.Apply(sources, "1");
            sources.RemoveAll(s => s.SegmentsCount == 0);
            var streams = VodStreamPlanner.Build(sources, seeds.ToList());
            VodStreamPlanner.AlignPeriods(streams);
            await DownloadCutStreams(root, streams, extractor);
            await AssertVideo(Path.Combine(root, "out", "result.mp4"), 4, 100);
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt")));
            Assert.Equal(["cue-0", "cue-2"], sub.Cues.Select(c => c.Payload));
            Assert.Equal([0.25, 2.25], sub.Cues.Select(c => c.StartTime.TotalSeconds));
            Assert.Equal(1, server.RequestCount("init.mp4"));
            Assert.Equal(0, server.RequestCount("ad-init.mp4"));
            Assert.Equal(0, server.RequestCount("sub-1.vtt"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DashCoarseSubtitleSpanningDeletedMediaIsSplitAndClipped()
    {
        if (!HasTool("ffmpeg") || !HasTool("ffprobe"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-dash-subtitle-gap-").FullName;
        try
        {
            await GenerateCutMedia(root);
            File.Move(Path.Combine(root, "media-1.m4s"), Path.Combine(root, "ad-media.m4s"));
            await File.WriteAllTextAsync(Path.Combine(root, "sub.vtt"), "WEBVTT\n\n00:00:01.000 --> 00:00:05.000\ncrossing\n\n");
            await File.WriteAllTextAsync(Path.Combine(root, "vod.mpd"), """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT6S">
                  <Period duration="PT6S">
                    <AdaptationSet mimeType="video/mp4"><Representation id="v" codecs="avc1.64001e">
                      <SegmentList duration="2"><Initialization sourceURL="init.mp4"/><SegmentURL media="media-0.m4s"/>
                        <SegmentURL media="ad-media.m4s"/><SegmentURL media="media-2.m4s"/></SegmentList>
                    </Representation></AdaptationSet>
                    <AdaptationSet mimeType="text/vtt" lang="en"><Representation id="s">
                      <SegmentList duration="6"><SegmentURL media="sub.vtt"/></SegmentList>
                    </Representation></AdaptationSet>
                  </Period>
                </MPD>
                """);
            await using var server = new MediaFixtureServer(root, (_, _) => null);
            using var extractor = new StreamExtractor(new ParserConfig());
            await extractor.LoadSourceFromUrlAsync(server.Url + "vod.mpd");
            var streams = await extractor.ExtractStreamsAsync();
            FilterUtil.CleanAd(streams, ["ad-"]);
            VodStreamPlanner.AlignPeriods(streams);
            await DownloadCutStreams(root, streams, extractor);
            await AssertVideo(Path.Combine(root, "out", "result.mp4"), 4, 100);
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(Path.Combine(root, "out", "result.en.vtt")));
            Assert.Equal([1d, 2d], sub.Cues.Select(c => c.StartTime.TotalSeconds));
            Assert.Equal([2d, 3d], sub.Cues.Select(c => c.EndTime.TotalSeconds));
            Assert.All(sub.Cues, c => Assert.Equal("crossing", c.Payload));
            Assert.Equal(1, server.RequestCount("init.mp4"));
        }
        finally { Directory.Delete(root, true); }
    }

    private static async Task DownloadCutStreams(string root, List<StreamSpec> streams, StreamExtractor extractor, bool binaryMerge = false, bool cenc = false)
    {
        var options = CreateOptions(root);
        options.SubtitleFormat = SubtitleFormat.VTT;
        options.ConcurrentDownload = true;
        options.BinaryMerge = binaryMerge;
        if (cenc)
        {
            options.Keys = [$"{new string('1', 32)}:{new string('a', 32)}"];
            options.DecryptionBinaryPath = "mp4decrypt";
            options.MP4RealTimeDecryption = true;
        }
        var manager = new SimpleDownloadManager(new DownloaderConfig { DirPrefix = Path.Combine(root, "tmp"), MyOptions = options }, streams, extractor);
        Assert.True(await manager.StartDownloadAsync());
    }
}
