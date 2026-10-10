using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using System.Text;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using System.Globalization;
using System.Net.Sockets;
using Mp4SubtitleParser;

namespace N_m3u8DL_RE.Util;

internal static class MergeUtil
{
    /// <summary>
    /// 输入一堆已存在的文件，合并到新文件
    /// </summary>
    /// <param name="files"></param>
    /// <param name="outputFilePath"></param>
    public static void CombineMultipleFilesIntoSingleFile(string[] files, string outputFilePath, Action<MediaProgress>? progress = null)
    {
        CombineMultipleFilesIntoSingleFileAsync(files, outputFilePath, progress: progress).GetAwaiter().GetResult();
    }

    internal static async Task CombineMultipleFilesIntoSingleFileAsync(string[] files, string outputFilePath,
        bool overwrite = true, CancellationToken token = default, Action<MediaProgress>? progress = null)
    {
        if (files.Length == 0)
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFilePath))!);
        using var outputStream = new FileStream(outputFilePath, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        var total = progress == null ? 0 : files.Where(file => file != "").Sum(file => new FileInfo(file).Length);
        long written = 0;
        var buffer = progress == null ? null : new byte[128 * 1024];
        progress?.Invoke(new(total > 0 ? 0 : null, 0, total));
        foreach (var inputFilePath in files)
        {
            if (inputFilePath == "")
                continue;
            using var inputStream = File.OpenRead(inputFilePath);
            if (buffer == null)
            {
                await inputStream.CopyToAsync(outputStream, token).ConfigureAwait(false);
                continue;
            }
            int count;
            while ((count = await inputStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await outputStream.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                written += count;
                progress!(new(total > 0 ? (double)written / total : null, written, total));
            }
        }
    }

    private static int InvokeFFmpeg(string binary, string command, string workingDirectory, Action<MediaProgress>? progress = null, double? duration = null)
    {
        if (progress != null)
            command = "-progress pipe:1 -nostats " + command;
        var reader = progress == null ? null : new MediaToolProgress(progress, duration, preserveTimestamp: command.Contains(" -copyts ", StringComparison.Ordinal));
        Logger.Debug($"{binary}: {command}");
        return ProcessUtil.RunCommandAsync(binary, command, workingDirectory,
            line => Logger.WarnMarkUp($"[grey]{line.EscapeMarkup()}[/]"), outputLine: reader == null ? null : reader.ReadFFmpeg).GetAwaiter().GetResult().ExitCode;
    }

    /// <summary>
    /// 判断 ffmpeg 的输出是否为文件句柄耗尽（Too many open files）导致的错误。
    /// concat 协议会一次性打开全部分片，分片过多 + 系统句柄上限过低时会触发该错误。
    /// </summary>
    internal static bool IsTooManyOpenFilesError(string ffmpegOutput)
    {
        return !string.IsNullOrEmpty(ffmpegOutput)
            && ffmpegOutput.Contains("too many open files", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 分块合并的分片数量阈值。
    /// </summary>
    internal const int PartialMergeThreshold = 1800;

    /// <summary>
    /// 判断合并前是否需要先做分块合并(<see cref="PartialCombineMultipleFiles"/>)。
    /// 分块合并只为 concat 协议服务: 该模式会一次性打开全部分片(见 #338、#89), 且所有文件名都要放在命令行上。
    /// concat demuxer 通过临时清单文件逐个读取分片, 上述两个限制都不存在;
    /// 而分块合并会把上百个分片按字节直接拼进单个 TS 中间文件, ffmpeg 只能把它当作一条连续流读取,
    /// 无法处理文件内部的时间戳重置, 导致时间轴错乱、时长严重偏短(见 #946)。
    /// 本机虚拟输入也没有上述两个限制，只在用户显式选择直接 concat 协议时保留分块合并。
    /// </summary>
    internal static bool ShouldPartialMerge(int fileCount, FFmpegConcatMode mode)
    {
        return fileCount >= PartialMergeThreshold && mode == FFmpegConcatMode.PROTOCOL;
    }

    internal static string BuildPartsConcatList(string[] files, IReadOnlyList<MediaPart> parts)
    {
        if (files.Length != parts.Count)
            throw new ArgumentException(ResString.mediaPartInputMismatch);
        var text = new StringBuilder("ffconcat version 1.0\n");
        double elapsed = 0;
        for (var i = 0; i < files.Length; i++)
        {
            var path = Path.GetFullPath(files[i]);
            if (path.Contains('\n') || path.Contains('\r'))
                throw new ArgumentException(ResString.concatInputPathInvalid);
            text.Append("file '").Append(path.Replace("'", "'\\''")).Append("'\n");
            var part = parts[i];
            var start = part.OutputStart ?? elapsed;
            var duration = part.OutputDuration ?? part.MediaSegments.Sum(s => s.Duration);
            var nextStart = i + 1 < parts.Count ? parts[i + 1].OutputStart ?? start + duration : start + duration;
            // DASH 的 PTO 是源媒体的时间原点。inpoint/outpoint 保留音视频各自的
            // 起始偏移并裁掉跨越 Period 尾部的分片；duration 统一推进各轨道的时间轴。
            if (part.PeriodIndex != null)
            {
                var inpoint = part.OutputInpoint ?? Math.Max(part.PresentationTimeOffset ?? 0,
                    part.MediaSegments.FirstOrDefault()?.PresentationTime ?? 0);
                text.Append("inpoint ").Append(inpoint.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
                text.Append("outpoint ").Append((inpoint + duration).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            }
            text.Append("duration ").Append((nextStart - start).ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            elapsed = start + duration;
        }
        return text.ToString();
    }

    public static bool ConcatMediaParts(string binary, string[] files, IReadOnlyList<MediaPart> parts, string output, MediaProcessingProgress? progress = null)
    {
        // init+媒体先形成各自可读取的文件；concat demuxer 重新读取每份配置，
        // 避免直接按字节拼接多个 moov 和发生回退的 tfdt。
        var listPath = Path.GetTempFileName();
        var normalized = new List<string>();
        try
        {
            var inputs = files.ToArray();
            for (var i = 0; i < inputs.Length; i++)
            {
                if (Path.GetExtension(inputs[i]).ToLowerInvariant() is not (".mp4" or ".m4a" or ".m4s"))
                    continue;
                // concat demuxer 要求各输入轨道 time_base 一致。init 的 timescale 可以
                // 不同，FFmpeg 解密也会改写它，因此先无损 remux 到共同的视频 timescale。
                // copyts 保留 PTO 对应的源时间，不能在此把每份输入单独归零。
                var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[i]))!, $"{Guid.NewGuid():N}.normalized.mp4");
                normalized.Add(path);
                progress?.Begin($"{ResString.processingNormalize} ({i + 1}/{inputs.Length})");
                if (InvokeFFmpeg(binary,
                    $"-loglevel warning -nostdin -y -copyts -avoid_negative_ts disabled -i \"{Path.GetFullPath(inputs[i])}\" -map 0:v? -map 0:a? -map 0:s? -c copy -video_track_timescale 90000 \"{path}\"",
                    Path.GetDirectoryName(path)!, progress == null ? null : progress.Report) != 0)
                    return false;
                inputs[i] = path;
            }
            File.WriteAllText(listPath, BuildPartsConcatList(inputs, parts), new UTF8Encoding(false));
            var start = parts.FirstOrDefault()?.OutputStart ?? 0;
            var offset = start == 0 ? "" : $" -itsoffset {start.ToString("R", CultureInfo.InvariantCulture)}";
            var copyTs = parts.Any(p => p.OutputStart != null) ? " -copyts -avoid_negative_ts disabled" : "";
            // MPEG-TS 默认额外延迟输出时钟，字幕公共时间轴已经归零，必须保留该时间轴。
            var tsOptions = copyTs.Length > 0 && Path.GetExtension(output).Equals(".ts", StringComparison.OrdinalIgnoreCase)
                ? " -mpegts_copyts 1 -muxdelay 0" : "";
            progress?.Begin(ResString.processingMerge, logStart: false);
            return InvokeFFmpeg(binary,
                $"-loglevel warning -nostdin -y{copyTs}{offset} -f concat -safe 0 -i \"{listPath}\" -map 0:v? -map 0:a? -map 0:s? -c copy{tsOptions} \"{Path.GetFullPath(output)}\"",
                Path.GetDirectoryName(Path.GetFullPath(files[0]))!, progress == null ? null : progress.Report,
                parts.Any(p => p.OutputStart != null) ? null : parts.Sum(p => p.OutputDuration ?? p.MediaSegments.Sum(s => s.Duration))) == 0;
        }
        finally
        {
            File.Delete(listPath);
            foreach (var path in normalized) File.Delete(path);
        }
    }

    public static string[] PartialCombineMultipleFiles(string[] files, Action<MediaProgress>? progress = null)
    {
        var newFiles = new List<string>();
        var div = files.Length <= 90000 ? 100 : 200;

        var outputName = Path.Combine(Path.GetDirectoryName(files[0])!, "T");
        var index = 0; // 序号
        var total = progress == null ? 0 : files.Sum(file => new FileInfo(file).Length);
        long written = 0;

        // 按照div的容量分割为小数组
        var li = Enumerable.Range(0, files.Length / div + 1).Select(x => files.Skip(x * div).Take(div).ToArray()).ToArray();
        foreach (var items in li)
        {
            if (items.Length == 0)
                continue;
            var output = outputName + index.ToString("0000") + ".ts";
            CombineMultipleFilesIntoSingleFile(items, output, progress == null ? null : value =>
                progress(new(total > 0 ? (double)(written + value.Bytes!.Value) / total : null, written + value.Bytes, total)));
            if (progress != null)
                written += new FileInfo(output).Length;
            newFiles.Add(output);
            // 合并后删除这些文件
            foreach (var item in items)
            {
                File.Delete(item);
            }
            index++;
        }

        return newFiles.ToArray();
    }

    public static bool MergeByFFmpeg(string binary, string[] files, string outputPath, string muxFormat, bool useAACFilter,
        bool fastStart = false,
        bool writeDate = true, FFmpegConcatMode concatMode = FFmpegConcatMode.LOCAL_HTTP, string poster = "", string audioName = "", string title = "",
        string copyright = "", string comment = "", string encodingTool = "", string recTime = "", Action<MediaProgress>? progress = null, double? duration = null)
    {
        // 改为绝对路径
        outputPath = Path.GetFullPath(outputPath);
        var format = muxFormat.ToUpperInvariant();
        var ddpAudio = File.Exists($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt")
            ? File.ReadAllText($"{Path.GetFileNameWithoutExtension(outputPath + ".mp4")}.txt") : "";
        if (!string.IsNullOrEmpty(ddpAudio))
            useAACFilter = false;
        var options = new List<string>();
        if (format == "MP4")
        {
            if (!string.IsNullOrEmpty(poster))
                options.AddRange(["-i", Path.GetFullPath(poster)]);
            if (!string.IsNullOrEmpty(ddpAudio))
                options.AddRange(["-i", Path.GetFullPath(ddpAudio)]);
            options.AddRange(["-map", "0:v?"]);
            if (!string.IsNullOrEmpty(ddpAudio))
                options.AddRange(["-map", string.IsNullOrEmpty(poster) ? "1:a" : "2:a"]);
            options.AddRange(["-map", "0:a?", "-map", "0:s?"]);
            if (!string.IsNullOrEmpty(poster))
                options.AddRange(["-map", "1", "-c:v:1", "copy", "-disposition:v:1", "attached_pic"]);
            if (writeDate)
                options.AddRange(["-metadata", "date=" + (string.IsNullOrEmpty(recTime) ? DateTime.Now.ToString("o") : recTime)]);
            options.AddRange(["-metadata", "encoding_tool=" + encodingTool, "-metadata", "title=" + title,
                "-metadata", "copyright=" + copyright, "-metadata", "comment=" + comment]);
            var audioIndex = string.IsNullOrEmpty(ddpAudio) ? 0 : 1;
            options.AddRange([$"-metadata:s:a:{audioIndex}", "title=" + audioName,
                $"-metadata:s:a:{audioIndex}", "handler=" + audioName]);
            if (!string.IsNullOrEmpty(ddpAudio))
                options.AddRange(["-metadata:s:a:0", "title=DD+", "-metadata:s:a:0", "handler=DD+"]);
            if (fastStart)
                options.AddRange(["-movflags", "+faststart"]);
        }
        else
        {
            options.AddRange(["-map", format is "EAC3" or "AAC" or "AC3" ? "0:a" : "0"]);
        }
        options.AddRange(["-c", "copy"]);
        if (format == "M4A")
            options.AddRange(["-f", "mp4"]);
        if (format == "TS")
            options.AddRange(["-f", "mpegts", "-bsf:v", "h264_mp4toannexb"]);
        else if (useAACFilter && format is "MP4" or "MKV" or "FLV" or "M4A")
            options.AddRange(["-bsf:a", "aac_adtstoasc"]);
        var extension = format == "AAC" ? ".m4a" : "." + format.ToLowerInvariant();
        try
        {
            return MergeByFFmpegAsync(binary, files, outputPath + extension, concatMode, options,
                overwrite: true, log: true, progress: progress, duration: duration).GetAwaiter().GetResult() == 0;
        }
        catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException)
        {
            Logger.WarnMarkUp(string.Format(ResString.ffmpegConcatInputFailed, ex.Message).EscapeMarkup());
            return false;
        }
    }

    internal static async Task<int> MergeByFFmpegAsync(string binary, string[] files, string output,
        FFmpegConcatMode mode, IReadOnlyList<string>? outputOptions = null, bool overwrite = false,
        bool log = false, CancellationToken token = default, Action<MediaProgress>? progress = null, double? duration = null)
    {
        files = files.Select(Path.GetFullPath).ToArray();
        ConcatInputServer? server = null;
        string? list = null;
        try
        {
            List<string> arguments = ["-hide_banner", "-loglevel", "warning", "-nostdin", overwrite ? "-y" : "-n"];
            // 三种模式只改变输入构造，转封装参数保持一致。
            switch (mode)
            {
                case FFmpegConcatMode.LOCAL_HTTP:
                    // 仍交给 concat 协议读取连续字节，但只打开一个可 seek 的虚拟资源。
                    server = new ConcatInputServer(files);
                    arguments.AddRange(["-protocol_whitelist", "concat,http,tcp", "-i", "concat:" + server.Url]);
                    break;
                case FFmpegConcatMode.PROTOCOL:
                    if (files.Any(path => path.Contains('|')))
                        throw new ArgumentException(ResString.toolsProtocolPath);
                    arguments.AddRange(["-i", "concat:" + string.Join('|', files)]);
                    break;
                case FFmpegConcatMode.DEMUXER:
                    // 使用 concat demuxer 合并
                    if (files.Any(path => path.Contains('\r') || path.Contains('\n')))
                        throw new ArgumentException(ResString.concatInputPathInvalid);
                    list = Path.GetTempFileName();
                    await File.WriteAllLinesAsync(list, files.Select(path => $"file '{path.Replace("'", "'\\''")}'"), token).ConfigureAwait(false);
                    arguments.AddRange(["-f", "concat", "-safe", "0", "-i", list]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
            if (outputOptions != null)
                arguments.AddRange(outputOptions);
            else
            {
                arguments.AddRange(["-map", "0:v?", "-map", "0:a?", "-map", "0:s?", "-c", "copy"]);
                var format = Path.GetExtension(output).ToLowerInvariant();
                if (format == ".mp4")
                    arguments.AddRange(["-c:s", "mov_text"]);
                if (format == ".m4a")
                    arguments.AddRange(["-vn", "-sn"]);
            }
            arguments.Add(Path.GetFullPath(output));
            var result = await RunMediaToolAsync(binary, arguments, token, log, server != null, progress: progress, duration: duration).ConfigureAwait(false);
            if (server?.Error is { } error)
                throw new IOException(string.Format(ResString.ffmpegConcatInputFailed, error.Message), error);
            // 直接 concat 协议仍可能耗尽句柄；失败时不改变时间轴处理方式，也不删除分片。
            if (log && result.ExitCode != 0 && IsTooManyOpenFilesError(result.Error))
                Logger.WarnMarkUp(ResString.ffmpegMergeReachLimit);
            return result.ExitCode;
        }
        finally
        {
            server?.Dispose();
            if (list != null)
                File.Delete(list);
        }
    }

    public static bool MuxInputsByFFmpeg(string binary, OutputFile[] files, string outputPath, MuxFormat muxFormat, bool dateinfo)
    {
        return MuxInputsAsync(binary, files, outputPath + OtherUtil.GetMuxExtension(muxFormat),
            downloadDefaults: true, dateinfo: dateinfo).GetAwaiter().GetResult() == 0;
    }

    public static bool MuxInputsByMkvmerge(string binary, OutputFile[] files, string outputPath)
    {
        // mkvmerge 的 1 表示完成但有警告，2 才表示失败。
        return MuxInputsAsync(binary, files, outputPath + ".mkv", useMkvmerge: true,
            downloadDefaults: true).GetAwaiter().GetResult() is 0 or 1;
    }

    internal static async Task<int> MuxInputsAsync(string binary, OutputFile[] files, string output,
        bool useMkvmerge = false, bool downloadDefaults = false, bool dateinfo = false,
        MuxSubtitleTimeline? timeline = null, string? title = null, CancellationToken token = default,
        Action<MediaProgress>? progress = null)
    {
        // LANG and NAME
        // 转换语言代码
        foreach (var file in files)
            LanguageCodeUtil.ConvertLangCodeAndDisplayName(file);
        var inputs = timeline?.Inputs ?? files.Select(file => Path.GetFullPath(file.FilePath)).ToArray();
        var format = Path.GetExtension(output).ToLowerInvariant();
        List<string> arguments = useMkvmerge ? ["--output", Path.GetFullPath(output)]
            : ["-hide_banner", "-loglevel", "warning", "-nostdin", downloadDefaults ? "-y" : "-n"];
        if (useMkvmerge)
        {
            if (title != null)
                arguments.AddRange(["--title", title]);
            var audioSeen = false;
            for (var i = 0; i < files.Length; i++)
            {
                if (downloadDefaults)
                {
                    arguments.Add("--no-chapters");
                    // 字幕都不设置默认
                    // 音频除了第一个音轨 都不设置默认
                    if (files[i].MediaType == MediaType.SUBTITLES || files[i].MediaType == MediaType.AUDIO && audioSeen)
                        arguments.AddRange(["--default-track-flag", "-1:no"]);
                    if (files[i].MediaType == MediaType.AUDIO)
                        audioSeen = true;
                }
                if (files[i].LangCode != null || downloadDefaults)
                    arguments.AddRange(["--language", "-1:" + (files[i].LangCode ?? "und")]);
                if (files[i].Description != null)
                    arguments.AddRange(["--track-name", "-1:" + files[i].Description]);
                if (timeline?.Origin != null && timeline.MediaStarts[i] is { } start)
                {
                    var milliseconds = Math.Round((start - timeline.Origin.Value) * 1000);
                    arguments.AddRange(["--sync", "-1:" + milliseconds.ToString(CultureInfo.InvariantCulture)]);
                }
                arguments.Add(inputs[i]);
            }
        }
        else
        {
            if (timeline?.Origin != null || files.Any(file => file.PreserveTimestamp))
                arguments.Add("-copyts");
            // INPUT
            for (var i = 0; i < inputs.Length; i++)
            {
                // 媒体共用原点，修复后的独立字幕已经位于播放时间轴。
                if (timeline?.Origin != null && timeline.MediaStarts[i] != null)
                    arguments.AddRange(["-itsoffset", (-timeline.Origin.Value).ToString("0.######", CultureInfo.InvariantCulture)]);
                arguments.AddRange(["-i", inputs[i]]);
            }
            // CLEAN
            if (downloadDefaults)
                arguments.AddRange(["-map_metadata", "-1"]);
            // MAP
            // "-map {i}" pulls in every stream an input has, including ones the mux
            // format can't hold. Some sites' segments carry a data stream alongside
            // the real track (e.g. HLS timed_id3 metadata), and Matroska/MP4 reject
            // the whole mux for it ("Only audio, video, and subtitles are supported"),
            // even with -ignore_unknown set below.
            //
            // Map by stream type instead, not by whole input - and ask for all three
            // types from every input rather than switching on each file's declared
            // MediaType. A "video" input isn't always video-only: some sites hand
            // out one combined file per rendition (audio muxed into the same
            // stream), and restricting that input to just ":v?" would silently
            // drop its audio. With metadata, map each known track explicitly;
            // otherwise use optional ":v?/:a?/:s?" maps. M4A keeps only audio.
            var trackCounts = new Dictionary<string, int> { ["Video"] = 0, ["Audio"] = 0, ["Subtitle"] = 0 };
            if (downloadDefaults || files.Any(file => file.LangCode != null || file.Description != null))
            {
                // 用真实轨道数量定位元数据；复用下载时已读取的信息，MP4 直接读 box。
                /**
                 * -metadata:s:xx 标记的是输出的第 xx 个流的 metadata，
                 * 若输入文件存在不止一个流，直接使用 files 的 index 会导致 metadata 错位。
                 * 按实际映射的轨道推进 streamIndex，给该输入的每个输出轨道设置元数据。
                 */
                var streamIndex = 0;
                for (var i = 0; i < files.Length; i++)
                {
                    var types = await MediainfoUtil.ReadTrackTypesAsync(binary, files[i], token).ConfigureAwait(false);
                    foreach (var (type, specifier) in new[] { ("Video", "v"), ("Audio", "a"), ("Subtitle", "s") })
                    {
                        if (format == ".m4a" && type != "Audio")
                            continue;
                        var count = types.Count(t => t == type);
                        trackCounts[type] += count;
                        for (var track = 0; track < count; track++)
                        {
                            arguments.AddRange(["-map", $"{i}:{specifier}:{track}"]);
                            if (files[i].LangCode != null || downloadDefaults)
                                arguments.AddRange([$"-metadata:s:{streamIndex}", "language=" + (files[i].LangCode ?? "und")]);
                            if (files[i].Description != null)
                            {
                                arguments.AddRange([$"-metadata:s:{streamIndex}", "title=" + files[i].Description]);
                                if (format is ".mp4" or ".m4a")
                                    arguments.AddRange([$"-metadata:s:{streamIndex}", "handler_name=" + files[i].Description]);
                            }
                            streamIndex++;
                        }
                    }
                }
            }
            else
            {
                for (var i = 0; i < files.Length; i++)
                    arguments.AddRange(["-map", $"{i}:v?", "-map", $"{i}:a?", "-map", $"{i}:s?"]);
            }
            arguments.AddRange(["-c", "copy"]);
            if (timeline?.Origin != null)
                arguments.AddRange(["-avoid_negative_ts", "disabled"]);
            // MP4 不支持 VTT/SRT 字幕，必须转换格式
            if (format == ".mp4")
                arguments.AddRange(["-c:s", "mov_text"]);
            else if (downloadDefaults && format == ".mkv")
                arguments.AddRange(["-c:s", files.Any(file => Path.GetExtension(file.FilePath).Equals(".srt", StringComparison.OrdinalIgnoreCase)) ? "srt" : "webvtt"]);
            if (format == ".m4a")
                arguments.AddRange(["-vn", "-sn"]);
            if (downloadDefaults)
            {
                arguments.AddRange(["-strict", "unofficial", "-ignore_unknown", "-copy_unknown"]);
                if (trackCounts["Video"] > 0)
                    arguments.AddRange(["-disposition:v:0", "default"]);
                // 字幕都不设置默认
                if (trackCounts["Subtitle"] > 0)
                    arguments.AddRange(["-disposition:s", "0"]);
                // 音频除了第一个音轨 都不设置默认
                for (var i = 0; i < trackCounts["Audio"]; i++)
                    arguments.AddRange([$"-disposition:a:{i}", i == 0 ? "default" : "0"]);
            }
            if (dateinfo)
                arguments.AddRange(["-metadata", "date=" + DateTime.Now.ToString("o")]);
            if (title != null)
                arguments.AddRange(["-metadata", "title=" + title]);
            arguments.Add(Path.GetFullPath(output));
        }
        var duration = progress == null || useMkvmerge || files.Any(file => file.PreserveTimestamp) || timeline?.Origin != null
            ? null : GetDuration(files);
        var result = await RunMediaToolAsync(binary, arguments, token, downloadDefaults,
            printOutput: useMkvmerge && !downloadDefaults, progress: progress, useMkvmerge: useMkvmerge,
            duration: duration).ConfigureAwait(false);
        if (!useMkvmerge && format == ".mkv" && result.ExitCode != 0 &&
            result.Error.Contains("Can't write packet with unknown timestamp", StringComparison.OrdinalIgnoreCase))
        {
            // 部分 TS 含只有 PPS 等参数集、没有 PTS/DTS 的独立视频包，Matroska 拒绝写入。
            // 仅针对该错误重试一次：保留已有时间戳，缺失 PTS 优先沿用 DTS，否则沿用前包时间。
            // 不删除这些包或改写源文件，也不能重建正常画面的时间轴（含 B 帧及 copyts）。
            Logger.Warn(ResString.muxTimestampRetry);
            if (!downloadDefaults)
                arguments[arguments.IndexOf("-n")] = "-y"; // 只覆盖本次失败生成的半成品。
            arguments.InsertRange(arguments.Count - 1, ["-bsf:v",
                @"setts=pts=if(eq(PTS\,NOPTS)\,if(eq(DTS\,NOPTS)\,PREV_OUTPTS\,DTS)\,PTS):dts=if(eq(DTS\,NOPTS)\,PREV_OUTDTS\,DTS)"]);
            progress?.Invoke(new());
            result = await RunMediaToolAsync(binary, arguments, token, downloadDefaults,
                progress: progress, duration: duration).ConfigureAwait(false);
        }
        return result.ExitCode;
    }

    private static double? GetDuration(OutputFile[] files)
    {
        var durations = new List<double>();
        foreach (var file in files.Where(file => file.MediaType != MediaType.SUBTITLES))
        {
            var extension = Path.GetExtension(file.FilePath).ToLowerInvariant();
            if (extension is ".srt" or ".vtt" or ".ass" or ".ssa" or ".sup" or ".idx")
                continue;
            var duration = file.Duration;
            if (duration == null && extension is ".mp4" or ".m4a" or ".m4s")
            {
                try { duration = MP4MediaInfoUtil.ReadTiming(file.FilePath)?.Duration; }
                catch (Exception ex) when (ex is IOException or ArgumentException or OverflowException) { }
            }
            if (duration is not > 0)
                return null;
            durations.Add(duration.Value);
        }
        return durations.Count == 0 ? null : durations.Max();
    }

    private static async Task<ProcessUtil.Result> RunMediaToolAsync(string binary, List<string> arguments,
        CancellationToken token, bool log, bool loopbackInput = false, bool printOutput = false,
        Action<MediaProgress>? progress = null, bool useMkvmerge = false, double? duration = null)
    {
        // 每次调用使用独立参数，重试时不能重复加入 progress/gui-mode。
        arguments = [.. arguments];
        var reader = progress == null ? null : new MediaToolProgress(progress, duration, preserveTimestamp: arguments.Contains("-copyts"));
        if (reader != null)
            arguments.InsertRange(0, useMkvmerge ? ["--gui-mode"] : ["-progress", "pipe:1", "-nostats"]);
        if (log)
            Logger.Debug($"{binary}: {string.Join(" ", arguments.Select(argument => $"\"{argument}\""))}");
        Action<string> write = line => Logger.WarnMarkUp($"[grey]{line.EscapeMarkup()}[/]");
        return await ProcessUtil.RunAsync(binary, arguments, token, loopbackInput: loopbackInput,
            errorLine: write, outputLine: reader != null ? line =>
            {
                if (!useMkvmerge) reader.ReadFFmpeg(line);
                else if (!reader.ReadMkvmerge(line))
                {
                    if (line.StartsWith("#GUI#warning", StringComparison.Ordinal) || line.StartsWith("#GUI#error", StringComparison.Ordinal))
                        Logger.WarnMarkUp($"[grey]{line.EscapeMarkup()}[/]");
                    else if (printOutput)
                        Logger.InfoMarkUp($"[grey]{line.EscapeMarkup()}[/]");
                }
            } : printOutput ? line => Logger.InfoMarkUp($"[grey]{line.EscapeMarkup()}[/]") : null).ConfigureAwait(false);
    }
}
