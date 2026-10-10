using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Tests.TestSupport;

// 共用的工具调用、媒体生成及校验方法；每次调用创建独立的选项和进程。
internal static class DownloadTestHelper
{
    internal static bool OnPath(string binary)
    {
        if (File.Exists(binary))
            return true;
        // Windows 的工具通常带 .exe，不能只检查不带扩展名的文件。
        string[] extensions = OperatingSystem.IsWindows()
            ? ["", ".exe"] : [""];
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Any(dir => extensions.Any(extension => File.Exists(Path.Combine(dir, binary + extension))));
    }

    internal static bool HasTool(string name)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(name, "-version")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    internal static async Task<string> Run(string binary, params string[] args)
    {
        var info = new ProcessStartInfo(binary) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await stderr);
        return await stdout;
    }

    // Windows 使用普通用户可创建的目录联接或文件硬链接；Unix 使用符号链接。
    internal static async Task CreateLink(string link, string target, bool directory)
    {
        if (OperatingSystem.IsWindows())
            await Run("cmd.exe", "/d", "/c", "mklink", directory ? "/J" : "/H", link, target);
        else if (directory)
            Directory.CreateSymbolicLink(link, target);
        else
            File.CreateSymbolicLink(link, target);
    }

    internal static MyOption CreateOptions(string root) => new()
    {
        SaveDir = Path.Combine(root, "out"), SaveName = "result", FFmpegBinaryPath = "ffmpeg",
        ThreadCount = 2, CheckSegmentsCount = true, DelAfterDone = true, NoAnsiColor = true,
        LogLevel = LogLevel.OFF, AutoSubtitleFix = true,
    };

    internal static async Task AssertVideo(string output, double duration, int frames)
    {
        // 验证真实媒体，不能仅以下载/合并退出码判断是否回归。
        using var probe = JsonDocument.Parse(await Run("ffprobe", "-v", "error", "-count_frames",
            "-show_entries", "format=duration:stream=codec_type,nb_read_frames", "-of", "json", output));
        Assert.InRange(double.Parse(probe.RootElement.GetProperty("format").GetProperty("duration").GetString()!,
            CultureInfo.InvariantCulture), duration - 0.05, duration + 0.05);
        var video = Assert.Single(probe.RootElement.GetProperty("streams").EnumerateArray(),
            s => s.GetProperty("codec_type").GetString() == "video");
        Assert.Equal(frames.ToString(), video.GetProperty("nb_read_frames").GetString());
        await Run("ffmpeg", "-v", "error", "-xerror", "-i", output, "-f", "null", "-");
    }

    internal static Task GenerateCutMedia(string root) => Run("ffmpeg", "-v", "error", "-y", "-f", "lavfi", "-i",
        "testsrc2=s=160x90:r=25", "-t", "6", "-c:v", "libx264", "-g", "50", "-bf", "0", "-f", "hls",
        "-hls_time", "2", "-hls_segment_type", "fmp4", "-hls_fmp4_init_filename", "init.mp4",
        "-hls_segment_filename", Path.Combine(root, "media-%d.m4s"), Path.Combine(root, "source.m3u8"));

    internal static async Task<string[]> FrameHashes(string path)
    {
        // 加密用例还比较解码后的逐帧像素，避免简单画面使漏解密的问题未被发现。
        var output = await Run("ffmpeg", "-v", "error", "-xerror", "-i", path, "-map", "0:v:0", "-f", "framemd5", "-");
        return output.Split('\n').Where(line => !line.StartsWith('#') && line.Contains(','))
            .Select(line => line.Split(',')[^1].Trim()).ToArray();
    }
}
