using System.Diagnostics;
using System.Net;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Tests.TestSupport;
using static N_m3u8DL_RE.Tests.TestSupport.DownloadTestHelper;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class RecordingExitCodeTests
{
    [Fact]
    public async Task FatalLiveRefreshSetsNonzeroProcessExitCode()
    {
        if (!HasTool("ffmpeg"))
            return;
        var root = Directory.CreateTempSubdirectory("live-exit-code-").FullName;
        try
        {
            await using var server = new MediaFixtureServer(root,
                (_, _) => "#EXTM3U\n#EXT-X-TARGETDURATION:2\n",
                responseStatus: (_, count) => count > 1 ? HttpStatusCode.Forbidden : null);
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            string[] args = [typeof(CommandInvoker).Assembly.Location, server.Url + "live.m3u8",
                "--auto-select", "--no-log", "--no-ansi-color", "--live-wait-time", "1",
                "--save-name", "check", "--save-dir", Path.Combine(root, "out"), "--tmp-dir", Path.Combine(root, "tmp")];
            foreach (var arg in args)
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                var output = await stdout + await stderr;
                Assert.Equal(1, process.ExitCode);
                Assert.Contains("Failed", output);
                Assert.DoesNotContain("Done", output);
                Assert.Equal(2, server.RequestCount("live.m3u8"));
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
