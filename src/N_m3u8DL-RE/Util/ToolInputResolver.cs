using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Util;

internal static class ToolInputResolver
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ts", ".m2ts", ".mts", ".mp4", ".m4a", ".mkv", ".mov", ".webm", ".avi",
        ".aac", ".mp3", ".flac", ".wav", ".ogg", ".opus", ".ac3", ".eac3", ".h264", ".h265", ".hevc"
    };

    private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".vtt", ".ass", ".ssa", ".sup"
    };

    internal static OutputFile[] Expand(string operation, OutputFile[] inputs, string? pattern, CancellationToken token)
    {
        if (inputs.Length == 0)
            throw new ArgumentException(ResString.toolsInputRequired);
        if (pattern != null && (string.IsNullOrWhiteSpace(pattern) || pattern.IndexOfAny(['/', '\\']) >= 0))
            throw new ArgumentException(ResString.toolsPatternInvalid);
        if (pattern != null && !inputs.Any(file => Directory.Exists(file.FilePath)))
            throw new ArgumentException(ResString.toolsPatternRequiresDirectory);

        var expanded = new List<OutputFile>();
        foreach (var input in inputs)
        {
            token.ThrowIfCancellationRequested();
            input.FilePath = Path.GetFullPath(input.FilePath);
            if (!Directory.Exists(input.FilePath))
            {
                expanded.Add(input);
                continue;
            }

            // 显式通配符只做文件筛选；自动模式优先保留 RE 轨道的 init、解密及输出保护规则。
            if (pattern == null)
            {
                var tracks = RecordingTrack.FindTracks(input.FilePath, allowMediaDirectory: true);
                if (tracks.Length > 0)
                {
                    if (operation != "mux" && tracks.Length > 1)
                        throw new ArgumentException($"{ResString.toolsMultipleTracks}: {input.FilePath}");
                    Logger.Info(string.Format(ResString.toolsDirectoryRecording, input.FilePath, tracks.Length));
                    expanded.AddRange(tracks.Select(track => Copy(input, track.FilePath)));
                    continue;
                }
            }

            var matches = Directory.GetFiles(input.FilePath, pattern ?? "*")
                .Where(path => pattern != null || IsMediaFile(path, operation == "mux"))
                .OrderBy(path => Path.GetFileName(path), Comparer<string>.Create(FileToolUtil.CompareNatural)).ToArray();
            if (matches.Length == 0)
                throw new ArgumentException(string.Format(ResString.toolsDirectoryEmpty, input.FilePath,
                    pattern ?? ResString.toolsAutomaticPattern));
            Logger.Info(string.Format(ResString.toolsDirectoryFiles, input.FilePath, matches.Length));
            expanded.AddRange(matches.Select(path => Copy(input, path)));
        }
        return expanded.ToArray();
    }

    private static bool IsMediaFile(string path, bool subtitles)
    {
        var name = Path.GetFileName(path);
        // 初始化文件和处理中间产物不能作为独立影片读入普通媒体目录。
        if (RecordingTrack.IsInitializationFile(path) ||
            name.StartsWith(".re-", StringComparison.OrdinalIgnoreCase))
            return false;
        var extension = Path.GetExtension(path);
        return MediaExtensions.Contains(extension) || subtitles && SubtitleExtensions.Contains(extension);
    }

    private static OutputFile Copy(OutputFile source, string path) => new()
    {
        Index = source.Index, FilePath = path, LangCode = source.LangCode, Description = source.Description
    };

    internal static FFmpegConcatMode SelectMergeMode(string[] inputs, RecordingTrack?[] tracks)
    {
        // 完整容器逐个解封装；带 init 的轨道仍作为连续分片读取，避免拆开 moov 与 moof。
        for (var i = 0; i < inputs.Length; i++)
        {
            if (tracks[i] is { HasInitialization: true })
                continue;
            var paths = tracks[i]?.Inputs ?? [inputs[i]];
            if (paths.Any(path => Path.GetExtension(path).ToLowerInvariant() is
                ".mp4" or ".m4a" or ".mkv" or ".mov" or ".webm" or ".avi" or ".wav" or ".flac" or ".ogg" or ".opus"))
                return FFmpegConcatMode.DEMUXER;
        }
        return FFmpegConcatMode.LOCAL_HTTP;
    }
}
