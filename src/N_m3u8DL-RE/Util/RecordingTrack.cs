using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Util;

// 只识别 RE 保存的分片和初始化文件，避免把日志、临时下载及已有输出一起合并。
internal sealed partial class RecordingTrack
{
    internal required OutputFile Info { get; init; }
    internal required string[] Sources { get; init; }
    internal required string[] Inputs { get; init; }
    internal bool HasInitialization => InitRegex().IsMatch(Path.GetFileName(Inputs[0]));
    private bool Ttml { get; init; }
    private uint? VttTimescale { get; init; }

    [GeneratedRegex(@"^(?:\d+(?:_\d+)?|\d{10,}_.+)(?:_dec)?\.(?:m4s|mp4|ts|aac|m4a|vtt|ttml)$", RegexOptions.IgnoreCase)]
    private static partial Regex SegmentRegex();

    [GeneratedRegex(@"^_init(?:_\d+)?(?:_dec)?\.mp4$", RegexOptions.IgnoreCase)]
    private static partial Regex InitRegex();

    [GeneratedRegex(@"^\d+\.fix\.(?:srt|vtt)$", RegexOptions.IgnoreCase)]
    private static partial Regex FixedSubtitleRegex();

    [GeneratedRegex(@"(?m)^\d+:\d{2}:\d{2}[.,]\d{1,3}[ \t]+-->[^\r\n]*\r?\n[ \t]*[^ \t\r\n]")]
    private static partial Regex SrtPayloadRegex();

    private static bool HasInputs(string directory) => Directory.Exists(Path.Combine(directory, "parts")) ||
        Directory.EnumerateFiles(directory).Any(file => SegmentRegex().IsMatch(Path.GetFileName(file)) ||
            FixedSubtitleRegex().IsMatch(Path.GetFileName(file)));

    internal static OutputFile[] FindTracks(string directory)
    {
        directory = Path.GetFullPath(directory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"{ResString.toolsInputMissing}: {directory}");
        // 录制目录下只查找直接的轨道目录，不递归处理其他录像或任意子目录。
        var tracks = Directory.GetDirectories(directory)
            .Where(path => Path.GetFileName(path).Contains('_') && int.TryParse(Path.GetFileName(path).Split('_')[0], out _))
            .Where(HasInputs)
            .OrderBy(path => Path.GetFileName(path), Comparer<string>.Create(FileToolUtil.CompareNatural))
            .Select((path, index) => new OutputFile { Index = index, FilePath = path }).ToArray();
        if (tracks.Length > 0)
            return tracks;
        // 优先选择轨道子目录，不能把录像根目录内的旧输出当成单条轨道。
        if (HasInputs(directory))
            return [new OutputFile { Index = 0, FilePath = directory }];
        throw new ArgumentException($"{ResString.toolsRecordingEmpty}: {directory}");
    }

    internal static RecordingTrack Read(OutputFile file, CancellationToken token, bool binaryConcat = false)
    {
        var sourceDirectory = Path.GetFullPath(file.FilePath);
        var directory = sourceDirectory;
        var partsDirectory = Path.Combine(directory, "parts");
        if (Directory.Exists(partsDirectory))
        {
            var parts = Directory.GetDirectories(partsDirectory)
                .Where(path => Path.GetFileName(path).All(char.IsAsciiDigit)).ToArray();
            // 普通点播也可能使用单段 parts 目录；多段需要清单中的 PTO、裁剪及间隔信息。
            if (parts.Length != 1)
                throw new ArgumentException($"{ResString.toolsRecordingPartsUnsupported}: {sourceDirectory}");
            var tracks = FindTracks(parts[0]);
            if (tracks.Length != 1 || Directory.Exists(Path.Combine(tracks[0].FilePath, "parts")))
                throw new ArgumentException($"{ResString.toolsRecordingPartsUnsupported}: {sourceDirectory}");
            directory = tracks[0].FilePath;
        }
        var sources = Directory.GetFiles(directory);
        var fixedSubtitles = sources.Where(path => FixedSubtitleRegex().IsMatch(Path.GetFileName(path))).ToArray();
        if (fixedSubtitles.Length > 1)
            throw new ArgumentException($"{ResString.toolsRecordingSubtitleUnsupported}: {directory}");
        var segmentGroups = sources.Where(path => SegmentRegex().IsMatch(Path.GetFileName(path)))
            .GroupBy(path => Path.GetFileName(path).Replace("_dec.", ".", StringComparison.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase).ToArray();
        var segments = segmentGroups.Select(group => group.FirstOrDefault(path => IsDecrypted(path)) ?? group.First())
            .OrderBy(path => Path.GetFileName(path), Comparer<string>.Create(FileToolUtil.CompareNatural)).ToArray();
        // 点播可能只留下已经修复的字幕，不能跳过它，也不能重复追加原分片。
        if (fixedSubtitles.Length == 1)
            segments = fixedSubtitles;
        if (segments.Length == 0)
            throw new ArgumentException($"{ResString.toolsRecordingEmpty}: {directory}");
        var extensions = segments.Select(path => Path.GetExtension(path).ToLowerInvariant()).Distinct().ToArray();
        if (extensions.Length != 1)
            throw new ArgumentException($"{ResString.toolsRecordingMixed}: {directory}");
        var initFiles = fixedSubtitles.Length == 0
            ? sources.Where(path => InitRegex().IsMatch(Path.GetFileName(path))).ToArray() : [];
        if (initFiles.Any(path => !Path.GetFileName(path).Equals("_init.mp4", StringComparison.OrdinalIgnoreCase) &&
                                  !Path.GetFileName(path).Equals("_init_dec.mp4", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"{ResString.toolsRecordingMultiInit}: {directory}");
        if (binaryConcat && fixedSubtitles.Length == 0)
        {
            // 二进制合并允许保留加密内容，但必须选用完整的一套分片及对应 init，不能混用原始和解密版本。
            var decrypted = segmentGroups.All(group => group.Any(IsDecrypted)) &&
                (extensions[0] != ".m4s" || initFiles.Any(path => Path.GetFileName(path).Equals("_init_dec.mp4", StringComparison.OrdinalIgnoreCase)));
            if (!decrypted && !segmentGroups.All(group => group.Any(path => !IsDecrypted(path))))
                throw new ArgumentException($"{ResString.toolsRecordingIncomplete}: {directory}");
            segments = segmentGroups.Select(group => group.First(path => IsDecrypted(path) == decrypted))
                .OrderBy(path => Path.GetFileName(path), Comparer<string>.Create(FileToolUtil.CompareNatural)).ToArray();
        }
        string? init = null;
        if (extensions[0] == ".m4s")
        {
            var decrypted = segments.Any(IsDecrypted);
            if (decrypted && !segments.All(IsDecrypted))
                throw new ArgumentException($"{ResString.toolsRecordingEncrypted}: {directory}");
            var initName = decrypted ? "_init_dec.mp4" : "_init.mp4";
            init = initFiles.FirstOrDefault(path => Path.GetFileName(path).Equals(initName, StringComparison.OrdinalIgnoreCase));
            if (init == null)
                throw new ArgumentException($"{ResString.toolsRecordingInitMissing}: {Path.Combine(directory, initName)}");
        }
        var outputFile = new OutputFile
        {
            Index = file.Index, FilePath = sourceDirectory, LangCode = file.LangCode, Description = file.Description,
            MediaType = file.MediaType, Duration = file.Duration, PreserveTimestamp = file.PreserveTimestamp
        };
        var ttml = extensions[0] == ".ttml";
        uint? vttTimescale = null;
        // concat 只拼接字节，无需解析媒体信息或验证内容是否已解密。
        if (init != null && !binaryConcat)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var data = File.ReadAllBytes(init);
                if (MP4InitUtil.ReadInit(data).Scheme != null)
                    throw new ArgumentException($"{ResString.toolsRecordingEncrypted}: {directory}");
                var tracks = MP4MediaInfoUtil.ReadTrackInfo(init, token);
                if (tracks?.Length != 1)
                    throw new ArgumentException($"{ResString.toolsTrackInfoFailed}: {init}");
                outputFile.MediaType = tracks[0].Type switch
                {
                    "Video" => MediaType.VIDEO,
                    "Audio" => MediaType.AUDIO,
                    _ => MediaType.SUBTITLES
                };
                outputFile.LangCode ??= tracks[0].Language;
                outputFile.Mediainfos = [new Mediainfo
                {
                    Type = tracks[0].Type,
                    BaseInfo = tracks[0].DolbyVision ? "hevc" : tracks[0].CodecTag,
                    DolbyVison = tracks[0].DolbyVision,
                    CodecTag = tracks[0].CodecTag,
                    DolbyVisionProfile = tracks[0].DolbyVisionProfile
                }];
                ttml = MP4TtmlUtil.CheckInit(data);
                var (vtt, timescale) = MP4VttUtil.CheckInit(data);
                if (vtt)
                    vttTimescale = timescale;
                if (outputFile.MediaType == MediaType.SUBTITLES && !ttml && !vtt)
                    throw new ArgumentException($"{ResString.toolsRecordingSubtitleUnsupported}: {directory}");
            }
            catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException))
            {
                throw new InvalidDataException($"{ResString.toolsTrackInfoFailed}: {init}", ex);
            }
        }
        else if (extensions[0] is ".srt" or ".vtt" or ".ttml")
        {
            outputFile.MediaType = MediaType.SUBTITLES;
        }
        if (file.LangCode == null)
        {
            // 已转换的点播字幕不再带 mdhd，使用 RE 轨道目录末尾的语言字段兜底。
            var name = Path.GetFileName(directory).Split('_');
            if (name.Length > 1 && name[^1].Length > 0 && int.TryParse(name[0], out _))
                outputFile.LangCode ??= name[^1];
            LanguageCodeUtil.ConvertLangCodeAndDisplayName(outputFile);
            // 某些源将轨道 ID 写进 mdhd，不能将未知代码自动用作轨道标题。
            if (outputFile.LangCode == "und")
            {
                outputFile.LangCode = null;
                outputFile.Description = file.Description;
            }
        }
        return new RecordingTrack
        {
            Info = outputFile, Sources = directory == sourceDirectory ? sources
                : Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories),
            Inputs = init == null ? segments : [init, .. segments],
            Ttml = ttml, VttTimescale = vttTimescale
        };
    }

    private static bool IsDecrypted(string path) => Path.GetFileNameWithoutExtension(path).EndsWith("_dec", StringComparison.OrdinalIgnoreCase);

    internal async Task<OutputFile?> PrepareAsync(string directory, int index, CancellationToken token, Action<MediaProgress> progress, bool virtualInput = false)
    {
        var extension = Path.GetExtension(Inputs[^1]).ToLowerInvariant();
        if (Info.MediaType == MediaType.SUBTITLES)
            extension = ".srt";
        else if (extension is ".m4s" or ".m4a")
            extension = ".mp4";
        var path = Path.Combine(directory, $"{index}{extension}");
        string[]? segmentsInput = null;
        (string Header, double Local, double Mpegts)? timestampMap = null;
        if (FixedSubtitleRegex().IsMatch(Path.GetFileName(Inputs[0])) &&
            Path.GetExtension(Inputs[0]).Equals(".srt", StringComparison.OrdinalIgnoreCase))
        {
            // 已修复的 SRT 只检查时间行后是否有对白，混流仍读取原文件以保留正文和样式。
            // VTT 仍转换成兼容两种混流工具的 SRT。
            var text = await File.ReadAllTextAsync(Inputs[0], token);
            if (!SrtPayloadRegex().IsMatch(text))
            {
                Logger.Warn($"{ResString.toolsRecordingSubtitleEmpty}: {Info.FilePath}");
                return null;
            }
            path = Inputs[0];
        }
        else if (Info.MediaType == MediaType.SUBTITLES)
        {
            var subtitle = new WebVttSub();
            var segments = Inputs.Where(input => !InitRegex().IsMatch(Path.GetFileName(input))).ToArray();
            for (var i = 0; i < segments.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    WebVttSub part;
                    if (VttTimescale is { } timescale)
                        part = MP4VttUtil.ExtractSub([segments[i]], timescale);
                    else if (Ttml)
                        part = Path.GetExtension(segments[i]).Equals(".m4s", StringComparison.OrdinalIgnoreCase)
                            ? MP4TtmlUtil.ExtractFromMp4(segments[i], 0) : MP4TtmlUtil.ExtractFromTTML(segments[i], 0);
                    else
                    {
                        var text = await File.ReadAllTextAsync(segments[i], token);
                        part = WebVttSub.Parse(text);
                        var map = HlsSubtitleTimeline.ReadTimestampMap(text);
                        if (i == 0)
                            timestampMap = map;
                        else if (timestampMap.HasValue != map.HasValue)
                            throw new FormatException(ResString.hlsTimestampMapInvalid);
                        if (timestampMap is { } reference)
                        {
                            // 将各分片映射到首片的 LOCAL 时钟，保留首片映射供混流时对齐媒体。
                            // 上一条 cue 仅用于展开 MPEGTS 回绕，不能用于累加字幕偏移。
                            var position = subtitle.Cues.LastOrDefault()?.EndTime.TotalSeconds ?? reference.Local;
                            HlsSubtitleTimeline.Normalize(part, text, reference.Mpegts - reference.Local, position);
                            if (part.Cues.Any(cue => cue.StartTime < TimeSpan.Zero))
                                throw new FormatException(ResString.hlsTimestampMapInvalid);
                        }
                    }
                    subtitle.AddCuesFromOne(part);
                }
                catch (Exception ex) when (ex is not (IOException or UnauthorizedAccessException or ArgumentException or OperationCanceledException))
                {
                    throw new InvalidDataException($"{ResString.toolsRecordingSubtitleUnsupported}: {segments[i]}", ex);
                }
                progress(new(Fraction: (i + 1.0) / segments.Length));
            }
            if (subtitle.Cues.Count == 0)
            {
                Logger.Warn($"{ResString.toolsRecordingSubtitleEmpty}: {Info.FilePath}");
                return null;
            }
            if (subtitle.Cues.Any(cue => cue.Payload.StartsWith("Base64::", StringComparison.Ordinal)))
                throw new ArgumentException($"{ResString.toolsRecordingSubtitleUnsupported}: {Info.FilePath}");
            await File.WriteAllTextAsync(path, subtitle.ToSrt(), token);
        }
        else if (virtualInput)
        {
            path = Inputs[0];
            segmentsInput = Inputs;
        }
        else
        {
            await MergeUtil.CombineMultipleFilesIntoSingleFileAsync(Inputs, path, overwrite: false, token: token, progress: progress);
        }
        return new OutputFile
        {
            Index = Info.Index, FilePath = path, Segments = segmentsInput, SubtitleTimestampMap = timestampMap?.Header,
            MediaType = Info.MediaType, LangCode = Info.LangCode,
            Description = Info.Description, Duration = Info.Duration, PreserveTimestamp = Info.PreserveTimestamp,
            Mediainfos = Info.MediaType == MediaType.SUBTITLES ? [] : Info.Mediainfos
        };
    }
}
