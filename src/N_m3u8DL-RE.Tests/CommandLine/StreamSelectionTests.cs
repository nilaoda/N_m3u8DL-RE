using System.Diagnostics;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class StreamSelectionTests
{
    [Theory]
    [InlineData("unmatched", 1)]
    [InlineData("drop-all", 1)]
    [InlineData("range", 1)]
    [InlineData("ad", 1)]
    [InlineData("matched", 0)]
    [InlineData("partial", 0)]
    public async Task EmptySelectionsReportAnErrorAndMatchingStreamsRemainUsable(string scenario, int expectedExitCode)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("stream-selection-").FullName;
        try
        {
            var hls = scenario is "range" or "ad";
            var manifest = Path.Combine(root, hls ? "source.m3u8" : "source.mpd");
            await File.WriteAllTextAsync(manifest, hls ? "#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\nmedia-0.ts\n#EXT-X-ENDLIST\n" : """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT2S">
                  <Period duration="PT2S">
                    <AdaptationSet mimeType="video/mp4">
                      <Representation id="video" codecs="avc1.64001e" bandwidth="100000">
                        <SegmentList duration="2"><SegmentURL media="video.mp4"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                    <AdaptationSet mimeType="text/vtt" lang="en-US">
                      <Role schemeIdUri="urn:mpeg:dash:role:2011" value="subtitle"/>
                      <Representation id="en" codecs="wvtt" bandwidth="1000">
                        <SegmentList duration="2"><SegmentURL media="en.vtt"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                    <AdaptationSet mimeType="text/vtt" lang="cs-CZ">
                      <Role schemeIdUri="urn:mpeg:dash:role:2011" value="forced-subtitle"/>
                      <Representation id="cs" codecs="wvtt" bandwidth="1000">
                        <SegmentList duration="2"><SegmentURL media="cs.vtt"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                  </Period>
                </MPD>
                """);
            string[] selection = scenario switch
            {
                "unmatched" => ["-dv", "-da", "-ss", "lang=hu-HU|en-US:role=ForcedSubtitle"],
                "drop-all" => ["-dv", "all", "-ds", "all"],
                "range" => ["--auto-select", "--custom-range", "10-20"],
                "ad" => ["--auto-select", "--ad-keyword", "media-"],
                "matched" => ["-ss", "lang=cs-CZ:role=ForcedSubtitle"],
                _ => ["-sv", "best", "-ss", "lang=hu-HU|en-US:role=ForcedSubtitle"],
            };
            var logFile = Path.Combine(root, "run.log");
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            string[] args = [typeof(CommandInvoker).Assembly.Location, manifest, "--no-config", "--no-ansi-color",
                "--disable-update-check", "--ui-language", "en-US", "--skip-download", "--save-name", "result",
                "--tmp-dir", Path.Combine(root, "tmp"), "--save-dir", Path.Combine(root, "out"),
                "--log-file-path", logFile, ..selection];
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout + await stderr;
            Assert.Equal(expectedExitCode, process.ExitCode);
            Assert.DoesNotContain("Unhandled exception", output);
            Assert.DoesNotContain("at N_m3u8DL_RE.", output);
            var selected = Path.Combine(root, "tmp", "result", "meta_selected.json");
            if (expectedExitCode != 0)
            {
                Assert.Contains("ERROR: No stream found to download", output);
                Assert.Contains("No stream found to download", await File.ReadAllTextAsync(logFile));
                Assert.False(File.Exists(selected));
                Assert.False(Directory.Exists(Path.Combine(root, "out")));
            }
            else
            {
                Assert.DoesNotContain("No stream found to download", output);
                using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(selected));
                var stream = Assert.Single(meta.RootElement.EnumerateArray());
                Assert.Equal(scenario == "matched" ? "SUBTITLES" : null,
                    stream.TryGetProperty("MediaType", out var type) ? type.GetString() : null);
                if (scenario == "matched")
                    Assert.Equal("cs-CZ", stream.GetProperty("Language").GetString());
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoSelectPrefersAudioGroupLinkedToSelectedVideo(bool reverseMediaOrder)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("auto-select-audio-").FullName;
        try
        {
            foreach (var name in new[] { "v360", "v1080", "a64", "a128", "fr64" })
                await File.WriteAllTextAsync(Path.Combine(root, name + ".m3u8"),
                    $"#EXTM3U\n#EXT-X-TARGETDURATION:2\n#EXTINF:2,\n{name}.ts\n#EXT-X-ENDLIST\n");
            // EXT-X-MEDIA 没有码率；英语低码率组排在前面时，此前会被自动选择。see #629
            string[] media =
            [
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aac-64\",NAME=\"English\",LANGUAGE=\"en\",CHANNELS=\"2\",URI=\"a64.m3u8\"",
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aac-128\",NAME=\"English\",LANGUAGE=\"en\",CHANNELS=\"2\",URI=\"a128.m3u8\"",
            ];
            if (reverseMediaOrder)
                Array.Reverse(media);
            var manifest = Path.Combine(root, "master.m3u8");
            await File.WriteAllTextAsync(manifest, string.Join('\n', [
                "#EXTM3U",
                ..media,
                "#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"aac-128\",NAME=\"Français\",LANGUAGE=\"fr\",CHANNELS=\"2\",URI=\"fr64.m3u8\"",
                "#EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360,CODECS=\"avc1.4d401e,mp4a.40.2\",AUDIO=\"aac-64\"",
                "v360.m3u8",
                "#EXT-X-STREAM-INF:BANDWIDTH=5000000,RESOLUTION=1920x1080,CODECS=\"avc1.640028,mp4a.40.2\",AUDIO=\"aac-128\"",
                "v1080.m3u8",
                ""]));
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            string[] args = [typeof(CommandInvoker).Assembly.Location, manifest, "--no-config", "--no-ansi-color",
                "--disable-update-check", "--ui-language", "en-US", "--skip-download", "--save-name", "result",
                "--tmp-dir", Path.Combine(root, "tmp"), "--save-dir", Path.Combine(root, "out"),
                "--log-file-path", Path.Combine(root, "run.log"), "--auto-select"];
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, output);

            using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "tmp", "result", "meta_selected.json")));
            var audios = meta.RootElement.EnumerateArray()
                .Where(s => s.TryGetProperty("MediaType", out var t) && t.GetString() == "AUDIO")
                .ToDictionary(s => s.GetProperty("Language").GetString()!, s => s.GetProperty("GroupId").GetString());
            Assert.Equal("aac-128", audios["en"]);
            Assert.Equal("aac-128", audios["fr"]);
            var video = Assert.Single(meta.RootElement.EnumerateArray(), s => !s.TryGetProperty("MediaType", out _));
            Assert.Equal("aac-128", video.GetProperty("AudioId").GetString());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AutoSelectKeepsChannelOrderForDashAudioWithEqualBandwidth()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("auto-select-dash-audio-").FullName;
        try
        {
            // DASH 的视频 AudioId 由解析器按码率推断；码率相同时不应覆盖声道数排序，应选 6 声道。
            var manifest = Path.Combine(root, "source.mpd");
            await File.WriteAllTextAsync(manifest, """
                <MPD xmlns="urn:mpeg:dash:schema:mpd:2011" type="static" mediaPresentationDuration="PT2S">
                  <Period duration="PT2S">
                    <AdaptationSet mimeType="video/mp4">
                      <Representation id="video" codecs="avc1.64001e" bandwidth="100000" width="1280" height="720">
                        <SegmentList duration="2"><SegmentURL media="video.mp4"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                    <AdaptationSet mimeType="audio/mp4" lang="en">
                      <AudioChannelConfiguration schemeIdUri="urn:mpeg:dash:23003:3:audio_channel_configuration:2011" value="2"/>
                      <Representation id="audio-2ch" codecs="mp4a.40.2" bandwidth="128000">
                        <SegmentList duration="2"><SegmentURL media="audio-2ch.mp4"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                    <AdaptationSet mimeType="audio/mp4" lang="en">
                      <AudioChannelConfiguration schemeIdUri="urn:mpeg:dash:23003:3:audio_channel_configuration:2011" value="6"/>
                      <Representation id="audio-6ch" codecs="ec-3" bandwidth="128000">
                        <SegmentList duration="2"><SegmentURL media="audio-6ch.mp4"/></SegmentList>
                      </Representation>
                    </AdaptationSet>
                  </Period>
                </MPD>
                """);
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
            };
            string[] args = [typeof(CommandInvoker).Assembly.Location, manifest, "--no-config", "--no-ansi-color",
                "--disable-update-check", "--ui-language", "en-US", "--skip-download", "--save-name", "result",
                "--tmp-dir", Path.Combine(root, "tmp"), "--save-dir", Path.Combine(root, "out"),
                "--log-file-path", Path.Combine(root, "run.log"), "--auto-select"];
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var output = await stdout + await stderr;
            Assert.True(process.ExitCode == 0, output);

            using var meta = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "tmp", "result", "meta_selected.json")));
            var audio = Assert.Single(meta.RootElement.EnumerateArray(),
                s => s.TryGetProperty("MediaType", out var t) && t.GetString() == "AUDIO");
            Assert.Equal("audio-6ch", audio.GetProperty("GroupId").GetString());
            Assert.Equal("6", audio.GetProperty("Channels").GetString());
        }
        finally { Directory.Delete(root, true); }
    }
}
