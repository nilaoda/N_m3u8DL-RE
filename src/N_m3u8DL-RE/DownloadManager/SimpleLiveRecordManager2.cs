﻿using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks.Dataflow;

using Mp4SubtitleParser;

using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enumerations;
using N_m3u8DL_RE.StreamParser;
using N_m3u8DL_RE.StreamParser.Mp4;
using N_m3u8DL_RE.Util;

using Spectre.Console;

namespace N_m3u8DL_RE.DownloadManager
{
    internal sealed class SimpleLiveRecordManager2 : IDisposable
    {
        private readonly SimpleDownloader Downloader;
        private readonly DownloaderConfig DownloaderConfig;
        private readonly StreamExtractor StreamExtractor;
        private readonly List<StreamSpec> SelectedSteams;
        private readonly ConcurrentDictionary<int, string> PipeSteamNamesDic = new();
        private List<OutputFile> OutputFiles = [];
        private readonly DateTime? PublishDateTime;
        private bool STOP_FLAG;
        private int WAIT_SEC; // 刷新间隔
        private readonly ConcurrentDictionary<int, int> RecordedDurDic = new(); // 已录制时长
        private readonly ConcurrentDictionary<int, int> RefreshedDurDic = new(); // 已刷新出的时长
        private readonly ConcurrentDictionary<int, BufferBlock<List<MediaSegment>>> BlockDic = new(); // 各流的Block
        private readonly ConcurrentDictionary<int, bool> SamePathDic = new(); // 各流是否allSamePath
        private readonly ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); // 各流是否达到上限
        private readonly ConcurrentDictionary<int, string> LastFileNameDic = new(); // 上次下载的文件名
        private readonly ConcurrentDictionary<int, long> MaxIndexDic = new(); // 最大Index
        private readonly ConcurrentDictionary<int, long> DateTimeDic = new(); // 上次下载的dateTime
        private readonly CancellationTokenSource CancellationTokenSource = new(); // 取消Wait

        private readonly Lock lockObj = new();
        private TimeSpan? audioStart;

        public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
            StreamExtractor = streamExtractor;
            SelectedSteams = selectedSteams;
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

        /// <summary>
        /// 获取时间戳
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        private static long GetUnixTimestamp(DateTime dateTime)
        {
            return new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeSeconds();
        }

        /// <summary>
        /// 获取分段文件夹
        /// </summary>
        /// <param name="segment"></param>
        /// <param name="allHasDatetime"></param>
        /// <returns></returns>
        private string GetSegmentName(MediaSegment segment, bool allHasDatetime, bool allSamePath)
        {
            if (!string.IsNullOrEmpty(segment.NameFromVar))
            {
                return segment.NameFromVar;
            }

            bool hls = StreamExtractor.ExtractorType == ExtractorType.HLS;

            string name = OtherUtil.GetFileNameFromInput(segment.Url, false);
            if (allSamePath)
            {
                name = OtherUtil.GetValidFileName(segment.Url.Split('?').Last(), "_");
            }

            if (hls && allHasDatetime)
            {
                name = GetUnixTimestamp(segment.DateTime!.Value).ToString(CultureInfo.InvariantCulture);
            }
            else if (hls)
            {
                name = segment.Index.ToString(CultureInfo.InvariantCulture);
            }

            return name;
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

            if (mediainfos.All(m => m.Type == "Audio") && streamSpec.MediaType != MediaType.AUDIO)
            {
                streamSpec.MediaType = MediaType.AUDIO;
            }
            else if (mediainfos.All(m => m.Type == "Subtitle") && streamSpec.MediaType != MediaType.SUBTITLES)
            {
                streamSpec.MediaType = MediaType.SUBTITLES;

                if (streamSpec.Extension is null or "ts")
                {
                    streamSpec.Extension = "vtt";
                }
            }
        }

        private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, BufferBlock<List<MediaSegment>> source)
        {
            long baseTimestamp = PublishDateTime == null ? 0L : (long)(PublishDateTime.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
            string decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
            string mp4InitFile = "";
            string? currentKID = "";
            bool readInfo = false; // 是否读取过
            bool useAACFilter = false; // ffmpeg合并flag
            bool initDownloaded = false; // 是否下载过init文件
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
            List<Mediainfo> mediaInfos = [];
            Stream? fileOutputStream = null;
            WebVttSub currentVtt = new(); // 字幕流始终维护一个实例
            bool firstSub = true;
            task.StartTask();

            string name = streamSpec.ToShortString();
            MediaType type = streamSpec.MediaType ?? MediaType.VIDEO;
            string dirName = $"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            string tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
            string saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            string saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;
            Dictionary<string, string> headers = DownloaderConfig.Headers;
            DecryptEngine decryptEngine = DownloaderConfig.MyOptions.DecryptionEngine;

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

            // 创建文件夹
            if (!Directory.Exists(tmpDir))
            {
                _ = Directory.CreateDirectory(tmpDir);
            }

            if (!Directory.Exists(saveDir))
            {
                _ = Directory.CreateDirectory(saveDir);
            }

            while (true && await source.OutputAvailableAsync())
            {
                // 接收新片段 且总是拿全部未处理的片段
                // 有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
                // TryReceiveAll可以稍微缓解一下
                _ = source.TryReceiveAll(out IList<List<MediaSegment>>? segmentsList);
                IEnumerable<MediaSegment> segments = segmentsList!.SelectMany(s => s);
                if (segments == null || !segments.Any())
                {
                    continue;
                }

                double segmentsDuration = segments.Sum(s => s.Duration);
                Logger.DebugMarkUp(string.Join(",", segments.Select(sss => GetSegmentName(sss, false, false))));

                // 下载init
                if (!initDownloaded && streamSpec.Playlist?.MediaInit != null)
                {
                    task.MaxValue += 1;
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
                        currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                        // 从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        // 实时解密
                        if ((streamSpec.Playlist.MediaInit.IsEncrypted || !string.IsNullOrEmpty(currentKID)) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                        {
                            string enc = result.ActualFilePath;
                            string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
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
                            lock (lockObj)
                            {
                                if (audioStart == null)
                                {
                                    audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                                }
                            }
                            ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                            readInfo = true;
                        }
                        initDownloaded = true;
                    }
                }

                bool allHasDatetime = segments.All(s => s.DateTime != null);
                if (!SamePathDic.TryGetValue(task.Id, out bool value))
                {
                    IEnumerable<string> allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    bool allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    value = allSamePath;
                    SamePathDic[task.Id] = value;
                }

                // 下载第一个分片
                if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
                {
                    MediaSegment seg = segments.First();
                    segments = segments.Skip(1);
                    // 获取文件名
                    string filename = GetSegmentName(seg, allHasDatetime, value);
                    long index = seg.Index;
                    string path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
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
                        // 从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        // 实时解密
                        if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                        {
                            string enc = result.ActualFilePath;
                            string dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
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
                            lock (lockObj)
                            {
                                if (audioStart == null)
                                {
                                    audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                                }
                            }
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
                    // 获取文件名
                    string filename = GetSegmentName(seg, allHasDatetime, value);
                    long index = seg.Index;
                    string path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
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
                        bool dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                        if (dResult)
                        {
                            File.Delete(enc);
                            result.ActualFilePath = dec;
                        }
                    }
                });

                // 自动修复VTT raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
                {
                    // 排序字幕并修正时间戳
                    List<MediaSegment> keys = [.. FileDic.Keys.OrderBy(k => k.Index)];
                    foreach (MediaSegment? seg in keys)
                    {
                        string vttContent = await File.ReadAllTextAsync(FileDic[seg]!.ActualFilePath);
                        int waitCount = 0;
                        while (DownloaderConfig.MyOptions.LiveFixVttByAudio && audioStart == null && waitCount++ < 5)
                        {
                            await Task.Delay(1000);
                        }
                        long subOffset = audioStart != null ? (long)audioStart.Value.TotalMilliseconds : 0L;
                        WebVttSub vtt = WebVttSub.Parse(vttContent, subOffset);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
                        }
                        if (firstSub) { currentVtt = vtt; firstSub = false; }
                        else
                        {
                            _ = currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                // 自动修复VTT mp4字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == MediaType.SUBTITLES
                                                               && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
                {
                    DownloadResult? initFile = FileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init", StringComparison.OrdinalIgnoreCase));
                    byte[] iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                    (bool sawVtt, uint timescale) = MP4VttUtil.CheckInit(iniFileBytes);
                    if (sawVtt)
                    {
                        string[] mp4s = [.. FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase))];
                        if (firstSub)
                        {
                            currentVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                            firstSub = false;
                        }
                        else
                        {
                            WebVttSub vtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                            _ = currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                // 自动修复TTML raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
                {
                    List<MediaSegment> keys = [.. FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase)).Select(s => s.Key)];
                    if (firstSub)
                    {
                        if (baseTimestamp != 0)
                        {
                            double total = segmentsDuration;
                            baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                        }
                        bool first = true;
                        foreach (MediaSegment? seg in keys)
                        {
                            WebVttSub vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
                            }
                            if (first) { currentVtt = vtt; first = false; }
                            else
                            {
                                _ = currentVtt.AddCuesFromOne(vtt);
                            }
                        }
                        firstSub = false;
                    }
                    else
                    {
                        foreach (MediaSegment? seg in keys)
                        {
                            WebVttSub vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                            }
                            _ = currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                // 自动修复TTML mp4字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("m4s")
                    && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
                {
                    // sawTtml暂时不判断
                    // var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                    // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                    // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                    IEnumerable<MediaSegment> keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s", StringComparison.OrdinalIgnoreCase)).Select(s => s.Key);
                    if (firstSub)
                    {
                        if (baseTimestamp != 0)
                        {
                            double total = segmentsDuration;
                            baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                        }
                        bool first = true;
                        foreach (MediaSegment? seg in keys)
                        {
                            WebVttSub vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
                            }
                            if (first) { currentVtt = vtt; first = false; }
                            else
                            {
                                _ = currentVtt.AddCuesFromOne(vtt);
                            }
                        }
                        firstSub = false;
                    }
                    else
                    {
                        foreach (MediaSegment? seg in keys)
                        {
                            WebVttSub vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                            }
                            _ = currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                RecordedDurDic[task.Id] += (int)segmentsDuration;

                /*// 写出m3u8
                if (DownloaderConfig.MyOptions.LiveWriteHLS)
                {
                    var _saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                    var _saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                    await StreamingUtil.WriteStreamListAsync(FileDic, task.Id, 0, _saveName, _saveDir);
                }*/

                // 合并逻辑
                if (DownloaderConfig.MyOptions.LiveRealTimeMerge)
                {
                    // 合并
                    string outputExt = "." + streamSpec.Extension;
                    if (streamSpec.Extension == null)
                    {
                        outputExt = ".ts";
                    }
                    else if (streamSpec is { MediaType: MediaType.AUDIO, Extension: "m4s" })
                    {
                        outputExt = ".m4a";
                    }
                    else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension == "m4s")
                    {
                        outputExt = ".mp4";
                    }
                    else if (streamSpec.MediaType == MediaType.SUBTITLES)
                    {
                        outputExt = DownloaderConfig.MyOptions.SubtitleFormat == SubtitleFormat.SRT ? ".srt" : ".vtt";
                    }

                    string output = Path.Combine(saveDir, saveName + outputExt);

                    // 移除无效片段
                    IEnumerable<MediaSegment> badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
                    foreach (MediaSegment? badKey in badKeys)
                    {
                        _ = FileDic!.Remove(badKey, out _);
                    }

                    // 设置输出流
                    if (fileOutputStream == null)
                    {
                        // 检测目标文件是否存在
                        while (File.Exists(output))
                        {
                            Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(output = Path.ChangeExtension(output, $"copy" + Path.GetExtension(output)))}");
                        }

                        if (!DownloaderConfig.MyOptions.LivePipeMux || streamSpec.MediaType == MediaType.SUBTITLES)
                        {
                            fileOutputStream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                        }
                        else
                        {
                            // 创建管道
                            output = Path.ChangeExtension(output, ".ts");
                            string pipeName = $"RE_pipe_{Guid.NewGuid()}";
                            fileOutputStream = PipeUtil.CreatePipe(pipeName);
                            Logger.InfoMarkUp($"{ResString.NamedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                            PipeSteamNamesDic[task.Id] = pipeName;
                            if (PipeSteamNamesDic.Count == SelectedSteams.Count(x => x.MediaType != MediaType.SUBTITLES))
                            {
                                string[] names = [.. PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value)];
                                Logger.WarnMarkUp($"{ResString.NamedPipeMux} [deepskyblue1]{Path.GetFileName(output).EscapeMarkup()}[/]");
                                Task<bool> t = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, names, output);
                            }

                            // Windows only
                            if (OperatingSystem.IsWindows())
                            {
                                await (fileOutputStream as NamedPipeServerStream)!.WaitForConnectionAsync();
                            }
                        }
                    }

                    if (streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        DownloadResult? initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                        string[] files = [.. FileDic.Where(f => f.Key != streamSpec.Playlist!.MediaInit).OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath)];
                        if (initResult != null && mp4InitFile != "")
                        {
                            // shaka/ffmpeg实时解密不需要init文件用于合并，mp4decrpyt需要
                            if (string.IsNullOrEmpty(currentKID) || decryptEngine == DecryptEngine.MP4DECRYPT)
                            {
                                files = [initResult.ActualFilePath, .. files];
                            }
                        }
                        foreach (string? inputFilePath in files)
                        {
                            using FileStream inputStream = File.OpenRead(inputFilePath);
                            inputStream.CopyTo(fileOutputStream);
                        }
                        if (!DownloaderConfig.MyOptions.LiveKeepSegments)
                        {
                            foreach (string? inputFilePath in files.Where(x => !Path.GetFileName(x).StartsWith("_init", StringComparison.OrdinalIgnoreCase)))
                            {
                                File.Delete(inputFilePath);
                            }
                        }
                        FileDic.Clear();
                        if (initResult != null)
                        {
                            FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                        }
                    }
                    else
                    {
                        DownloadResult? initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                        string[] files = [.. FileDic.OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath)];
                        foreach (string? inputFilePath in files)
                        {
                            if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init", StringComparison.OrdinalIgnoreCase))
                            {
                                File.Delete(inputFilePath);
                            }
                        }

                        // 处理图形字幕
                        await SubtitleUtil.TryWriteImagePngsAsync(currentVtt, tmpDir);

                        string subText = currentVtt.ToVtt();
                        if (outputExt == ".srt")
                        {
                            subText = currentVtt.ToSrt();
                        }
                        byte[] subBytes = Encoding.UTF8.GetBytes(subText);
                        fileOutputStream.Position = 0;
                        fileOutputStream.Write(subBytes);
                        FileDic.Clear();
                        if (initResult != null)
                        {
                            FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                        }
                    }

                    // 刷新buffer
                    fileOutputStream?.Flush();
                }

                if (STOP_FLAG && source.Count == 0)
                {
                    break;
                }
            }

            if (fileOutputStream == null)
            {
                return true;
            }

            if (!DownloaderConfig.MyOptions.LivePipeMux)
            {
                // 记录所有文件信息
                OutputFiles.Add(new OutputFile()
                {
                    Index = task.Id,
                    FilePath = (fileOutputStream as FileStream)!.Name,
                    LangCode = streamSpec.Language,
                    Description = streamSpec.Name,
                    Mediainfos = mediaInfos,
                    MediaType = streamSpec.MediaType,
                });
            }
            fileOutputStream.Close();
            fileOutputStream.Dispose();

            return true;
        }

        private async Task PlayListProduceAsync(Dictionary<StreamSpec, ProgressTask> dic)
        {
            while (!STOP_FLAG)
            {
                if (WAIT_SEC == 0)
                {
                    continue;
                }

                // 1. MPD 所有URL相同 单次请求即可获得所有轨道的信息
                // 2. M3U8 所有URL不同 才需要多次请求
                await Parallel.ForEachAsync(dic, async (dic, _) =>
                {
                    StreamSpec streamSpec = dic.Key;
                    ProgressTask task = dic.Value;

                    // 达到上限时 不需要刷新了
                    if (RecordLimitReachedDic[task.Id])
                    {
                        return;
                    }

                    bool allHasDatetime = streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null);
                    if (!SamePathDic.TryGetValue(task.Id, out bool value))
                    {
                        IEnumerable<string> allName = streamSpec.Playlist!.MediaParts[0].MediaSegments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                        bool allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                        value = allSamePath;
                        SamePathDic[task.Id] = value;
                    }
                    // 过滤不需要下载的片段
                    FilterMediaSegments(streamSpec, task, allHasDatetime, value);
                    List<MediaSegment> newList = streamSpec.Playlist!.MediaParts[0].MediaSegments;
                    if (newList.Count > 0)
                    {
                        task.MaxValue += newList.Count;
                        // 推送给消费者
                        bool result = await BlockDic[task.Id].SendAsync(newList);
                        if (!result)
                        {
                            Logger.ErrorMarkUp($"Failed to send media segments to consumer for stream {streamSpec.ToShortShortString()}");
                        }
                        // 更新最新链接
                        LastFileNameDic[task.Id] = GetSegmentName(newList.Last(), allHasDatetime, value);
                        // 尝试更新时间戳
                        DateTime? dt = newList.Last().DateTime;
                        DateTimeDic[task.Id] = dt != null ? GetUnixTimestamp(dt.Value) : 0L;
                        // 累加已获取到的时长
                        RefreshedDurDic[task.Id] += (int)newList.Sum(s => s.Duration);
                    }

                    if (!STOP_FLAG && RefreshedDurDic[task.Id] >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds)
                    {
                        RecordLimitReachedDic[task.Id] = true;
                    }

                    // 检测时长限制
                    if (!STOP_FLAG && RecordLimitReachedDic.Values.All(x => x))
                    {
                        Logger.WarnMarkUp($"[darkorange3_1]{ResString.LiveLimitReached}[/]");
                        STOP_FLAG = true;
                        CancellationTokenSource.Cancel();
                    }
                });

                try
                {
                    // Logger.WarnMarkUp($"wait {waitSec}s");
                    if (!STOP_FLAG)
                    {
                        await Task.Delay(WAIT_SEC * 1000, CancellationTokenSource.Token);
                    }
                    // 刷新列表
                    if (!STOP_FLAG)
                    {
                        await StreamExtractor.RefreshPlayListAsync([.. dic.Keys]);
                    }
                }
                catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationTokenSource.Token)
                {
                    // 不需要做事
                }
                catch (Exception e)
                {
                    Logger.ErrorMarkUp(e);
                    STOP_FLAG = true;
                    // 停止所有Block
                    foreach (BufferBlock<List<MediaSegment>> target in BlockDic.Values)
                    {
                        target.Complete();
                    }
                }
            }
        }

        private void FilterMediaSegments(StreamSpec streamSpec, ProgressTask task, bool allHasDatetime, bool allSamePath)
        {
            if (string.IsNullOrEmpty(LastFileNameDic[task.Id]) && DateTimeDic[task.Id] == 0)
            {
                return;
            }

            int index = -1;
            long dateTime = DateTimeDic[task.Id];
            string lastName = LastFileNameDic[task.Id];

            // 优先使用dateTime判断
            index = dateTime != 0 && streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null)
                ? streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetUnixTimestamp(s.DateTime!.Value) == dateTime)
                : streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetSegmentName(s, allHasDatetime, allSamePath) == lastName);

            if (index > -1)
            {
                // 修正Index
                List<MediaSegment> list = [.. streamSpec.Playlist!.MediaParts[0].MediaSegments.Skip(index + 1)];
                if (list.Count > 0)
                {
                    long newMin = list.Min(s => s.Index);
                    long oldMax = MaxIndexDic[task.Id];
                    if (newMin < oldMax)
                    {
                        long offset = oldMax - newMin + 1;
                        foreach (MediaSegment? item in list)
                        {
                            item.Index += offset;
                        }
                    }
                    MaxIndexDic[task.Id] = list.Max(s => s.Index);
                }
                streamSpec.Playlist!.MediaParts[0].MediaSegments = list;
            }
        }

        public async Task<bool> StartRecordAsync()
        {
            int takeLastCount = DownloaderConfig.MyOptions.LiveTakeCount;
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();
            // 同步流
            FilterUtil.SyncStreams(SelectedSteams, takeLastCount);
            // 设置等待时间
            if (WAIT_SEC == 0)
            {
                WAIT_SEC = (int)(SelectedSteams.Min(s =>
                {
                    static double selector(MediaSegment s)
                    {
                        return s.Duration;
                    }
                    return s.Playlist!.MediaParts[0].MediaSegments.Sum(selector);
                }) / 2);
                WAIT_SEC -= 2; // 再提前两秒吧 留出冗余
                if (DownloaderConfig.MyOptions.LiveWaitTime != null)
                {
                    WAIT_SEC = DownloaderConfig.MyOptions.LiveWaitTime.Value;
                }

                if (WAIT_SEC <= 0)
                {
                    WAIT_SEC = 1;
                }

                Logger.WarnMarkUp($"set refresh interval to {WAIT_SEC} seconds");
            }
            // 如果没有选中音频 取消通过音频修复vtt时间轴
            if (SelectedSteams.All(x => x.MediaType != MediaType.AUDIO))
            {
                DownloaderConfig.MyOptions.LiveFixVttByAudio = false;
            }

            /*// 写出master
            if (DownloaderConfig.MyOptions.LiveWriteHLS)
            {
                var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                var saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                await StreamingUtil.WriteMasterListAsync(SelectedSteams, saveName, saveDir);
            }*/

            Progress progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
            progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

            // 进度条的列定义
            ProgressColumn[] progressColumns =
            [
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new RecordingDurationColumn(RecordedDurDic, RefreshedDurDic), // 时长显示
                new RecordingStatusColumn(),
                new PercentageColumn(),
                new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
                new SpinnerColumn(),
            ];
            if (DownloaderConfig.MyOptions.NoAnsiColor)
            {
                progressColumns = [.. progressColumns.SkipLast(1)];
            }
            _ = progress.Columns(progressColumns);

            await progress.StartAsync(async ctx =>
            {
                // 创建任务
                Dictionary<StreamSpec, ProgressTask> dic = SelectedSteams.Select(item =>
                {
                    ProgressTask task = ctx.AddTask(item.ToShortShortString(), autoStart: false, maxValue: 0);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                    // 限速设置
                    if (DownloaderConfig.MyOptions.MaxSpeed != null)
                    {
                        SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                    }
                    LastFileNameDic[task.Id] = "";
                    RecordLimitReachedDic[task.Id] = false;
                    DateTimeDic[task.Id] = 0L;
                    RecordedDurDic[task.Id] = 0;
                    RefreshedDurDic[task.Id] = 0;
                    MaxIndexDic[task.Id] = item.Playlist?.MediaParts[0].MediaSegments.LastOrDefault()?.Index ?? 0L; // 最大Index
                    BlockDic[task.Id] = new BufferBlock<List<MediaSegment>>();
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                DownloaderConfig.MyOptions.ConcurrentDownload = true;
                DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
                DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
                if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.RealTimeDecMessage}[/]");
                }

                TimeSpan? limit = DownloaderConfig.MyOptions.LiveRecordLimit;
                if (limit != TimeSpan.MaxValue)
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.LiveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
                }
                // 录制直播时，用户选了几个流就并发录几个
                ParallelOptions options = new()
                {
                    MaxDegreeOfParallelism = SelectedSteams.Count
                };
                // 开始刷新
                Task producerTask = PlayListProduceAsync(dic);
                await Task.Delay(200);
                // 并发下载
                await Parallel.ForEachAsync(dic, options, async (kp, _) =>
                {
                    ProgressTask task = kp.Value;
                    Task<bool> consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], BlockDic[task.Id]);
                    Results[kp.Key] = await consumerTask;
                });
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

        public void Dispose()
        {
            CancellationTokenSource.Dispose();
        }
    }
}