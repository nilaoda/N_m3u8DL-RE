using System.Globalization;
using N_m3u8DL_RE.Entity;
using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Resource;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;

namespace N_m3u8DL_RE.Util;

internal static partial class MediainfoUtil
{
    [GeneratedRegex("  Stream #.*")]
    private static partial Regex TextRegex();
    [GeneratedRegex(@"#0:\d(\[0x\w+?\])")]
    private static partial Regex IdRegex();
    [GeneratedRegex(": (\\w+): (.*)")]
    private static partial Regex TypeRegex();
    [GeneratedRegex("(.*?)(,|$)")]
    private static partial Regex BaseInfoRegex();
    [GeneratedRegex(@" \/ 0x\w+")]
    private static partial Regex ReplaceRegex();
    [GeneratedRegex(@"\d{2,}x\d+")]
    private static partial Regex ResRegex();
    [GeneratedRegex(@"\d+ kb\/s")]
    private static partial Regex BitrateRegex();
    [GeneratedRegex(@"(\d+(\.\d+)?) fps")]
    private static partial Regex FpsRegex();
    [GeneratedRegex(@"DOVI configuration record.*profile: (\d).*compatibility id: (\d)")]
    private static partial Regex DoViRegex();
    [GeneratedRegex(@"Duration.*?start: (-?\d+(?:\.\d+)?)")]
    private static partial Regex StartRegex();
    [GeneratedRegex(@"Duration:\s*(\d+:\d{2}:\d{2}\.\d+)")]
    private static partial Regex DurationRegex();

    internal static async Task<string[]> ReadStreamTypesAsync(string binary, string file, CancellationToken token)
    {
        var result = await ProcessUtil.RunAsync(binary, ["-nostdin", "-hide_banner", "-i", file], token,
            captureLimit: int.MaxValue).ConfigureAwait(false);
        return TextRegex().Matches(result.Error).Select(stream => TypeRegex().Match(stream.Value).Groups[1].Value)
            .Where(type => type is "Video" or "Audio" or "Subtitle").ToArray();
    }

    internal static async Task<string[]> ReadTrackTypesAsync(string binary, OutputFile file, CancellationToken token)
    {
        var cached = file.Mediainfos.Select(info => info.Type).Where(type => type is "Video" or "Audio" or "Subtitle")
            .Select(type => type!).ToArray();
        if (cached.Length > 0)
            return cached;
        var extension = Path.GetExtension(file.FilePath).ToLowerInvariant();
        string[]? types;
        if (extension is ".mp4" or ".m4a" or ".m4v" or ".mov" or ".m4s")
            types = MP4MediaInfoUtil.ReadTrackTypes(file.FilePath, token);
        else if (extension is ".srt" or ".vtt" or ".ass" or ".ssa" or ".sup" or ".idx")
            types = ["Subtitle"];
        else
            types = await ReadStreamTypesAsync(binary, file.FilePath, token).ConfigureAwait(false);
        if (types == null || types.Length == 0)
            throw new ArgumentException($"{ResString.toolsTrackInfoFailed}: {file.FilePath}");
        return types;
    }

    internal static async Task<(double Start, double Duration)?> ReadTimingAsync(string binary, string file, CancellationToken token)
    {
        // 非 MP4 文件沿用下载流程的 FFmpeg 输入信息读取媒体时钟。
        var result = await ProcessUtil.RunAsync(binary, ["-nostdin", "-hide_banner", "-i", file], token);
        var start = StartRegex().Match(result.Error);
        var duration = DurationRegex().Match(result.Error);
        var hasMedia = TextRegex().Matches(result.Error).Any(stream =>
            TypeRegex().Match(stream.Value).Groups[1].Value is "Audio" or "Video");
        if (!hasMedia || !start.Success || !duration.Success ||
            !double.TryParse(start.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var origin))
            return null;
        var seconds = WebVttSub.ParseTimestamp(duration.Groups[1].Value).TotalSeconds;
        // Matroska 的 Duration 为时间轴终点，TS 等格式则直接报告媒体长度。
        if (result.Error.Contains("Input #0, matroska,webm,", StringComparison.Ordinal))
            seconds -= origin;
        return seconds > 0 ? (origin, seconds) : null;
    }

    public static async Task<List<Mediainfo>> ReadInfoAsync(string binary, string file)
    {
        var result = new List<Mediainfo>();

        if (string.IsNullOrEmpty(file) || !File.Exists(file)) return result;

        // 探测进程不能抢占终端输入或切换终端模式，空格/回车留给程序的选择界面。
        // 复用统一的管道读取和进程管理。
        var probe = await ProcessUtil.RunAsync(binary, ["-nostdin", "-hide_banner", "-i", file], default,
            captureLimit: int.MaxValue).ConfigureAwait(false);
        var output = probe.Error;

        foreach (Match stream in TextRegex().Matches(output))
        {
            var info = new Mediainfo()
            {
                Text = TypeRegex().Match(stream.Value).Groups[2].Value.TrimEnd(),
                Id = IdRegex().Match(stream.Value).Groups[1].Value,
                Type = TypeRegex().Match(stream.Value).Groups[1].Value,
            };

            info.Resolution = ResRegex().Match(info.Text).Value;
            info.Bitrate = BitrateRegex().Match(info.Text).Value;
            info.Fps = FpsRegex().Match(info.Text).Value;
            info.BaseInfo = BaseInfoRegex().Match(info.Text).Groups[1].Value;
            info.BaseInfo = ReplaceRegex().Replace(info.BaseInfo, "");
            info.HDR = info.Text.Contains("/bt2020/");

            if (info.BaseInfo.Contains("dvhe")
                || info.BaseInfo.Contains("dvh1")
                || info.BaseInfo.Contains("DOVI")
                || info.Type.Contains("dvvideo")
                || (DoViRegex().IsMatch(output) && info.Type == "Video")
               )
                info.DolbyVison = true;

            if (StartRegex().IsMatch(output))
            {
                var f = StartRegex().Match(output).Groups[1].Value;
                if (double.TryParse(f, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    info.StartTime = TimeSpan.FromSeconds(d);
            }

            result.Add(info);
        }

        if (result.Count == 0)
        {
            result.Add(new Mediainfo
            {
                Type = "Unknown"
            });
        }

        return result;
    }
}
