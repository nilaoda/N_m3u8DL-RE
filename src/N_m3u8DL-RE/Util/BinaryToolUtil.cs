using System.Runtime.InteropServices;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Util;

internal static class BinaryToolUtil
{
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
