using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Common.Enum;
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
        // 目录输入先规划分片，输出保护必须覆盖源目录中的全部文件，包括未选用的原始 init。
        var tracks = new RecordingTrack?[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(inputs[i]))
                tracks[i] = RecordingTrack.Read(files?[i] ?? new OutputFile { Index = i, FilePath = inputs[i] }, token, binaryConcat: operation == "concat");
            else if (!File.Exists(inputs[i]))
                throw new FileNotFoundException($"{ResString.toolsInputMissing}: {inputs[i]}", inputs[i]);
        }
        var sources = inputs.SelectMany((input, i) => tracks[i]?.Sources ?? [input]).ToArray();
        output = Path.GetFullPath(output);
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var resolvedOutput = ResolvePath(output);
        if (sources.Any(input => comparer.Equals(ResolvePath(input), resolvedOutput)))
            throw new ArgumentException(ResString.toolsOutputIsInput);
        if (OperatingSystem.IsWindows() && File.Exists(output))
        {
            // 本机盘符与 UNC 共享可以访问同一个文件，路径文本无法确认其身份。
            var outputId = ReadWindowsFileId(output);
            if (sources.Any(input => ReadWindowsFileId(input) == outputId))
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
        Logger.InfoMarkUp($"[deepskyblue3]{output.EscapeMarkup()}[/]");
        if (dryRun)
        {
            if (files != null)
                foreach (var file in files)
                    LanguageCodeUtil.ConvertLangCodeAndDisplayName(file);
            for (var i = 0; i < inputs.Length; i++)
            {
                Logger.InfoMarkUp($"[grey][[{i}]][/] {inputs[i].EscapeMarkup()}");
                if (tracks[i] is { } track)
                {
                    foreach (var segment in track.Inputs)
                        Logger.Info($"    {Path.GetFileName(segment)}");
                    LanguageCodeUtil.ConvertLangCodeAndDisplayName(track.Info);
                }
                var info = tracks[i]?.Info ?? files?[i];
                if (info != null && (info.LangCode != null || info.Description != null))
                    Logger.Info($"    lang={info.LangCode} name={info.Description}");
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
            string? trackDirectory = null;
            var servers = new List<ConcatInputServer>();
            try
            {
                var muxFiles = files ?? inputs.Select(path => new OutputFile { Index = 999, FilePath = path }).ToArray();
                if (operation == "mux" && tracks.Any(track => track != null))
                {
                    // 字幕转换及 mkvmerge 中间轨道写到输出目录，源录制目录始终只读。
                    trackDirectory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!,
                        $".re-tracks-{Guid.NewGuid():N}")).FullName;
                    var prepared = new List<OutputFile>();
                    for (var i = 0; i < tracks.Length; i++)
                    {
                        if (tracks[i] is not { } track)
                        {
                            prepared.Add(muxFiles[i]);
                            continue;
                        }
                        processing.Begin(muxer == "ffmpeg" && track.Info.MediaType != MediaType.SUBTITLES
                            ? ResString.processingPreparing : $"{ResString.processingMerge}: {Path.GetFileName(track.Info.FilePath)}");
                        var file = await track.PrepareAsync(trackDirectory, i, token, processing.Report, virtualInput: muxer == "ffmpeg");
                        if (file != null)
                            prepared.Add(file);
                    }
                    muxFiles = prepared.ToArray();
                    if (muxFiles.Length == 0)
                        throw new ArgumentException(ResString.toolsRecordingEmpty);
                    inputs = muxFiles.Select(file => file.FilePath).ToArray();
                }
                else if (operation != "mux")
                {
                    inputs = inputs.SelectMany((input, i) =>
                    {
                        if (operation == "merge" && mode == FFmpegConcatMode.DEMUXER && tracks[i] is { HasInitialization: true } track)
                        {
                            // init 和裸分片必须作为完整轨道读取；demuxer 仍逐个处理不同输入。
                            var server = new ConcatInputServer(track.Inputs);
                            servers.Add(server);
                            return new[] { server.Url };
                        }
                        return tracks[i]?.Inputs ?? [input];
                    }).ToArray();
                }
                using var timeline = operation == "mux" && autoSubtitleFix
                    ? await MuxSubtitleTimeline.CreateAsync(muxFiles, ffmpegPath, token,
                        alignMedia: tracks.Any(track => track != null), temporaryDirectory: trackDirectory) : null;
                var muxInputs = timeline?.Inputs.ToArray() ?? inputs.ToArray();
                if (operation == "mux" && muxer == "ffmpeg")
                {
                    for (var i = 0; i < muxFiles.Length; i++)
                    {
                        if (muxFiles[i].Segments is not { } segments)
                            continue;
                        var server = new ConcatInputServer(segments);
                        servers.Add(server);
                        muxInputs[i] = "concat:" + server.Url;
                    }
                }
                if (operation == "concat")
                {
                    processing.Begin(ResString.processingMerge);
                    await MergeUtil.CombineMultipleFilesIntoSingleFileAsync(inputs, temporary, overwrite: false, token: token, progress: processing.Report);
                }
                else
                {
                    processing.Begin(operation == "mux" ? ResString.processingMux : ResString.processingMerge);
                    var exitCode = operation == "mux"
                        ? await MergeUtil.MuxInputsAsync(binary!, muxFiles,
                            temporary, useMkvmerge: muxer == "mkvmerge", timeline: timeline, title: title, token: token, progress: processing.Report,
                            inputPaths: muxInputs, loopbackInput: servers.Count > 0)
                        : await MergeUtil.MergeByFFmpegAsync(binary!, inputs, temporary, mode, token: token, progress: processing.Report,
                            inputPaths: servers.Count > 0 ? inputs : null, loopbackInput: servers.Count > 0);
                    foreach (var server in servers)
                        if (server.Error is { } error)
                            throw new IOException(string.Format(ResString.ffmpegConcatInputFailed, error.Message), error);
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
                try
                {
                    File.Delete(temporary);
                }
                finally
                {
                    foreach (var server in servers)
                        server.Dispose();
                    if (trackDirectory != null)
                        Directory.Delete(trackDirectory, recursive: true);
                }
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

    internal static string ResolvePath(string path)
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
