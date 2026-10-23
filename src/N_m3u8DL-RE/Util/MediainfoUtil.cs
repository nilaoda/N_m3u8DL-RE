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
    [GeneratedRegex(@"Stream #\d+:\d+")]
    private static partial Regex StreamIndexRegex();
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
    [GeneratedRegex(@"DOVI configuration record.*profile: (\d+).*compatibility id: (\d+)")]
    private static partial Regex DoViRegex();
    [GeneratedRegex(@"\((\w{4}) / 0x[0-9a-fA-F]+\)")]
    private static partial Regex CodecTagRegex();
    [GeneratedRegex(@"Duration.*?start: (-?\d+(?:\.\d+)?)")]
    private static partial Regex StartRegex();
    [GeneratedRegex(@"Duration:\s*(\d+:\d{2}:\d{2}\.\d+)")]
    private static partial Regex DurationRegex();

    internal static async Task<List<Mediainfo>> ReadTrackInfoAsync(string binary, OutputFile file, CancellationToken token)
    {
        // 下载和录制已经识别过轨道，后续混流直接复用，避免重复启动 FFmpeg。
        if (file.Mediainfos.Any(info => info.Type is "Video" or "Audio" or "Subtitle"))
            return file.Mediainfos;
        var input = file.Segments?.FirstOrDefault() ?? file.FilePath;
        var extension = Path.GetExtension(input).ToLowerInvariant();
        List<Mediainfo>? infos = null;
        if (extension is ".mp4" or ".m4a" or ".m4v" or ".mov" or ".m4s")
        {
            using var stream = file.Segments == null ? File.OpenRead(input) : (Stream)new ConcatInputStream(file.Segments);
            infos = MP4MediaInfoUtil.ReadTrackInfo(stream, token)?.Select(track => new Mediainfo
            {
                Type = track.Type,
                BaseInfo = track.DolbyVision ? "hevc" : track.CodecTag,
                DolbyVison = track.DolbyVision,
                CodecTag = track.CodecTag,
                DolbyVisionProfile = track.DolbyVisionProfile
            }).ToList();
        }
        else if (extension is ".srt" or ".vtt" or ".ass" or ".ssa" or ".sup" or ".idx")
            infos = [new Mediainfo { Type = "Subtitle" }];
        if (infos == null)
        {
            // 尚无媒体信息的输入只识别一次；裸分片需按完整虚拟输入读取。
            using var server = file.Segments == null ? null : new ConcatInputServer(file.Segments);
            infos = await ReadInfoAsync(binary, server?.Url ?? input, token, loopbackInput: server != null).ConfigureAwait(false);
            if (server?.Error is { } error)
                throw new IOException(string.Format(ResString.ffmpegConcatInputFailed, error.Message), error);
        }
        if (!infos.Any(info => info.Type is "Video" or "Audio" or "Subtitle"))
            throw new ArgumentException($"{ResString.toolsTrackInfoFailed}: {file.FilePath}");
        file.Mediainfos = infos;
        return infos;
    }

    internal static async Task<(double Start, double Duration)?> ReadTimingAsync(string binary, string file, CancellationToken token,
        bool loopbackInput = false)
    {
        // 非 MP4 文件沿用下载流程的 FFmpeg 输入信息读取媒体时钟。
        var result = await ProcessUtil.RunAsync(binary, ["-nostdin", "-hide_banner", "-i", file], token, loopbackInput: loopbackInput);
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

    public static async Task<List<Mediainfo>> ReadInfoAsync(string binary, string file, CancellationToken token = default, bool loopbackInput = false)
    {
        var result = new List<Mediainfo>();

        if (string.IsNullOrEmpty(file) || !loopbackInput && !File.Exists(file)) return result;

        // 探测进程不能抢占终端输入或切换终端模式，空格/回车留给程序的选择界面。
        // 复用统一的管道读取和进程管理。
        var probe = await ProcessUtil.RunAsync(binary, ["-nostdin", "-hide_banner", "-i", file], token,
            captureLimit: int.MaxValue, loopbackInput: loopbackInput).ConfigureAwait(false);
        var output = probe.Error;

        // TS 的多个 Program 可能共用同一条流，FFmpeg 会重复打印，轨道只能计入一次。
        var streams = TextRegex().Matches(output);
        foreach (var (stream, index) in streams.Select((stream, index) => (stream, index))
            .DistinctBy(item => StreamIndexRegex().Match(item.stream.Value).Value))
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

            info.CodecTag = CodecTagRegex().Match(stream.Value) is { Success: true } tag ? tag.Groups[1].Value : null;
            // 配置附属于当前轨道，不能把一条杜比视界轨道的 Profile 套到其它视频上。
            var details = output[stream.Index..(index + 1 < streams.Count ? streams[index + 1].Index : output.Length)];
            var dovi = DoViRegex().Match(details);
            if (info.BaseInfo.Contains("dvhe")
                || info.BaseInfo.Contains("dvh1")
                || info.BaseInfo.Contains("DOVI")
                || info.Type.Contains("dvvideo")
                || (dovi.Success && info.Type == "Video")
               )
                info.DolbyVison = true;
            if (info.Type == "Video" && dovi.Success && int.TryParse(dovi.Groups[1].Value, out var profile))
                info.DolbyVisionProfile = profile;

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
