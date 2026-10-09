using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Tests.Util;

public sealed class DoctorUtilTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-doctor-").FullName;

    [Fact]
    public async Task ReportsEnvironmentImmediatelyAndToolsAsTheyFinish()
    {
        if (OperatingSystem.IsWindows())
            return;
        // 首次启动超过原来的 5 秒时限，仍应正常报告版本。
        var slow = Script("/bin/sleep 6\nprintf 'ffmpeg version test\\n'\n");
        var fast = Script("printf 'mkvmerge v1.0\\n'\n");
        var started = false;
        var fastCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = DoctorUtil.InspectAsync(slow, fast, null, DecryptEngine.MP4DECRYPT, null, true, default,
            report =>
            {
                started = true;
                Assert.Empty(report.Tools);
                Assert.False(report.ConfigurationEnabled);
            },
            tool =>
            {
                if (tool.Name == "mkvmerge")
                    fastCompleted.SetResult();
            });
        Assert.True(started);
        await fastCompleted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(task.IsCompleted);
        var report = await task;
        Assert.Equal(new[] { "ffmpeg", "mkvmerge", "mp4decrypt", "shaka-packager" }, report.Tools.Select(t => t.Name));
        Assert.All(report.Tools.Take(2), tool => Assert.Equal("ok", tool.Status));
    }

    [Fact]
    public async Task MissingAndUnlaunchableToolsHaveDifferentStatuses()
    {
        var missing = await DoctorUtil.ProbeAsync("ffmpeg", null, ["-version"], true, false, default);
        Assert.Equal("missing", missing.Status);
        var broken = await DoctorUtil.ProbeAsync("ffmpeg", Path.Combine(directory, "missing"), ["-version"], true, true, default);
        Assert.Equal("error", broken.Status);
        Assert.NotNull(broken.Detail);
    }

    [Fact]
    public async Task ReadsVersionFromStderrAndAcceptsBento4UsageExitCode()
    {
        if (OperatingSystem.IsWindows())
            return;
        var path = Script("printf 'MP4 Decrypter - Version 1.4\\n' >&2\nexit 1\n");
        var result = await DoctorUtil.ProbeAsync("mp4decrypt", path, [], false, false, default);
        Assert.Equal("ok", result.Status);
        Assert.Equal("MP4 Decrypter - Version 1.4", result.Version);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public async Task WrongExecutableCannotPassTheVersionCheck()
    {
        if (OperatingSystem.IsWindows())
            return;
        var result = await DoctorUtil.ProbeAsync("ffmpeg", Script("printf 'other program version 1.0\\n'\n"),
            ["-version"], true, true, default);
        Assert.Equal("error", result.Status);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public async Task FailingVersionProbeIsReported()
    {
        if (OperatingSystem.IsWindows())
            return;
        var path = Script("printf 'wrong architecture\\n' >&2\nexit 2\n");
        var result = await DoctorUtil.ProbeAsync("ffmpeg", path, ["-version"], true, true, default);
        Assert.Equal("error", result.Status);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("wrong architecture", result.Detail);
    }

    [Fact]
    public async Task HungToolTimesOutAndLargeOutputDoesNotBlock()
    {
        if (OperatingSystem.IsWindows())
            return;
        var path = Script("exec /bin/sleep 30\n");
        var report = await DoctorUtil.InspectAsync(path, null, null, DecryptEngine.MP4DECRYPT, null, true, default,
            timeout: TimeSpan.FromMilliseconds(100));
        var result = report.Tools.Single(tool => tool.Name == "ffmpeg");
        Assert.Equal("timeout", result.Status);
        var verbose = Script("printf 'ffmpeg version test\\n'\ni=0\nwhile [ $i -lt 20000 ]; do printf 'configuration details\\n' >&2; i=$((i+1)); done\n");
        result = await DoctorUtil.ProbeAsync("ffmpeg", verbose, ["-version"], true, true, default);
        Assert.Equal("ok", result.Status);
        Assert.Equal("ffmpeg version test", result.Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationPropagatesRatherThanBeingReportedAsTimeout(bool unlimited)
    {
        if (OperatingSystem.IsWindows())
            return;
        using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DoctorUtil.ProbeAsync("ffmpeg",
            Script("exec /bin/sleep 30\n"), ["-version"], true, true, token.Token,
            unlimited ? TimeSpan.Zero : null));
    }

    private string Script(string text)
    {
        var path = Path.Combine(directory, "fake tool " + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "#!/bin/sh\n" + text);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    public void Dispose() => Directory.Delete(directory, true);
}
