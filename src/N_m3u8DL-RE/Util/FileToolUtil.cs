using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using Spectre.Console;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace N_m3u8DL_RE.Util;

internal static class FileToolUtil
{
    internal static async Task<int> ExecuteAsync(string operation, string[] inputs, string output,
        string? directory, string pattern, bool overwrite, bool dryRun, string? ffmpegPath,
        string? mkvmergePath, string muxer, FFmpegConcatMode mode, bool autoSubtitleFix, CancellationToken token,
        OutputFile[]? files = null, string? title = null)
    {
        if (directory != null)
        {
            if (inputs.Length > 0)
                throw new ArgumentException(ResString.toolsInputConflict);
            inputs = Directory.GetFiles(Path.GetFullPath(directory), pattern)
                .OrderBy(path => Path.GetFileName(path)!, Comparer<string>.Create(CompareNatural)).ToArray();
        }
        if (inputs.Length == 0)
            throw new ArgumentException(ResString.toolsInputRequired);
        inputs = inputs.Select(Path.GetFullPath).ToArray();
        if (files != null)
        {
            for (var i = 0; i < files.Length; i++)
            {
                files[i].FilePath = inputs[i];
            }
        }
        output = Path.GetFullPath(output);
        foreach (var input in inputs)
            if (!File.Exists(input))
                throw new FileNotFoundException($"{ResString.toolsInputMissing}: {input}", input);
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var resolvedOutput = ResolvePath(output);
        if (inputs.Any(input => comparer.Equals(ResolvePath(input), resolvedOutput)))
            throw new ArgumentException(ResString.toolsOutputIsInput);
        if (OperatingSystem.IsWindows() && File.Exists(output))
        {
            // 本机盘符与 UNC 共享可以访问同一个文件，路径文本无法确认其身份。
            var outputId = ReadWindowsFileId(output);
            if (inputs.Any(input => ReadWindowsFileId(input) == outputId))
                throw new ArgumentException(ResString.toolsOutputIsInput);
        }
        if (File.Exists(output) && !overwrite)
            throw new IOException(ResString.toolsOutputExists);
        var format = Path.GetExtension(output).ToLowerInvariant();
        if (operation != "concat" && format is not (".mp4" or ".mkv" or ".ts" or ".m4a"))
            throw new ArgumentException(ResString.toolsFormatInvalid);
        if (operation == "mux" && muxer == "mkvmerge" && format != ".mkv")
            throw new ArgumentException(ResString.toolsMkvmergeFormat);
        Logger.Info(string.Format(ResString.toolsProcessing, operation, inputs.Length));
        Logger.InfoMarkUp($"[deepskyblue1]{output.EscapeMarkup()}[/]");
        if (dryRun)
        {
            if (files != null)
                foreach (var file in files)
                    LanguageCodeUtil.ConvertLangCodeAndDisplayName(file);
            for (var i = 0; i < inputs.Length; i++)
            {
                Logger.InfoMarkUp($"[grey][[{i}]][/] {inputs[i].EscapeMarkup()}");
                if (files != null && (files[i].LangCode != null || files[i].Description != null))
                    Logger.Info($"    lang={files[i].LangCode} name={files[i].Description}");
            }
            if (title != null)
                Logger.Info($"title={title}");
            Logger.Info(ResString.toolsDryRun);
            Console.WriteLine($"{operation} => {output}");
            return 0;
        }
        var binary = operation == "concat" ? null : muxer == "mkvmerge"
            ? BinaryToolUtil.Resolve("mkvmerge", mkvmergePath) : BinaryToolUtil.Resolve("ffmpeg", ffmpegPath);
        if (operation != "concat" && binary == null)
            throw new FileNotFoundException(muxer == "mkvmerge" ? ResString.mkvmergeNotFound : ResString.ffmpegNotFound);
        if (binary != null)
            Logger.Info($"{(muxer == "mkvmerge" ? "mkvmerge" : "ffmpeg")}: {binary}");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // 先写同目录临时文件，失败或取消时保留原输出；完成后才替换。
        var temporary = Path.Combine(Path.GetDirectoryName(output)!, $".re-{Guid.NewGuid():N}{format}");
        var success = await MediaProcessingProgress.RunAsync(operation, async processing =>
        {
            try
            {
                using var timeline = operation == "mux" && autoSubtitleFix
                    ? await MuxSubtitleTimeline.CreateAsync(inputs, ffmpegPath, token) : null;
                if (operation == "concat")
                {
                    processing.Begin(ResString.processingMerge);
                    await MergeUtil.CombineMultipleFilesIntoSingleFileAsync(inputs, temporary, overwrite: false, token: token, progress: processing.Report);
                }
                else
                {
                    processing.Begin(operation == "mux" ? ResString.processingMux : ResString.processingMerge);
                    var exitCode = operation == "mux"
                        ? await MergeUtil.MuxInputsAsync(binary!, files ?? inputs.Select(path => new OutputFile { Index = 999, FilePath = path }).ToArray(),
                            temporary, useMkvmerge: muxer == "mkvmerge", timeline: timeline, title: title, token: token, progress: processing.Report)
                        : await MergeUtil.MergeByFFmpegAsync(binary!, inputs, temporary, mode, token: token, progress: processing.Report);
                    // mkvmerge 的 1 表示成功但有警告，2 才是错误。
                    if (exitCode != 0 && !(muxer == "mkvmerge" && exitCode == 1))
                    {
                        Logger.Error(string.Format(ResString.toolsProcessFailed, operation, exitCode));
                        return false;
                    }
                }
                token.ThrowIfCancellationRequested();
                processing.Begin(ResString.processingFinishing);
                File.Move(temporary, output, overwrite);
                processing.Report(new(Bytes: new FileInfo(output).Length));
                return true;
            }
            finally
            {
                File.Delete(temporary);
            }
        });
        if (success)
            Console.WriteLine(output);
        return success ? 0 : 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        public uint Attributes;
        public FILETIME CreationTime;
        public FILETIME LastAccessTime;
        public FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint LinkCount;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out WindowsFileInformation information);

    private static (uint Volume, ulong Index) ReadWindowsFileId(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (!GetFileInformationByHandle(handle, out var information))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return (information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
    }

    private static string ResolvePath(string path)
    {
        // Windows 的扩展路径与普通盘符、UNC 路径使用同一形式比较。
        if (OperatingSystem.IsWindows())
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path[8..];
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path[4..];
        }
        var parent = Path.GetDirectoryName(path);
        if (parent == null)
            return path;
        // 逐层解析目录链接，避免输入与输出通过不同路径指向同一个源文件。
        var resolved = Path.Combine(ResolvePath(parent), Path.GetFileName(path));
        FileSystemInfo info;
        if (Directory.Exists(resolved))
            info = new DirectoryInfo(resolved);
        else if (File.Exists(resolved))
            info = new FileInfo(resolved);
        else
            return resolved;
        // 逐个解析链接，由上面的路径处理统一目标格式，也保留 UNC 前缀。
        var target = info.ResolveLinkTarget(returnFinalTarget: false);
        return target == null ? resolved : ResolvePath(target.FullName);
    }

    internal static int CompareNatural(string? left, string? right)
    {
        left ??= "";
        right ??= "";
        var i = 0;
        var j = 0;
        while (i < left.Length && j < right.Length)
        {
            if (char.IsAsciiDigit(left[i]) && char.IsAsciiDigit(right[j]))
            {
                var startLeft = i;
                var startRight = j;
                while (i < left.Length && char.IsAsciiDigit(left[i]))
                    i++;
                while (j < right.Length && char.IsAsciiDigit(right[j]))
                    j++;
                var a = left.AsSpan(startLeft, i - startLeft).TrimStart('0');
                var b = right.AsSpan(startRight, j - startRight).TrimStart('0');
                var comparison = a.Length.CompareTo(b.Length);
                if (comparison == 0)
                    comparison = a.SequenceCompareTo(b);
                if (comparison != 0)
                    return comparison;
            }
            else
            {
                var comparison = char.ToUpperInvariant(left[i++]).CompareTo(char.ToUpperInvariant(right[j++]));
                if (comparison != 0)
                    return comparison;
            }
        }
        var remaining = (left.Length - i).CompareTo(right.Length - j);
        return remaining != 0 ? remaining : StringComparer.Ordinal.Compare(left, right);
    }
}
