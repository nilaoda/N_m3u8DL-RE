using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Runtime;

public class TaskCancellationTests
{
    [Fact]
    public async Task AmbientCancellationStopsExternalTool()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var previous = OperationCancellation.Token;
        OperationCancellation.Token = stop.Token;
        try
        {
            var binary = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
            string[] arguments = OperatingSystem.IsWindows()
                ? ["/c", "ping -n 30 127.0.0.1 >nul"] : ["-c", "sleep 30"];
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProcessUtil.RunAsync(binary, arguments, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            OperationCancellation.Token = previous;
        }
    }
}
