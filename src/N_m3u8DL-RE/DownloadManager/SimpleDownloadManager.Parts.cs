using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.DownloadManager;

internal partial class SimpleDownloadManager
{
    private async Task<bool> DownloadPartsAsync(StreamSpec stream, ProgressTask task, SpeedContainer speed)
    {
        var parts = stream.Playlist!.MediaParts.Where(p => p.MediaSegments.Count > 0).ToList();
        if (parts.Count == 0)
            return false;
        speed.ResetVars();
        task.MaxValue = parts.Sum(p => p.MediaSegments.Count + (p.MediaInit != null ? 1 : 0));
        task.StartTask();
        var dirName = OtherUtil.GetSafeFileName($"{task.Id}_{OtherUtil.GetValidFileName(stream.GroupId ?? "", "-")}_{stream.Codecs}_{stream.Bandwidth}_{stream.Language}");
        var partsDir = Path.Combine(DownloaderConfig.DirPrefix, dirName, "parts");
        var outputs = new List<OutputFile>();
        // 集合只在当前轨道顺序下载期间使用，不跨轨道或跨次下载去重。
        var logMessages = new HashSet<string>();
        var cache = new VodInitCache(Path.Combine(partsDir, "_init-cache"));

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            speed.SingleSegment = false;
            speed.ResponseLength = null;
            // 每段使用自己的 init、KID、媒体探测结果和临时目录。
            // 即使分片序号或 URL 相同，也不会覆盖另一段的下载/解密结果。
            var partStream = stream.WithPlaylist(new Playlist { MediaParts = [part] });
            partStream.Codecs = part.Codecs ?? stream.Codecs;
            partStream.GroupId = part.RepresentationId ?? stream.GroupId;
            partStream.SkippedDuration = 0;
            var options = DownloaderConfig.MyOptions.Clone();
            // FFmpeg/Packager 的逐片解密会重建时间轴，且不能独立解密 init。
            // 多 init 点播对这两种引擎按 part 解密完整文件，再按原始 PTO 拼接。
            if (options.DecryptionEngine != DecryptEngine.MP4DECRYPT)
                options.MP4RealTimeDecryption = false;
            var dir = Path.Combine(partsDir, i.ToString("D4"));
            options.SaveDir = dir;
            options.SaveName = "part";
            options.SavePattern = null;
            // 在全部段成功拼接前保留输入，后段失败时仍可重试或手动恢复。
            options.DelAfterDone = false;
            if (options.AutoSubtitleFix && stream.MediaType == MediaType.SUBTITLES)
                options.SubtitleFormat = SubtitleFormat.VTT;
            var manager = new SimpleDownloadManager(new DownloaderConfig
            {
                MyOptions = options,
                DirPrefix = dir,
                Headers = DownloaderConfig.Headers,
                CheckContentLength = DownloaderConfig.CheckContentLength,
            }, [partStream], StreamExtractor);
            manager.initCache = cache;
            manager.partLogMessages = logMessages;
            manager.hlsMediaOrigins = hlsMediaOrigins;
            manager.hlsMediaReady = hlsMediaReady;
            manager.hlsSubtitleOnlyCuts = hlsSubtitleOnlyCuts;
            if (!await manager.DownloadStreamAsync(partStream, task, speed, isPart: true))
                return false;
            if (options.SkipMerge)
                continue;
            var output = manager.OutputFiles.SingleOrDefault();
            if (output == null)
                return false;
            outputs.Add(output);
        }

        if (DownloaderConfig.MyOptions.SkipMerge)
            return true;
        // 清单可能省略 codecs/channels，下载后再用实际探测结果兜底。
        // 音频采样率、声道布局和 AAC profile 变化无法无损归并为一条固定配置的轨道。
        var signatures = outputs.Select(o => string.Join("|", o.Mediainfos
            .Where(info => info.Type is "Video" or "Audio")
            .Select(info => info.Type + ":" + (info.BaseInfo ?? "").Split(' ')[0] +
                (info.Type == "Audio" ? ":" + Regex.Match(info.Text ?? "", @"\b\d+ Hz,\s*[^,]+").Value +
                    ":" + Regex.Match(info.BaseInfo ?? "", @"^aac \([^)]*\)").Value : "")))).Distinct().ToList();
        if (signatures.Count > 1)
            throw new NotSupportedException(ResString.vodPartsIncompatible);
        var first = outputs[0];
        var saveName = !string.IsNullOrWhiteSpace(DownloaderConfig.MyOptions.SavePattern)
            ? OtherUtil.FormatSavePattern(DownloaderConfig.MyOptions.SavePattern, stream, DownloaderConfig.MyOptions.SaveName, task.Id)
            : DownloaderConfig.MyOptions.SaveName != null
                ? $"{DownloaderConfig.MyOptions.SaveName}.{stream.Language}".TrimEnd('.') : dirName;
        var subtitle = stream.MediaType == MediaType.SUBTITLES && DownloaderConfig.MyOptions.AutoSubtitleFix;
        var ext = subtitle ? (DownloaderConfig.MyOptions.SubtitleFormat == SubtitleFormat.VTT ? ".vtt" : ".srt")
            : Path.GetExtension(first.FilePath);
        if (ext is ".m4s" or ".ts" && parts.Any(part => part.MediaInit != null))
            ext = stream.MediaType == MediaType.AUDIO ? ".m4a" : ".mp4";
        var outputPath = OtherUtil.HandleFileCollision(Path.Combine(
            DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory, saveName + ext), stream, reservedOutputPaths);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        // 单段且没有裁剪/输出偏移时，子下载器的合并结果就是最终文件。
        // 若其它媒体轨道需要保留公共时间轴，则本轨道也必须完成时间轴归一化。
        var copySingle = outputs.Count == 1 && SelectedSteams.Where(s => s.MediaType != MediaType.SUBTITLES)
            .All(CanUseSinglePartOutput);
        bool success;
        if (subtitle)
        {
            success = await MergeSubtitlesAsync(parts, outputs, outputPath);
        }
        else if (copySingle)
        {
            if (DownloaderConfig.MyOptions.DelAfterDone)
                File.Move(first.FilePath, outputPath);
            else
                File.Copy(first.FilePath, outputPath);
            success = true;
        }
        else
        {
            Logger.InfoMarkUp($"[grey]{(parts.Count > 1 ? string.Format(ResString.vodPartsConcat, parts.Count) : ResString.ffmpegMerge)}[/]");
            success = MergeUtil.ConcatMediaParts(DownloaderConfig.MyOptions.FFmpegBinaryPath!,
                outputs.Select(o => o.FilePath).ToArray(), parts, outputPath);
        }
        if (!success)
        {
            // FFmpeg 失败也可能已经写出文件，不能把半成品留在最终保存目录。
            // 独立 part 与原始分片仍保留在 partsDir，供重试或手动恢复。
            File.Delete(outputPath);
            return false;
        }
        first.FilePath = outputPath;
        first.PreserveTimestamp = !subtitle && !copySingle && parts.Any(p => p.OutputStart != null);
        lock (OutputFiles)
            OutputFiles.Add(first);
        if (DownloaderConfig.MyOptions.DelAfterDone)
        {
            // 成功后才清理分段文件；图形字幕提取的 PNG 保留供用户使用。
            foreach (var file in Directory.EnumerateFiles(partsDir, "*", SearchOption.AllDirectories))
                if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            OtherUtil.SafeDeleteDir(partsDir);
        }
        return true;
    }

    private static bool CanUseSinglePartOutput(StreamSpec stream)
    {
        if (stream.Playlist?.MediaParts.Count != 1 || stream.SkippedDuration > 0)
            return false;
        var part = stream.Playlist.MediaParts[0];
        if (Math.Abs(part.OutputStart ?? 0) > 0.001)
            return false;
        if (part.PeriodIndex == null)
            return Math.Abs(part.MediaSegments.FirstOrDefault()?.HlsTime ?? 0) <= 0.001;
        // DASH 仍需处理非零 PTO、共同 inpoint 和跨越 Period 尾部的分片。
        return Math.Abs(GetPartInpoint(part) ?? 0) <= 0.001 &&
            (part.OutputDuration == null || part.OutputDuration >= part.MediaSegments.Sum(s => s.Duration) - 0.001);
    }

    private async Task<bool> MergeSubtitlesAsync(List<MediaPart> parts, List<OutputFile> outputs, string output)
    {
        var final = new WebVttSub();
        double elapsed = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var sub = WebVttSub.Parse(await File.ReadAllTextAsync(outputs[i].FilePath));
            var start = part.OutputStart ?? elapsed;
            var inpoint = GetPartInpoint(part) ?? 0;
            if (StreamExtractor.ExtractorType == ExtractorType.HLS && part.MediaInit != null &&
                hlsMediaOrigins?.TryGetValue(part.DiscontinuitySequence ?? 0, out var origin) == true)
                inpoint += origin;
            var duration = part.OutputDuration ?? part.MediaSegments.Sum(s => s.Duration);
            // 各段字幕已用自己的 init/timescale 提取，按媒体时间换算到输出时间。
            // 不能靠 cue 数量或最后一句结束时间推进 Period，否则无对白的尾部会消失。
            foreach (var cue in sub.Cues)
            {
                var cueStart = cue.StartTime.TotalSeconds - inpoint;
                var cueEnd = cue.EndTime.TotalSeconds - inpoint;
                if (cueEnd <= 0 || cueStart >= duration)
                    continue;
                cue.StartTime = TimeSpan.FromSeconds(start + Math.Max(0, cueStart));
                cue.EndTime = TimeSpan.FromSeconds(start + Math.Min(duration, cueEnd));
                final.Cues.Add(cue);
            }
            elapsed = start + duration;
        }
        await File.WriteAllTextAsync(output, DownloaderConfig.MyOptions.SubtitleFormat == SubtitleFormat.VTT
            ? final.ToVtt() : final.ToSrt());
        return true;
    }

    internal static double? GetPartInpoint(MediaPart part)
    {
        if (part.OutputInpoint != null)
            return part.OutputInpoint;
        if (part.PeriodIndex == null)
            return null;
        return Math.Max(part.PresentationTimeOffset ?? 0, part.MediaSegments.FirstOrDefault()?.PresentationTime ?? 0);
    }
}
