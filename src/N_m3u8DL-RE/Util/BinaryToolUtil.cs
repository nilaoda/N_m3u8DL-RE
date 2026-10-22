using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Util;

internal static class BinaryToolUtil
{
    // MP4 的杜比视界配置及增强层信息要求非严格标准模式，各条转封装路径共用此参数。
    internal const string FFmpegStrictOptions = "-strict unofficial";
    internal static readonly string[] FFmpegStrictArguments = FFmpegStrictOptions.Split(' ');
    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> DolbyVisionSupport = new();

    internal static bool FFmpegSupportsDolbyVision(string versionOutput)
    {
        // FFmpeg 9 的 libavformat 63 完整保留 Profile 7 的 hvcE；按库版本兼容发行版和 Git 构建。
        var match = Regex.Match(versionOutput, @"(?m)^\s*libavformat\s+(\d+)\.");
        return match.Success && int.TryParse(match.Groups[1].Value, out var major) && major >= 63;
    }

    internal static async Task<bool> FFmpegSupportsDolbyVisionAsync(string? configuredPath)
    {
        var binary = Resolve("ffmpeg", configuredPath);
        if (binary == null)
            return false;
        // 点播各段、并发轨道及选项副本共享一次探测，不能逐分片启动外部进程。
        return await DolbyVisionSupport.GetOrAdd(binary, path => new(() => ProbeDolbyVisionAsync(path)))
            .Value.ConfigureAwait(false);
    }

    private static async Task<bool> ProbeDolbyVisionAsync(string binary)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var result = await ProcessUtil.RunAsync(binary, ["-version"], timeout.Token).ConfigureAwait(false);
            return result.ExitCode == 0 && FFmpegSupportsDolbyVision(result.Output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or OperationCanceledException)
        {
            return false;
        }
    }

    internal static string? FindShakaPackager()
    {
        var file = GlobalUtil.FindExecutable("shaka-packager");
        if (file != null) return file;

        // 按照架构优先搜索同架构二进制
        var names = new List<string>();
        if (OperatingSystem.IsLinux())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                names.Add("packager-linux-arm64");
                names.Add("packager-linux-x64");
            }
            else
            {
                names.Add("packager-linux-x64");
                names.Add("packager-linux-arm64");
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
            {
                names.Add("packager-osx-arm64");
                names.Add("packager-osx-x64");
            }
            else
            {
                names.Add("packager-osx-x64");
                names.Add("packager-osx-arm64");
            }
        }
        else if (OperatingSystem.IsWindows())
        {
            names.Add("packager-win-x64");
        }

        foreach (var name in names)
        {
            file = GlobalUtil.FindExecutable(name);
            if (file != null) return file;
        }

        return null;
    }

    internal static string? Resolve(string name, string? configuredPath)
    {
        if (configuredPath == null)
            return GlobalUtil.FindExecutable(name);
        if (File.Exists(configuredPath))
            return Path.GetFullPath(configuredPath);
        return Path.IsPathRooted(configuredPath) || configuredPath.Contains(Path.DirectorySeparatorChar) ||
            configuredPath.Contains(Path.AltDirectorySeparatorChar) ? null : GlobalUtil.FindExecutable(configuredPath);
    }
}
