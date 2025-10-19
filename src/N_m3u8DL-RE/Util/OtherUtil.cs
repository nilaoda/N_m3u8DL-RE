using N_m3u8DL_RE.Enum;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Util;

internal static partial class OtherUtil
{
    public static Dictionary<string, string> SplitHeaderArrayToDic(string[]? headers)
    {
        Dictionary<string, string> dic = new();
        if (headers == null) return dic;
        
        foreach (string header in headers)
        {
            var index = header.IndexOf(':');
            if (index != -1)
            {
                dic[header[..index].Trim().ToLower()] = header[(index + 1)..].Trim();
            }
        }

        return dic;
    }

    private static readonly char[] InvalidChars = "34,60,62,124,0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31,58,42,63,92,47"
        .Split(',').Select(s => (char)int.Parse(s)).ToArray();
    public static string GetValidFileName(string input, string re = "_", bool filterSlash = false)
    {
        var title = InvalidChars.Aggregate(input, (current, invalidChar) => current.Replace(invalidChar.ToString(), re));
        if (filterSlash)
        {
            title = title.Replace("/", re);
            title = title.Replace("\\", re);
        }
        return title.Trim('.');
    }

    /// <summary>
    /// 从输入自动获取文件名
    /// </summary>
    /// <param name="input"></param>
    /// <param name="addSuffix"></param>
    /// <returns></returns>
    public static string GetFileNameFromInput(string input, bool addSuffix = true)
    {
        var saveName = addSuffix ? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") : string.Empty;
        if (File.Exists(input))
        {
            saveName = Path.GetFileNameWithoutExtension(input) + "_" + saveName;
        }
        else
        {
            var uri = new Uri(input.Split('?').First());
            var name = Path.GetFileNameWithoutExtension(uri.LocalPath);
            saveName = GetValidFileName(name) + "_" + saveName;
        }
        return saveName;
    }

    /// <summary>
    /// 从 hh:mm:ss 解析TimeSpan
    /// </summary>
    /// <param name="timeStr"></param>
    /// <returns></returns>
    public static TimeSpan ParseDur(string timeStr)
    {
        var arr = timeStr.Replace("：", ":").Split(':');
        var days = -1;
        var hours = -1;
        var mins = -1;
        var secs = -1;
        arr.Reverse().Select(i => Convert.ToInt32(i)).ToList().ForEach(item =>
        {
            if (secs == -1) secs = item;
            else if (mins == -1) mins = item;
            else if (hours == -1) hours = item;
            else if (days == -1) days = item;
        });

        if (days == -1) days = 0;
        if (hours == -1) hours = 0;
        if (mins == -1) mins = 0;
        if (secs == -1) secs = 0;

        return new TimeSpan(days, hours, mins, secs);
    }

    /// <summary>
    /// 从1h3m20s解析出总秒数
    /// </summary>
    /// <param name="timeStr"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static double ParseSeconds(string timeStr)
    {
        var pattern = TimeStrRegex();

        var match = pattern.Match(timeStr);

        if (!match.Success)
        {
            throw new ArgumentException("时间格式无效");
        }

        int hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        int minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        int seconds = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;

        return hours * 3600 + minutes * 60 + seconds;
    }

    // 若该文件夹为空，删除，同时判断其父文件夹，直到遇到根目录或不为空的目录
    public static void SafeDeleteDir(string dirPath)
    {
        if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            return;

        var parent = Path.GetDirectoryName(dirPath)!;
        if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
        {
            Directory.Delete(dirPath);
        }
        else
        {
            return;
        }
        SafeDeleteDir(parent);
    }

    /// <summary>
    /// 解压并替换原文件
    /// </summary>
    /// <param name="filePath"></param>
    public static async Task DeGzipFileAsync(string filePath)
    {
        var deGzipFile = Path.ChangeExtension(filePath, ".dezip_tmp");
        try
        {
            await using (var fileToDecompressAsStream = File.OpenRead(filePath))
            {
                await using var decompressedStream = File.Create(deGzipFile);
                await using var decompressionStream = new GZipStream(fileToDecompressAsStream, CompressionMode.Decompress);
                await decompressionStream.CopyToAsync(decompressedStream);
            };
            File.Delete(filePath);
            File.Move(deGzipFile, filePath);
        }
        catch 
        {
            if (File.Exists(deGzipFile)) File.Delete(deGzipFile);
        }
    }

    public static string GetEnvironmentVariable(string key, string defaultValue = "")
    {
        return Environment.GetEnvironmentVariable(key) ?? defaultValue;
    }

    public static string GetMuxExtension(MuxFormat muxFormat)
    {
        return muxFormat switch
        {
            MuxFormat.MP4 => ".mp4",
            MuxFormat.MKV => ".mkv",
            MuxFormat.TS => ".ts",
            _ => throw new ArgumentException($"unknown format: {muxFormat}")
        };
    }

    [GeneratedRegex(@"^(?:(\d+)h)?(?:(\d+)m)?(?:(\d+)s)?$")]
    private static partial Regex TimeStrRegex();

    /// <summary>
    /// 格式化保存模板
    /// </summary>
    /// <param name="savePattern">模板字符串</param>
    /// <param name="streamSpec">流规格</param>
    /// <param name="saveName">保存名称</param>
    /// <param name="taskId">任务ID</param>
    /// <returns>格式化后的文件名(不含扩展名)</returns>
    public static string FormatSavePattern(string savePattern, Common.Entity.StreamSpec streamSpec, string? saveName, int taskId)
    {
        var result = savePattern;

        // 替换基本变量
        result = result.Replace("<SaveName>", saveName ?? "");
        result = result.Replace("<Id>", taskId.ToString());
        result = result.Replace("<Codecs>", streamSpec.Codecs ?? "");
        result = result.Replace("<Language>", streamSpec.Language ?? "");
        result = result.Replace("<Bandwidth>", streamSpec.Bandwidth?.ToString() ?? "");
        result = result.Replace("<Resolution>", streamSpec.Resolution ?? "");
        result = result.Replace("<FrameRate>", streamSpec.FrameRate?.ToString() ?? "");
        result = result.Replace("<Channels>", streamSpec.Channels ?? "");
        result = result.Replace("<VideoRange>", streamSpec.VideoRange ?? "");
        result = result.Replace("<MediaType>", streamSpec.MediaType?.ToString() ?? "");
        result = result.Replace("<GroupId>", streamSpec.GroupId ?? "");

        // 清理多余的分隔符
        result = result.Replace("__", "_").Replace("..", ".").Trim('_').Trim('.');

        // 清理文件名中的非法字符
        return GetValidFileName(result);
    }

    /// <summary>
    /// 处理文件名冲突，使用流元数据生成唯一文件名
    /// </summary>
    /// <param name="originalPath">原始文件路径</param>
    /// <param name="streamSpec">流规格（用于获取元数据）</param>
    /// <returns>不冲突的文件路径</returns>
    public static string HandleFileCollision(string originalPath, Common.Entity.StreamSpec streamSpec)
    {
        if (!File.Exists(originalPath))
            return originalPath;

        var dir = Path.GetDirectoryName(originalPath) ?? "";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
        var ext = Path.GetExtension(originalPath);

        // 尝试使用元数据生成唯一文件名
        var attempts = new List<string>();

        // 对于视频流，尝试添加分辨率和带宽
        if (streamSpec.MediaType == Common.Enum.MediaType.VIDEO)
        {
            if (!string.IsNullOrEmpty(streamSpec.Resolution))
            {
                attempts.Add($"{nameWithoutExt}.{streamSpec.Resolution}{ext}");
            }
            if (streamSpec.Bandwidth.HasValue)
            {
                var bandwidthMbps = streamSpec.Bandwidth.Value / 1000000.0;
                attempts.Add($"{nameWithoutExt}.{bandwidthMbps:F1}Mbps{ext}");
            }
            if (!string.IsNullOrEmpty(streamSpec.Resolution) && streamSpec.Bandwidth.HasValue)
            {
                var bandwidthMbps = streamSpec.Bandwidth.Value / 1000000.0;
                attempts.Add($"{nameWithoutExt}.{streamSpec.Resolution}.{bandwidthMbps:F1}Mbps{ext}");
            }
        }
        // 对于音频流，尝试添加语言、声道和带宽
        else if (streamSpec.MediaType == Common.Enum.MediaType.AUDIO)
        {
            if (!string.IsNullOrEmpty(streamSpec.Language))
            {
                attempts.Add($"{nameWithoutExt}.{streamSpec.Language}{ext}");
            }
            if (!string.IsNullOrEmpty(streamSpec.Channels))
            {
                attempts.Add($"{nameWithoutExt}.{streamSpec.Channels}ch{ext}");
            }
            if (!string.IsNullOrEmpty(streamSpec.Language) && !string.IsNullOrEmpty(streamSpec.Channels))
            {
                attempts.Add($"{nameWithoutExt}.{streamSpec.Language}.{streamSpec.Channels}ch{ext}");
            }
            if (streamSpec.Bandwidth.HasValue)
            {
                var bandwidthKbps = streamSpec.Bandwidth.Value / 1000;
                attempts.Add($"{nameWithoutExt}.{bandwidthKbps}kbps{ext}");
            }
        }
        // 对于字幕流，尝试添加语言
        else if (streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES)
        {
            if (!string.IsNullOrEmpty(streamSpec.Language))
            {
                attempts.Add($"{nameWithoutExt}.{streamSpec.Language}{ext}");
            }
        }

        // 尝试所有基于元数据的文件名
        foreach (var attempt in attempts)
        {
            var attemptPath = Path.Combine(dir, attempt);
            if (!File.Exists(attemptPath))
                return attemptPath;
        }

        // 所有元数据方案都失败，回退到 "copy" 方案
        var output = originalPath;
        while (File.Exists(output))
        {
            output = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(output)}.copy{Path.GetExtension(output)}");
        }
        return output;
    }
}