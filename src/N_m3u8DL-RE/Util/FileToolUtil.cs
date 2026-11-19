using N_m3u8DL_RE.Runtime;
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
    internal static async Task<FileTaskResult> ExecuteAsync(FileTaskOptions options, CancellationToken token)
    {
        var operation = options.Operation;
        var output = options.Output;
        if (operation is not ("concat" or "merge" or "mux"))
            throw new ArgumentException("Unsupported file operation.");
        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Output is required.");
        token.ThrowIfCancellationRequested();
        var files = options.Inputs;
        files = ToolInputResolver.Expand(operation, files, options.Pattern, token);
        var inputs = files.Select(file => file.FilePath).ToArray();
        // 目录输入先规划分片，输出保护必须覆盖源目录中的全部文件，包括未选用的原始 init。
        var tracks = new RecordingTrack?[inputs.Length];
        for (var i = 0; i < inputs.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(inputs[i]))
                tracks[i] = RecordingTrack.Read(files[i], token, binaryConcat: operation == "concat");
            else if (!File.Exists(inputs[i]))
                throw new FileNotFoundException($"{ResString.toolsInputMissing}: {inputs[i]}", inputs[i]);
        }
        var mode = options.ConcatMode ?? ToolInputResolver.SelectMergeMode(inputs, tracks);
        var sources = inputs.SelectMany((input, i) => tracks[i]?.Sources ?? [input]).ToArray();
        output = Path.GetFullPath(output);
        var comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var resolvedOutput = ResolvePath(output);
        var conflictingInput = sources.FirstOrDefault(input => comparer.Equals(ResolvePath(input), resolvedOutput));
        if (conflictingInput == null && OperatingSystem.IsWindows() && File.Exists(output))
        {
            // 本机盘符与 UNC 共享可以访问同一个文件，路径文本无法确认其身份。
            var outputId = ReadWindowsFileId(output);
            conflictingInput = sources.FirstOrDefault(input => ReadWindowsFileId(input) == outputId);
        }
        if (conflictingInput != null)
            throw new ArgumentException(string.Format(ResString.toolsOutputIsInput, output, conflictingInput, Environment.NewLine));
        if (File.Exists(output) && !options.Overwrite)
            throw new IOException(ResString.toolsOutputExists);
        var format = Path.GetExtension(output).ToLowerInvariant();
        if (operation != "concat" && format is not (".mp4" or ".mkv" or ".ts" or ".m4a"))
            throw new ArgumentException(ResString.toolsFormatInvalid);
        if (operation == "mux" && options.Muxer == "mkvmerge" && format != ".mkv")
            throw new ArgumentException(ResString.toolsMkvmergeFormat);
        Logger.Info(string.Format(ResString.toolsProcessing, operation, inputs.Length));
        Logger.InfoMarkUp($"[deepskyblue3]{output.EscapeMarkup()}[/]");
        if (operation == "merge")
            Logger.Info(string.Format(ResString.toolsMergeMode, mode));
        if (options.DryRun)
        {
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
                var info = tracks[i]?.Info ?? files[i];
                if (info.LangCode != null || info.Description != null)
                    Logger.Info($"    lang={info.LangCode} name={info.Description}");
            }
            if (options.Title != null)
                Logger.Info($"title={options.Title}");
            Logger.Info(ResString.toolsDryRun);
            return new(true, output);
        }
        var binary = operation == "concat" ? null : options.Muxer == "mkvmerge"
            ? BinaryToolUtil.Resolve("mkvmerge", options.MkvmergeBinaryPath) : BinaryToolUtil.Resolve("ffmpeg", options.FFmpegBinaryPath);
        if (operation != "concat" && binary == null)
            throw new FileNotFoundException(options.Muxer == "mkvmerge" ? ResString.mkvmergeNotFound : ResString.ffmpegNotFound);
        if (binary != null)
            Logger.Info($"{(options.Muxer == "mkvmerge" ? "mkvmerge" : "ffmpeg")}: {binary}");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // 先写同目录临时文件，失败或取消时保留原输出；完成后才替换。
        var temporary = Path.Combine(Path.GetDirectoryName(output)!, $".re-{Guid.NewGuid():N}{format}");
        string? errorMessage = null;
        var success = await MediaProcessingProgress.RunAsync(operation, async processing =>
        {
            string? trackDirectory = null;
            var servers = new List<ConcatInputServer>();
            try
            {
                var muxFiles = files;
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
                        var preparing = options.Muxer == "ffmpeg" && track.Info.MediaType != MediaType.SUBTITLES;
                        processing.Begin(preparing ? MediaProcessingStage.Preparing : MediaProcessingStage.Merging,
                            preparing ? null : $"{ResString.processingMerge}: {Path.GetFileName(track.Info.FilePath)}");
                        var file = await track.PrepareAsync(trackDirectory, i, token, processing.Report, virtualInput: options.Muxer == "ffmpeg");
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
                using var timeline = operation == "mux" && options.AutoSubtitleFix
                    ? await MuxSubtitleTimeline.CreateAsync(muxFiles, options.FFmpegBinaryPath, token,
                        alignMedia: tracks.Any(track => track != null), temporaryDirectory: trackDirectory) : null;
                var muxInputs = timeline?.Inputs.ToArray() ?? inputs.ToArray();
                if (operation == "mux" && options.Muxer == "ffmpeg")
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
                    processing.Begin(MediaProcessingStage.Merging);
                    await MergeUtil.CombineMultipleFilesIntoSingleFileAsync(inputs, temporary, overwrite: false, token: token, progress: processing.Report);
                }
                else
                {
                    processing.Begin(operation == "mux" ? MediaProcessingStage.Muxing : MediaProcessingStage.Merging);
                    var exitCode = operation == "mux"
                        ? await MergeUtil.MuxInputsAsync(binary!, muxFiles,
                            temporary, useMkvmerge: options.Muxer == "mkvmerge", timeline: timeline, title: options.Title, token: token, progress: processing.Report,
                            inputPaths: muxInputs, loopbackInput: servers.Count > 0)
                        : await MergeUtil.MergeByFFmpegAsync(binary!, inputs, temporary, mode, token: token, progress: processing.Report,
                            inputPaths: servers.Count > 0 ? inputs : null, loopbackInput: servers.Count > 0,
                            mediaInfos: tracks[0]?.Info.Mediainfos is { Count: > 0 } infos ? infos : null);
                    foreach (var server in servers)
                        if (server.Error is { } error)
                            throw new IOException(string.Format(ResString.ffmpegConcatInputFailed, error.Message), error);
                    // mkvmerge 的 1 表示成功但有警告，2 才是错误。
                    if (exitCode != 0 && !(options.Muxer == "mkvmerge" && exitCode == 1))
                    {
                        errorMessage = string.Format(ResString.toolsProcessFailed, operation, exitCode);
                        Logger.Error(errorMessage);
                        return false;
                    }
                }
                token.ThrowIfCancellationRequested();
                processing.Begin(MediaProcessingStage.Finalizing);
                File.Move(temporary, output, options.Overwrite);
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
        return new(success, output, errorMessage);
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
