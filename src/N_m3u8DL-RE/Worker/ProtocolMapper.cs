using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using System.CommandLine;
using System.Globalization;
using SystemEnum = System.Enum;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;

namespace N_m3u8DL_RE.Worker;

internal static class ProtocolMapper
{
    internal static MyOption ToOptions(TaskRequest request)
    {
        if (request.ThreadCount is <= 0)
            throw new ArgumentException("threadCount must be positive.");
        if (request.DownloadRetryCount is < 0)
            throw new ArgumentException("downloadRetryCount must not be negative.");
        if (request.HttpRequestTimeout is { } timeout && (!double.IsFinite(timeout) || timeout <= 0 || timeout > int.MaxValue / 1000d))
            throw new ArgumentException("httpRequestTimeout must be a finite positive duration.");
        if (request.MaxSpeed is <= 0)
            throw new ArgumentException("maxSpeed must be positive.");
        if (request.LiveWaitTime is <= 0 || request.LiveIdleTimeout is <= 0 || request.LiveTakeCount is <= 0)
            throw new ArgumentException("Live intervals and counts must be positive.");
        if (request.Keys?.Any(string.IsNullOrWhiteSpace) == true || request.AdKeywords?.Any(string.IsNullOrWhiteSpace) == true ||
            request.MuxImports?.Any(string.IsNullOrWhiteSpace) == true)
            throw new ArgumentException("Keys, adKeywords, and muxImports must contain nonempty strings.");
        var arguments = new List<string>();
        // 参数名直接来自 CLI 选项定义，复杂值继续交给同一解析器处理。
        void Add(Option option, object? value)
        {
            if (value == null)
                return;
            var text = value is bool boolean ? boolean ? "true" : "false" :
                Convert.ToString(value, CultureInfo.InvariantCulture)!;
            // 使用等号保留以连字符开头的字面值，避免被识别成其他选项。
            arguments.Add(option.Name + "=" + text);
        }
        Add(CommandInvoker.SaveDir, request.SaveDir);
        Add(CommandInvoker.TmpDir, request.TmpDir);
        Add(CommandInvoker.SaveName, request.SaveName);
        Add(CommandInvoker.SavePattern, request.SavePattern);
        Add(CommandInvoker.UrlProcessorArgs, request.UrlProcessorArgs);
        Add(CommandInvoker.Cookies, request.Cookies);
        Add(CommandInvoker.AutoSelect, request.AutoSelect);
        Add(CommandInvoker.SubOnly, request.SubOnly);
        Add(CommandInvoker.SubtitleFormat, request.SubFormat);
        Add(CommandInvoker.VideoFilter, request.SelectVideo);
        Add(CommandInvoker.AudioFilter, request.SelectAudio);
        Add(CommandInvoker.SubtitleFilter, request.SelectSubtitle);
        Add(CommandInvoker.DropVideoFilter, request.DropVideo);
        Add(CommandInvoker.DropAudioFilter, request.DropAudio);
        Add(CommandInvoker.DropSubtitleFilter, request.DropSubtitle);
        Add(CommandInvoker.VodSelectParts, request.VodSelectParts);
        Add(CommandInvoker.VodListParts, request.VodListParts);
        Add(CommandInvoker.VodDropParts, request.VodDropParts);
        Add(CommandInvoker.ThreadCount, request.ThreadCount);
        Add(CommandInvoker.DownloadRetryCount, request.DownloadRetryCount);
        Add(CommandInvoker.HttpRequestTimeout, request.HttpRequestTimeout);
        Add(CommandInvoker.HttpVersion, request.HttpVersion);
        Add(CommandInvoker.ConcurrentDownload, request.ConcurrentDownload);
        Add(CommandInvoker.SkipMerge, request.SkipMerge);
        Add(CommandInvoker.SkipDownload, request.SkipDownload);
        Add(CommandInvoker.BinaryMerge, request.BinaryMerge);
        Add(CommandInvoker.FFmpegConcatMode, request.FfmpegConcatMode);
        Add(CommandInvoker.AutoSubtitleFix, request.AutoSubtitleFix);
        Add(CommandInvoker.CheckSegmentsCount, request.CheckSegmentsCount);
        Add(CommandInvoker.NoDateInfo, request.NoDateInfo);
        Add(CommandInvoker.WriteMetaJson, request.WriteMetaJson);
        Add(CommandInvoker.DelAfterDone, request.DelAfterDone);
        Add(CommandInvoker.FFmpegBinaryPath, request.FfmpegBinaryPath);
        Add(CommandInvoker.DecryptionBinaryPath, request.DecryptionBinaryPath);
        Add(CommandInvoker.DecryptionEngine, request.DecryptionEngine);
        Add(CommandInvoker.MP4RealTimeDecryption, request.Mp4RealTimeDecryption);
        Add(CommandInvoker.CustomHLSMethod, request.CustomHlsMethod);
        Add(CommandInvoker.CustomHLSKey, request.CustomHlsKey);
        Add(CommandInvoker.CustomHLSIv, request.CustomHlsIv);
        Add(CommandInvoker.CustomHLSScope, request.CustomHlsScope);
        Add(CommandInvoker.AllowHlsMultiExtMap, request.AllowHlsMultiExtMap);
        Add(CommandInvoker.KeyTextFile, request.KeyTextFile);
        Add(CommandInvoker.MuxAfterDone, request.MuxAfterDone);
        Add(CommandInvoker.CustomProxy, request.CustomProxy);
        Add(CommandInvoker.UseSystemProxy, request.UseSystemProxy);
        Add(CommandInvoker.NetworkInterface, request.NetworkInterface);
        Add(CommandInvoker.BaseUrl, request.BaseUrl);
        Add(CommandInvoker.AppendUrlParams, request.AppendUrlParams);
        Add(CommandInvoker.CustomRange, request.CustomRange);
        Add(CommandInvoker.LivePerformAsVod, request.LivePerformAsVod);
        Add(CommandInvoker.LiveRealTimeMerge, request.LiveRealTimeMerge);
        Add(CommandInvoker.LivePipeMux, request.LivePipeMux);
        Add(CommandInvoker.LiveFixVttByAudio, request.LiveFixVttByAudio);
        Add(CommandInvoker.LiveKeepSegments, request.LiveKeepSegments);
        Add(CommandInvoker.LiveRecordLimit, request.LiveRecordLimit);
        Add(CommandInvoker.LiveWaitTime, request.LiveWaitTime);
        Add(CommandInvoker.LiveIdleTimeout, request.LiveIdleTimeout);
        Add(CommandInvoker.LiveTakeCount, request.LiveTakeCount);
        Add(CommandInvoker.LiveCatchupOption, request.LiveCatchup);
        Add(CommandInvoker.TaskStartAt, request.TaskStartAt);
        Add(CommandInvoker.LogFilePath, request.LogFilePath);
        Add(CommandInvoker.LogLevel, request.LogLevel);
        Add(CommandInvoker.NoLog, request.NoLog);
        foreach (var (name, value) in request.Headers ?? [])
            Add(CommandInvoker.Headers, $"{name}: {value}");
        foreach (var key in request.Keys ?? [])
            Add(CommandInvoker.Keys, key);
        foreach (var import in request.MuxImports ?? [])
            Add(CommandInvoker.MuxImports, import);
        foreach (var keyword in request.AdKeywords ?? [])
            Add(CommandInvoker.AdKeywords, keyword);
        var options = OptionNormalizer.Parse(request.Input, arguments);
        options.MaxSpeed = request.MaxSpeed;
        options.DisableUpdateCheck = true;
        options.NoAnsiColor = true;
        options.ForceAnsiConsole = false;
        return options;
    }

    internal static FileTaskOptions ToFileOptions(string operation, FileTaskRequest request)
    {
        if (request.Inputs is not { Length: > 0 } inputs || inputs.Any(input => input == null || string.IsNullOrWhiteSpace(input.Path)))
            throw new ArgumentException("inputs must contain one or more nonempty paths.");
        if (string.IsNullOrWhiteSpace(request.Output))
            throw new ArgumentException("output is required.");
        if (operation is not ("concat" or "merge" or "mux"))
            throw new ArgumentException("Unsupported file operation.");
        if (operation != "mux" && (request.Muxer != null || request.MkvmergeBinaryPath != null ||
            request.AutoSubtitleFix != null || request.Title != null || inputs.Any(input => input.Language != null || input.Name != null)))
            throw new ArgumentException("Muxer, subtitle options, title, and input metadata are only available for mux.");
        if (operation == "concat" && request.FfmpegBinaryPath != null)
            throw new ArgumentException("concat does not use FFmpeg.");
        if (operation != "merge" && request.ConcatMode != null)
            throw new ArgumentException("concatMode is only available for merge.");
        var muxer = request.Muxer ?? FileTaskOptions.DefaultMuxer;
        if (muxer is not ("ffmpeg" or "mkvmerge"))
            throw new ArgumentException("muxer must be ffmpeg or mkvmerge.");
        FFmpegConcatMode? mode = null;
        if (request.ConcatMode != null)
        {
            if (!SystemEnum.TryParse<FFmpegConcatMode>(request.ConcatMode, ignoreCase: true, out var parsed) || !SystemEnum.IsDefined(parsed))
                throw new ArgumentException("concatMode must be LOCAL_HTTP, PROTOCOL, or DEMUXER.");
            mode = parsed;
        }
        return new FileTaskOptions
        {
            Operation = operation, Output = request.Output,
            Inputs = inputs.Select((input, index) => new OutputFile
            {
                Index = index, FilePath = input.Path, LangCode = input.Language, Description = input.Name
            }).ToArray(),
            Pattern = request.Pattern, Overwrite = request.Overwrite, DryRun = request.DryRun,
            FFmpegBinaryPath = request.FfmpegBinaryPath, MkvmergeBinaryPath = request.MkvmergeBinaryPath,
            Muxer = muxer, ConcatMode = mode, AutoSubtitleFix = request.AutoSubtitleFix ?? FileTaskOptions.DefaultAutoSubtitleFix,
            Title = request.Title
        };
    }

    internal static TaskResult ToResult(ExecutionResult result) => new(result.Status,
        result.Outputs.Select(path => new OutputArtifact(path, Size(path))).ToArray(),
        result.ErrorCode == null ? null : new TaskError(result.ErrorCode, result.ErrorMessage ?? result.ErrorCode));

    private static long? Size(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
