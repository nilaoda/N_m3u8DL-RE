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
}
