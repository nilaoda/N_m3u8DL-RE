using System.CommandLine;
using System.Diagnostics;
using System.Reflection;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class HttpRequestTimeoutOptionTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("100", false)]
    [InlineData("0.5", false)]
    [InlineData(null, true)]
    [InlineData("100", true)]
    public void ParsingDistinguishesManualTimeoutFromDefault(string? seconds, bool asVod)
    {
        var command = new RootCommand();
        foreach (var field in typeof(CommandInvoker).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (field.GetValue(null) is Option option)
                command.Options.Add(option);
            else if (field.GetValue(null) is Argument argument)
                command.Arguments.Add(argument);
        }
        List<string> args = ["https://example.test/live.mpd"];
        if (seconds != null)
            args.AddRange(["--http-request-timeout", seconds]);
        if (asVod)
            args.Add("--live-perform-as-vod");
        var result = command.Parse(args.ToArray());
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(seconds != null, options.HttpRequestTimeoutSpecified);
        Assert.Equal(seconds == "0.5" ? 0.5 : 100, options.HttpRequestTimeout);
        Assert.Equal(asVod, options.LivePerformAsVod);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task VodAllowsSlowResponsesWhileLiveCapsManualTimeoutBySegmentDuration(bool asVod, bool manualLive)
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("vod-timeout-scope-").FullName;
        try
        {
            await GenerateCutMedia(root);
            var manifest = await File.ReadAllTextAsync(Path.Combine(root, "source.m3u8"));
            if (asVod)
                manifest = manifest.Replace("#EXT-X-ENDLIST", "");
            var mediaRequests = 0;
            await using var server = new MediaFixtureServer(root, (path, version) => path != "source.m3u8" ? null :
                manualLive && version == 0 ? manifest.Replace("#EXT-X-ENDLIST", "") : manifest,
                async path =>
                {
                    // 点播可等待慢响应；直播即使指定 10 秒也须在两倍片长后转入补片。
                    if (path == "media-1.m4s" && (!manualLive || Interlocked.Increment(ref mediaRequests) == 1))
                        await Task.Delay(TimeSpan.FromSeconds(6));
                });
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            List<string> args = [typeof(CommandInvoker).Assembly.Location, server.Url + "source.m3u8",
                "--auto-select", "--no-log", "--no-ansi-color", "--skip-merge", "--save-name", "check",
                "--save-dir", Path.Combine(root, "out"), "--tmp-dir", Path.Combine(root, "tmp")];
            if (asVod)
                args.Add("--live-perform-as-vod");
            if (manualLive)
                args.AddRange(["--http-request-timeout", "10", "--live-wait-time", "1"]);
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                var output = await stdout + await stderr;
                Assert.True(process.ExitCode == 0, output);
                Assert.Equal(manualLive ? 2 : 1, server.RequestCount("media-1.m4s"));
                var retained = Directory.GetFiles(Path.Combine(root, "tmp"), "*", SearchOption.AllDirectories)
                    .Select(File.ReadAllBytes).ToArray();
                for (var i = 0; i < 3; i++)
                {
                    var expected = await File.ReadAllBytesAsync(Path.Combine(root, $"media-{i}.m4s"));
                    Assert.Contains(retained, bytes => bytes.AsSpan().SequenceEqual(expected));
                }
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
