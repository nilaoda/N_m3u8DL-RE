using System.Collections.Concurrent;
using System.Text;

using Mp4SubtitleParser;

using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enumerations;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Mp4;
using N_m3u8DL_RE.Util;

using Spectre.Console;

namespace N_m3u8DL_RE.DownloadManager
{
    internal sealed class SimpleDownloadManager
    {
        private readonly SimpleDownloader Downloader;
        private readonly DownloaderConfig DownloaderConfig;
        private readonly StreamExtractor StreamExtractor;
        private readonly List<StreamSpec> SelectedSteams;
        private List<OutputFile> OutputFiles = [];

        public SimpleDownloadManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            DownloaderConfig = downloaderConfig;
            SelectedSteams = selectedSteams;
            StreamExtractor = streamExtractor;
            Downloader = new SimpleDownloader(DownloaderConfig);
        }

        // 从文件读取KEY
        private async Task SearchKeyAsync(string? currentKID)
        {
            string? _key = await MP4DecryptUtil.SearchKeyFromFileAsync(DownloaderConfig.MyOptions.KeyTextFile, currentKID);
            if (_key != null)
            {
                DownloaderConfig.MyOptions.Keys = DownloaderConfig.MyOptions.Keys == null ? [_key] : [.. DownloaderConfig.MyOptions.Keys, _key];
            }
        }

        private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
        {
            if (!DownloaderConfig.MyOptions.BinaryMerge && mediainfos.Any(m => m.DolbyVison))
            {
                DownloaderConfig.MyOptions.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge2}[/]");
            }

            if (DownloaderConfig.MyOptions.MuxAfterDone && mediainfos.Any(m => m.DolbyVison))
            {
                DownloaderConfig.MyOptions.MuxAfterDone = false;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge5}[/]");
            }

            if (mediainfos.Where(m => m.Type == "Audio").All(m => m.BaseInfo!.Contains("aac")))
            {
                useAACFilter = true;
            }

            if (mediainfos.All(m => m.Type == "Audio"))
            {
                streamSpec.MediaType = MediaType.AUDIO;
            }
            else if (mediainfos.All(m => m.Type == "Subtitle"))
            {
                streamSpec.MediaType = MediaType.SUBTITLES;
                if (streamSpec.Extension is null or "ts")
                {
                    streamSpec.Extension = "vtt";
                }
            }
        }

        private async Task<bool> DownloadStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer)
        {
            speedContainer.ResetVars();
            bool useAACFilter = false; // ffmpeg合并flag
            List<Mediainfo> mediaInfos = [];
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();

            IEnumerable<MediaSegment>? segments = streamSpec.Playlist?.MediaParts.SelectMany(m => m.MediaSegments);
            if (segments == null || !segments.Any())
            {
                return false;
            }
            // 单分段尝试切片并行下载
            if (segments.Count() == 1)
            {
                List<MediaSegment>? splitSegments = await LargeSingleFileSplitUtil.SplitUrlAsync(segments.First(), DownloaderConfig.Headers);
                if (splitSegments != null)
                {
                    segments = splitSegments;
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.SingleFileSplitWarn}[/]");
                    if (DownloaderConfig.MyOptions.MP4RealTimeDecryption)
                    {
                        DownloaderConfig.MyOptions.MP4RealTimeDecryption = false;
                        Logger.WarnMarkUp($"[darkorange3_1]{ResString.SingleFileRealtimeDecryptWarn}[/]");
                    }
                }
                else
                {
                    speedContainer.SingleSegment = true;
                }
            }

            MediaType type = streamSpec.MediaType ?? MediaType.VIDEO;
            string dirName = $"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            string tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
            string saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            string saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;
            Dictionary<string, string> headers = DownloaderConfig.Headers;

            string decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
            DecryptEngine decryptEngine = DownloaderConfig.MyOptions.DecryptionEngine;
            string mp4InitFile = "";
            string? currentKID = "";
            bool readInfo = false; // 是否读取过
            ParsedMP4Info mp4Info = new();

            // 用户自定义范围导致被跳过的时长 计算字幕偏移使用
            double skippedDur = streamSpec.SkippedDuration ?? 0d;

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

            // 创建文件夹
            if (!Directory.Exists(tmpDir))
            {
                Directory.CreateDirectory(tmpDir);
            }

            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            int totalCount = segments.Count();
            if (streamSpec.Playlist?.MediaInit != null)
            {
                totalCount++;
            }

            task.MaxValue = totalCount;
            task.StartTask();

            // 开始下载
            Logger.InfoMarkUp(ResString.StartDownloading + streamSpec.ToShortString());

            // 对于CENC，全部自动开启二进制合并
            if (!DownloaderConfig.MyOptions.BinaryMerge && totalCount >= 1 && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method == EncryptMethod.CENC)
            {
                DownloaderConfig.MyOptions.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge4}[/]");
            }

            // 下载init
            if (streamSpec.Playlist?.MediaInit != null)
            {
                // 对于fMP4，自动开启二进制合并
                if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                {
                    DownloaderConfig.MyOptions.BinaryMerge = true;
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge}[/]");
                }

                string path = Path.Combine(tmpDir, "_init.mp4.tmp");
                DownloadResult? result = await Downloader.DownloadSegmentAsync(streamSpec.Playlist.MediaInit, path, speedContainer, headers);
                FileDic[streamSpec.Playlist.MediaInit] = result;
                if (result is not { Success: true })
                {
                    string errorDetails = result?.ActualContentLength != null ?
                        $"Expected: {result.RespContentLength} bytes, Got: {result.ActualContentLength} bytes" :
                        "No content received";
                    throw new InvalidOperationException($"Failed to download initialization file for stream '{streamSpec.ToShortString()}'. {errorDetails}. URL: {streamSpec.Playlist.MediaInit.Url}");
                }
                mp4InitFile = result.ActualFilePath;
                task.Increment(1);

                // 读取mp4信息
                if (result is { Success: true })
                {
                    mp4Info = MP4DecryptUtil.GetMP4Info(result.ActualFilePath);
                    currentKID = mp4Info.KID;
                    // try shaka packager, which can handle WebM
                    if (string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER)
                    {
                        currentKID = MP4DecryptUtil.ReadInitShaka(result.ActualFilePath, decryptionBinaryPath);
                    }
                    // 从文件读取KEY
                    await SearchKeyAsync(currentKID);
                    // 实时解密
                    if ((streamSpec.Playlist.MediaInit.IsEncrypted || !string.IsNullOrEmpty(currentKID)) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                    {
                        string enc = result.ActualFilePath;
                        string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, isMultiDRM: mp4Info.isMultiDRM);
                        if (dResult)
                        {
                            FileDic[streamSpec.Playlist.MediaInit]!.ActualFilePath = dec;
                        }
                    }
                    // ffmpeg读取信息
                    if (!readInfo)
                    {
                        Logger.WarnMarkUp(ResString.ReadingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
                    }
                }
            }

            // 计算填零个数
            string pad = "0".PadLeft(segments.Count().ToString().Length, '0');

            // 下载第一个分片
            if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
            {
                MediaSegment seg = segments.First();
                segments = segments.Skip(1);

                long index = seg.Index;
                string path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
                DownloadResult? result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                if (result is not { Success: true })
                {
                    string errorDetails = result?.ActualContentLength != null ?
                        $"Expected: {result.RespContentLength} bytes, Got: {result.ActualContentLength} bytes" :
                        "No content received";
                    throw new InvalidOperationException($"Failed to download first segment (Index: {seg.Index}) for stream '{streamSpec.ToShortString()}'. {errorDetails}. URL: {seg.Url}");
                }
                task.Increment(1);
                if (result is { Success: true })
                {
                    // 修复MSS init
                    if (StreamExtractor.ExtractorType == ExtractorType.MSS)
                    {
                        MSSMoovProcessor processor = new(streamSpec);
                        byte[] header = processor.GenHeader(File.ReadAllBytes(result.ActualFilePath));
                        await File.WriteAllBytesAsync(FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath, header);
                        if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                        {
                            // 需要重新解密init
                            string enc = FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath;
                            string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                            if (dResult)
                            {
                                FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath = dec;
                            }
                        }
                    }
                    // 读取init信息
                    if (string.IsNullOrEmpty(currentKID))
                    {
                        currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                    }
                    // try shaka packager, which can handle WebM
                    if (string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER)
                    {
                        currentKID = MP4DecryptUtil.ReadInitShaka(result.ActualFilePath, decryptionBinaryPath);
                    }
                    // 从文件读取KEY
                    await SearchKeyAsync(currentKID);
                    // 实时解密
                    if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                    {
                        string enc = result.ActualFilePath;
                        string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        mp4Info = MP4DecryptUtil.GetMP4Info(enc);
                        bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile, isMultiDRM: mp4Info.isMultiDRM);
                        if (dResult)
                        {
                            File.Delete(enc);
                            result.ActualFilePath = dec;
                        }
                    }
                    if (!readInfo)
                    {
                        // ffmpeg读取信息
                        Logger.WarnMarkUp(ResString.ReadingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
                    }
                }
            }

            // 开始下载
            ParallelOptions options = new()
            {
                MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
            };
            await Parallel.ForEachAsync(segments, options, async (seg, _) =>
            {
                long index = seg.Index;
                string path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
                DownloadResult? result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                if (result is { Success: true })
                {
                    task.Increment(1);
                }
                // 实时解密
                if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && result is { Success: true } && !string.IsNullOrEmpty(currentKID))
                {
                    string enc = result.ActualFilePath;
                    string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    mp4Info = MP4DecryptUtil.GetMP4Info(enc);
                    bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile, isMultiDRM: mp4Info.isMultiDRM);
                    if (dResult)
                    {
                        File.Delete(enc);
                        result.ActualFilePath = dec;
                    }
                }
            });

            // 修改输出后缀
            string outputExt = "." + streamSpec.Extension;
            if (streamSpec.Extension == null)
            {
                outputExt = ".ts";
            }
            else if (streamSpec is { MediaType: MediaType.AUDIO, Extension: "m4s" or "mp4" })
            {
                outputExt = ".m4a";
            }
            else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension is "m4s" or "mp4")
            {
                outputExt = ".mp4";
            }

            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == MediaType.SUBTITLES)
            {
                outputExt = DownloaderConfig.MyOptions.SubtitleFormat == SubtitleFormat.SRT ? ".srt" : ".vtt";
            }
            string output = Path.Combine(saveDir, saveName + outputExt);

            // 检测目标文件是否存在
            while (File.Exists(output))
            {
                Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(output = Path.ChangeExtension(output, $"copy" + Path.GetExtension(output)))}");
            }

            if (!string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, Keys.Length: > 0 } && mp4InitFile != "")
            {
                File.Delete(mp4InitFile);
                // shaka/ffmpeg实时解密不需要init文件用于合并
                if (decryptEngine != DecryptEngine.MP4DECRYPT)
                {
                    FileDic!.Remove(streamSpec.Playlist!.MediaInit, out _);
                }
            }

            // 校验分片数量
            if (DownloaderConfig.MyOptions.CheckSegmentsCount && FileDic.Values.Any(s => s == null))
            {
                Logger.ErrorMarkUp(ResString.SegmentCountCheckNotPass, totalCount, FileDic.Values.Count(s => s != null));
                return false;
            }

            // 移除无效片段
            IEnumerable<MediaSegment> badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
            foreach (MediaSegment? badKey in badKeys)
            {
                FileDic!.Remove(badKey, out _);
            }

            // 校验完整性
            if (DownloaderConfig.CheckContentLength && FileDic.Values.Any(a => !a!.Success))
            {
                return false;
            }

            // 自动修复VTT raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
            {
                Logger.WarnMarkUp(ResString.FixingVTT);
                // 排序字幕并修正时间戳
                bool first = true;
                WebVttSub finalVtt = new();
                IOrderedEnumerable<MediaSegment> keys = FileDic.Keys.OrderBy(k => k.Index);
                foreach (MediaSegment? seg in keys)
                {
                    string vttContent = File.ReadAllText(FileDic[seg]!.ActualFilePath);
                    WebVttSub vtt = WebVttSub.Parse(vttContent);
                    // 手动计算MPEGTS
                    if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else
                    {
                        finalVtt.AddCuesFromOne(vtt);
                    }
                }
                // 写出字幕
                string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];
                foreach (string? item in files)
                {
                    File.Delete(item);
                }

                FileDic.Clear();
                int index = 0;
                string path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                // 设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                string subContentFixed = finalVtt.ToVtt();
                // 转换字幕格式
                if (DownloaderConfig.MyOptions.SubtitleFormat != SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = finalVtt.ToSrt();
                }
                await File.WriteAllTextAsync(path, subContentFixed, Encoding.UTF8);
                FileDic[keys.First()] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
            }

            // 自动修复VTT mp4字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == MediaType.SUBTITLES
                                                           && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
            {
                DownloadResult? initFile = FileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init"));
                byte[] iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                (bool sawVtt, uint timescale) = MP4VttUtil.CheckInit(iniFileBytes);
                if (sawVtt)
                {
                    Logger.WarnMarkUp(ResString.FixingVTTmp4);
                    string[] mp4s = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s"))];
                    WebVttSub finalVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    // 写出字幕
                    MediaSegment firstKey = FileDic.Keys.First();
                    string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];
                    foreach (string? item in files)
                    {
                        File.Delete(item);
                    }

                    FileDic.Clear();
                    int index = 0;
                    string path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                    // 设置字幕偏移
                    finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                    string subContentFixed = finalVtt.ToVtt();
                    // 转换字幕格式
                    if (DownloaderConfig.MyOptions.SubtitleFormat != SubtitleFormat.VTT)
                    {
                        path = Path.ChangeExtension(path, ".srt");
                        subContentFixed = finalVtt.ToSrt();
                    }
                    await File.WriteAllTextAsync(path, subContentFixed, Encoding.UTF8);
                    FileDic[firstKey] = new DownloadResult()
                    {
                        ActualContentLength = subContentFixed.Length,
                        ActualFilePath = path
                    };
                }
            }

            // 自动修复TTML raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
            {
                Logger.WarnMarkUp(ResString.FixingTTML);
                bool first = true;
                WebVttSub finalVtt = new();
                IEnumerable<MediaSegment> keys = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Key);
                foreach (MediaSegment? seg in keys)
                {
                    WebVttSub vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0);
                    // 手动计算MPEGTS
                    if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else
                    {
                        finalVtt.AddCuesFromOne(vtt);
                    }
                }
                // 写出字幕
                MediaSegment firstKey = FileDic.Keys.First();
                string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];

                // 处理图形字幕
                await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

                string keepSegments = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReKeepImageSegments);
                if (keepSegments != "1")
                {
                    foreach (string? item in files)
                    {
                        File.Delete(item);
                    }
                }

                FileDic.Clear();
                int index = 0;
                string path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                // 设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                string subContentFixed = finalVtt.ToVtt();
                // 转换字幕格式
                if (DownloaderConfig.MyOptions.SubtitleFormat != SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = finalVtt.ToSrt();
                }
                await File.WriteAllTextAsync(path, subContentFixed, Encoding.UTF8);
                FileDic[firstKey] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
            }

            // 自动修复TTML mp4字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("m4s")
                && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
            {
                Logger.WarnMarkUp(ResString.FixingTTMLmp4);
                // sawTtml暂时不判断
                // var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                bool first = true;
                WebVttSub finalVtt = new();
                IEnumerable<MediaSegment> keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
                foreach (MediaSegment? seg in keys)
                {
                    WebVttSub vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0);
                    // 手动计算MPEGTS
                    if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else
                    {
                        finalVtt.AddCuesFromOne(vtt);
                    }
                }

                // 写出字幕
                MediaSegment firstKey = FileDic.Keys.First();
                string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];

                // 处理图形字幕
                await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

                string keepSegments = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReKeepImageSegments);
                if (keepSegments != "1")
                {
                    foreach (string? item in files)
                    {
                        File.Delete(item);
                    }
                }

                FileDic.Clear();
                int index = 0;
                string path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                // 设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                string subContentFixed = finalVtt.ToVtt();
                // 转换字幕格式
                if (DownloaderConfig.MyOptions.SubtitleFormat != SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = finalVtt.ToSrt();
                }
                await File.WriteAllTextAsync(path, subContentFixed, Encoding.UTF8);
                FileDic[firstKey] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
            }

            bool mergeSuccess = false;
            // 合并
            if (!DownloaderConfig.MyOptions.SkipMerge)
            {
                // 字幕也使用二进制合并
                if (DownloaderConfig.MyOptions.BinaryMerge || streamSpec.MediaType == MediaType.SUBTITLES)
                {
                    Logger.InfoMarkUp(ResString.BinaryMerge);
                    string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];
                    MergeUtil.CombineMultipleFilesIntoSingleFile(files, output);
                    mergeSuccess = true;
                }
                else
                {
                    // ffmpeg合并
                    string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath)];
                    Logger.InfoMarkUp(ResString.FfmpegMerge);
                    string ext = streamSpec.MediaType == MediaType.AUDIO ? "m4a" : "mp4";
                    string ffOut = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + $".{ext}");
                    // 检测目标文件是否存在
                    while (File.Exists(ffOut))
                    {
                        Logger.WarnMarkUp($"{Path.GetFileName(ffOut)} => {Path.GetFileName(ffOut = Path.ChangeExtension(ffOut, $"copy" + Path.GetExtension(ffOut)))}");
                    }
                    // 大于1800分片，需要分步骤合并
                    if (files.Length >= 1800)
                    {
                        Logger.WarnMarkUp(ResString.PartMerge);
                        files = MergeUtil.PartialCombineMultipleFiles(files);
                        FileDic.Clear();
                        foreach (string? item in files)
                        {
                            FileDic[new MediaSegment() { Url = item }] = new DownloadResult()
                            {
                                ActualFilePath = item
                            };
                        }
                    }
                    mergeSuccess = MergeUtil.MergeByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, files, Path.ChangeExtension(ffOut, null), ext, useAACFilter, writeDate: !DownloaderConfig.MyOptions.NoDateInfo, useConcatDemuxer: DownloaderConfig.MyOptions.UseFFmpegConcatDemuxer);
                    if (mergeSuccess)
                    {
                        output = ffOut;
                    }
                }
            }

            // 删除临时文件夹
            if (DownloaderConfig.MyOptions is { SkipMerge: false, DelAfterDone: true } && mergeSuccess)
            {
                IEnumerable<string> files = FileDic.Values.Select(v => v!.ActualFilePath);
                foreach (string? file in files)
                {
                    File.Delete(file);
                }
                OtherUtil.SafeDeleteDir(tmpDir);
            }

            // 重新读取init信息
            if (mergeSuccess && totalCount >= 1 && string.IsNullOrEmpty(currentKID) && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method != EncryptMethod.NONE)
            {
                currentKID = MP4DecryptUtil.GetMP4Info(output).KID;
                // try shaka packager, which can handle WebM
                if (string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER)
                {
                    currentKID = MP4DecryptUtil.ReadInitShaka(output, decryptionBinaryPath);
                }
                // 从文件读取KEY
                await SearchKeyAsync(currentKID);
            }

            // 调用mp4decrypt解密
            if (mergeSuccess && File.Exists(output) && !string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions is { MP4RealTimeDecryption: false, Keys.Length: > 0 })
            {
                string enc = output;
                string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                mp4Info = MP4DecryptUtil.GetMP4Info(enc);
                Logger.InfoMarkUp($"[grey]Decrypting using {decryptEngine}...[/]");
                bool result = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, isMultiDRM: mp4Info.isMultiDRM);
                if (result)
                {
                    File.Delete(enc);
                    File.Move(dec, enc);
                }
            }

            // 记录所有文件信息
            if (File.Exists(output))
            {
                OutputFiles.Add(new OutputFile()
                {
                    Index = task.Id,
                    FilePath = output,
                    LangCode = streamSpec.Language,
                    Description = streamSpec.Name,
                    Mediainfos = mediaInfos,
                    MediaType = streamSpec.MediaType,
                });
            }

            return true;
        }

        public async Task<bool> StartDownloadAsync()
        {
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            Progress progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
            progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

            // 进度条的列定义
            ProgressColumn[] progressColumns =
            [
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new ProgressBarColumn(){ Width = 30 },
                new MyPercentageColumn(),
                new DownloadStatusColumn(SpeedContainerDic),
                new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            ];
            if (DownloaderConfig.MyOptions.NoAnsiColor)
            {
                progressColumns = [.. progressColumns.SkipLast(1)];
            }
            progress.Columns(progressColumns);

            if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.RealTimeDecMessage}[/]");
            }

            await progress.StartAsync(async ctx =>
            {
                // 创建任务
                Dictionary<StreamSpec, ProgressTask> dic = SelectedSteams.Select(item =>
                {
                    string description = item.ToShortShortString();
                    ProgressTask task = ctx.AddTask(description, autoStart: false);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                    // 限速设置
                    if (DownloaderConfig.MyOptions.MaxSpeed != null)
                    {
                        SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                    }
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                if (!DownloaderConfig.MyOptions.ConcurrentDownload)
                {
                    // 遍历，顺序下载
                    foreach (KeyValuePair<StreamSpec, ProgressTask> kp in dic)
                    {
                        ProgressTask task = kp.Value;
                        bool result = await DownloadStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                        Results[kp.Key] = result;
                        // 失败不再下载后续
                        if (!result)
                        {
                            break;
                        }
                    }
                }
                else
                {
                    // 并发下载
                    await Parallel.ForEachAsync(dic, async (kp, _) =>
                    {
                        ProgressTask task = kp.Value;
                        bool result = await DownloadStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                        Results[kp.Key] = result;
                    });
                }
            });

            bool success = Results.Values.All(v => v == true);

            // 删除临时文件夹
            if (DownloaderConfig.MyOptions is { SkipMerge: false, DelAfterDone: true } && success)
            {
                foreach (KeyValuePair<string, string> item in StreamExtractor.RawFiles)
                {
                    string file = Path.Combine(DownloaderConfig.DirPrefix, item.Key);
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                    }
                }
                OtherUtil.SafeDeleteDir(DownloaderConfig.DirPrefix);
            }

            // 混流
            if (success && DownloaderConfig.MyOptions.MuxAfterDone && OutputFiles.Count > 0)
            {
                OutputFiles = [.. OutputFiles.OrderBy(o => o.Index)];
                // 是否跳过字幕
                if (DownloaderConfig.MyOptions.MuxOptions!.SkipSubtitle)
                {
                    OutputFiles = [.. OutputFiles.Where(o => o.MediaType != MediaType.SUBTITLES)];
                }
                if (DownloaderConfig.MyOptions.MuxImports != null)
                {
                    OutputFiles.AddRange(DownloaderConfig.MyOptions.MuxImports);
                }
                OutputFiles.ForEach(f => Logger.WarnMarkUp($"[grey]{Path.GetFileName(f.FilePath).EscapeMarkup()}[/]"));
                string saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                string ext = OtherUtil.GetMuxExtension(DownloaderConfig.MyOptions.MuxOptions.MuxFormat);
                string dirName = Path.GetFileName(DownloaderConfig.DirPrefix);
                string outName = $"{dirName}.MUX";
                string outPath = Path.Combine(saveDir, outName);
                Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}{ext}[/]");
                bool result = DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge
                    ? MergeUtil.MuxInputsByMkvmerge(DownloaderConfig.MyOptions.MkvmergeBinaryPath!, [.. OutputFiles], outPath)
                    : MergeUtil.MuxInputsByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, [.. OutputFiles], outPath, DownloaderConfig.MyOptions.MuxOptions.MuxFormat, !DownloaderConfig.MyOptions.NoDateInfo);
                // 完成后删除各轨道文件
                if (result)
                {
                    if (!DownloaderConfig.MyOptions.MuxOptions.KeepFiles)
                    {
                        Logger.WarnMarkUp("[grey]Cleaning files...[/]");
                        OutputFiles.ForEach(f => File.Delete(f.FilePath));
                        string tmpDir = DownloaderConfig.MyOptions.TmpDir ?? Environment.CurrentDirectory;
                        OtherUtil.SafeDeleteDir(tmpDir);
                    }
                }
                else
                {
                    success = false;
                    Logger.ErrorMarkUp($"Mux failed");
                }
                // 判断是否要改名
                string newPath = Path.ChangeExtension(outPath, ext);
                if (result && !File.Exists(newPath))
                {
                    Logger.WarnMarkUp($"Rename to [grey]{Path.GetFileName(newPath).EscapeMarkup()}[/]");
                    File.Move(outPath + ext, newPath);
                }
            }

            return success;
        }
    }
}