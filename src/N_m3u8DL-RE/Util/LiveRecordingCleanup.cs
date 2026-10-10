using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Util;

internal sealed class LiveRecordingCleanup
{
    private readonly string root;
    private readonly string resolvedRoot;
    private readonly StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly HashSet<string> files = [];
    private readonly HashSet<string> mergedFiles = [];
    private readonly HashSet<string> directories = [];
    private readonly Lock lockObj = new();

    public LiveRecordingCleanup(string root)
    {
        this.root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        resolvedRoot = FileToolUtil.ResolvePath(this.root);
        directories.Add(this.root);
    }

    // 在创建前登记；下载器复用的旧 init、旧解密文件不能作为本次临时文件删除。
    public void TrackFile(string path)
    {
        path = Path.GetFullPath(path);
        lock (lockObj)
        {
            if (!Path.Exists(path) && IsSafePath(path))
                files.Add(path);
        }
    }

    public void TrackDirectory(string path)
    {
        lock (lockObj)
            directories.Add(Path.GetFullPath(path));
    }

    // 该批次已成功写入并刷新输出后，关联的 init 才能在录制结束时清理。
    public void MarkMerged(IEnumerable<string> paths)
    {
        lock (lockObj)
        {
            foreach (var path in paths.Select(Path.GetFullPath))
            {
                if (files.Contains(path))
                    mergedFiles.Add(path);
            }
        }
    }

    private bool IsSafePath(string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return false;
        // 配置的暂存目录可以经过链接，但任务根及其内部不能经过链接，也不能在录制期间换目标。
        for (var current = path; current != null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return false;
            if (string.Equals(current, root, comparison))
                return string.Equals(FileToolUtil.ResolvePath(root), resolvedRoot, comparison);
        }
        return false;
    }

    public void DeleteFile(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
            if (IsSafePath(path) && File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Logger.Warn(ex.Message);
        }
    }

    public void Cleanup()
    {
        foreach (var file in mergedFiles)
            DeleteFile(file);
        // 只检查本次使用的轨道目录和任务根；不扫描未知子目录，不向用户的暂存目录递归。
        foreach (var directory in directories.OrderByDescending(path => path.Length))
        {
            try
            {
                if (IsSafePath(directory) && Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.Warn(ex.Message);
            }
        }
    }
}
