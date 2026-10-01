using Mp4SubtitleParser;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Mp4;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.DownloadManager;

internal class SimpleLiveRecordManager2
{
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    ConcurrentDictionary<int, string> PipeSteamNamesDic = new();
    List<OutputFile> OutputFiles = [];
    DateTime? PublishDateTime;
    bool STOP_FLAG = false;
    int WAIT_SEC = 0; // 刷新间隔
    ConcurrentDictionary<int, int> RecordedDurDic = new(); // 已录制时长
    ConcurrentDictionary<int, int> RefreshedDurDic = new(); // 已刷新出的时长
    ConcurrentDictionary<int, long> RecordingSizeDic = new(); // 已写入文件的大小
    ConcurrentDictionary<int, BufferBlock<List<MediaSegment>>> BlockDic = new(); // 各流的Block
    ConcurrentDictionary<int, bool> SamePathDic = new(); // 各流是否allSamePath
    ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); // 各流是否达到上限
    ConcurrentDictionary<int, bool> LiveEndDic = new(); // 各流是否已结束直播(出现ENDLIST)
    ConcurrentDictionary<int, LiveSegmentTracker> SegmentTrackers = new(); // 各流的去重边界与录制顺序
    CancellationTokenSource CancellationTokenSource = new(); // 取消Wait
    List<Regex> AdKeywordRegexList = []; // 广告关键字正则（直播刷新时复用）

    private readonly Lock lockObj = new();
    TimeSpan? audioStart = null;

    public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
    {
        this.DownloaderConfig = downloaderConfig;
        Downloader = new SimpleDownloader(DownloaderConfig);
        PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
        StreamExtractor = streamExtractor;
        SelectedSteams = selectedSteams;
    }

    // 从文件读取KEY
    private async Task SearchKeyAsync(string? currentKID)
    {
        var _key = await MP4DecryptUtil.SearchKeyFromFileAsync(DownloaderConfig.MyOptions.KeyTextFile, currentKID);
        if (_key != null)
        {
            if (DownloaderConfig.MyOptions.Keys == null)
                DownloaderConfig.MyOptions.Keys = [_key];
            else
                DownloaderConfig.MyOptions.Keys = [..DownloaderConfig.MyOptions.Keys, _key];
        }
    }

    /// <summary>
    /// 获取源分片名称，用于识别重叠片段，也是落盘文件名的一部分
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
            name = LiveSegmentTracker.GetUnixTimestamp(segment.DateTime!.Value).ToString();
        }
        else if (hls)
        {
            name = segment.Index.ToString();
        }

        // URL 衍生的分片名(尤其是 DASH 带超长查询串的场景, 如 YouTube)可能超过文件系统
        // 单个组件 255 字节的限制导致创建临时文件失败, 这里统一截断到安全长度。see #650
        return OtherUtil.TruncateFileName(name, 200);
    }

    private static long GetRecordOrder(MediaSegment segment) => segment.RecordingIndex ?? segment.Index;

    private void AddRecordedFileSize(int taskId, DownloadResult? result)
    {
        if (DownloaderConfig.MyOptions.LiveRealTimeMerge || result is not { Success: true }) return;
        var fileSize = new FileInfo(result.ActualFilePath).Length;
        RecordingSizeDic.AddOrUpdate(taskId, fileSize, (_, current) => current + fileSize);
    }

    private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
    {
        if (!DownloaderConfig.MyOptions.BinaryMerge && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.BinaryMerge = true;
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge2}[/]");
        }

        if (DownloaderConfig.MyOptions.MuxAfterDone && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.MuxAfterDone = false;
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge5}[/]");
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
                streamSpec.Extension = "vtt";
        }
    }

    private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, BufferBlock<List<MediaSegment>> source)
    {
        var baseTimestamp = PublishDateTime == null ? 0L : (long)(PublishDateTime.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
        var decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
        var mediaInit = streamSpec.Playlist?.MediaParts.FirstOrDefault()?.MediaInit;
        var mp4InitFile = "";
        var currentKID = "";
        var readInfo = false; // 是否读取过
        bool useAACFilter = false; // ffmpeg合并flag
        bool initDownloaded = false; // 是否下载过init文件
        ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
        List<Mediainfo> mediaInfos = [];
        Stream? fileOutputStream = null;
        long mergedBytesWritten = 0;
        WebVttSub currentVtt = new(); // 字幕流始终维护一个实例
        bool firstSub = true;
        task.StartTask();

        var name = streamSpec.ToShortString();
        var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
        var dirName = OtherUtil.GetSafeFileName($"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}");
        var tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
        var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;

        // Use SavePattern if provided, otherwise use SaveName or dirName
        var saveName = dirName;
        if (!string.IsNullOrWhiteSpace(DownloaderConfig.MyOptions.SavePattern))
        {
            saveName = OtherUtil.FormatSavePattern(DownloaderConfig.MyOptions.SavePattern, streamSpec, DownloaderConfig.MyOptions.SaveName, task.Id);
        }
        else if (DownloaderConfig.MyOptions.SaveName != null)
        {
            saveName = $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.');
        }
        var headers = DownloaderConfig.Headers;
        var decryptEngine = DownloaderConfig.MyOptions.DecryptionEngine;

        Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

        // 创建文件夹
        if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        while (true && await source.OutputAvailableAsync())
        {
            // 接收新片段 且总是拿全部未处理的片段
            // 有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
            // TryReceiveAll可以稍微缓解一下
            source.TryReceiveAll(out IList<List<MediaSegment>>? segmentsList);
            var segments = segmentsList!.SelectMany(s => s);
            if (segments == null || !segments.Any()) continue;
            var segmentsDuration = segments.Sum(s => s.Duration);
            Logger.DebugMarkUp(string.Join(",", segments.Select(sss => GetSegmentName(sss, false, false))));

            // 下载init
            // 初始清单可能尚未发布 MAP，首片到来时重新取得 init；下载后保留原对象作字典键。
            if (!initDownloaded)
                mediaInit = streamSpec.Playlist?.MediaParts.FirstOrDefault()?.MediaInit ?? mediaInit;
            if (!initDownloaded && mediaInit != null)
            {
                task.MaxValue += 1;
                // 对于fMP4，自动开启二进制合并
                if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                {
                    DownloaderConfig.MyOptions.BinaryMerge = true;
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]");
                }

                var path = Path.Combine(tmpDir, "_init.mp4.tmp");
                var result = await Downloader.DownloadSegmentAsync(mediaInit, path, speedContainer, headers);
                FileDic[mediaInit] = result;
                if (result is not { Success: true })
                {
                    throw new Exception("Download init file failed!");
                }
                mp4InitFile = result.ActualFilePath;
                task.Increment(1);

                // 读取mp4信息
                if (result is { Success: true })
                {
                    currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                    // MPD的cenc:default_KID优先
                    if (mediaInit?.EncryptInfo.KID != null)
                    {
                        currentKID = mediaInit.EncryptInfo.KID;
                        Logger.WarnMarkUp($"[grey]KID (from MPD): {currentKID}[/]");
                    }
                    // 从文件读取KEY
                    await SearchKeyAsync(currentKID);
                    // 实时解密
                    if ((mediaInit.IsEncrypted || !string.IsNullOrEmpty(currentKID)) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                        if (dResult)
                        {
                            FileDic[mediaInit]!.ActualFilePath = dec;
                        }
                    }
                    // ffmpeg读取信息
                    if (!readInfo)
                    {
                        Logger.WarnMarkUp(ResString.readingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        lock (lockObj)
                        {
                            if (audioStart == null) audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                        }
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
                    }
                    initDownloaded = true;
                }
                AddRecordedFileSize(task.Id, result);
            }

            var allHasDatetime = segments.All(s => s.DateTime != null);
            if (!SamePathDic.ContainsKey(task.Id))
            {
                var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                SamePathDic[task.Id] = allSamePath;
            }

            // 下载第一个分片
            if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
            {
                var seg = segments.First();
                segments = segments.Skip(1);
                // 获取文件名
                var filename = LiveSegmentTracker.GetFileName(seg, GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]));
                var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                if (result is not { Success: true })
                {
                    throw new Exception("Download first segment failed!");
                }
                task.Increment(1);
                if (result is { Success: true })
                {
                    // 修复MSS init
                    if (StreamExtractor.ExtractorType == ExtractorType.MSS)
                    {
                        var processor = new MSSMoovProcessor(streamSpec);
                        var header = processor.GenHeader(File.ReadAllBytes(result.ActualFilePath));
                        await File.WriteAllBytesAsync(FileDic[mediaInit!]!.ActualFilePath, header);
                        if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                        {
                            // 需要重新解密init
                            var enc = FileDic[mediaInit!]!.ActualFilePath;
                            var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                            if (dResult)
                            {
                                FileDic[mediaInit!]!.ActualFilePath = dec;
                            }
                        }
                    }
                    // 读取init信息
                    if (string.IsNullOrEmpty(currentKID))
                    {
                        // MPD的cenc:default_KID优先
                        if (mediaInit?.EncryptInfo.KID != null)
                        {
                            currentKID = mediaInit.EncryptInfo.KID;
                            Logger.WarnMarkUp($"[grey]KID (from MPD): {currentKID}[/]");
                        }
                        else
                        {
                            currentKID = MP4DecryptUtil.GetMP4Info(result.ActualFilePath).KID;
                        }
                    }
                    // 从文件读取KEY
                    await SearchKeyAsync(currentKID);
                    // 实时解密
                    if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                        if (dResult)
                        {
                            File.Delete(enc);
                            result.ActualFilePath = dec;
                        }
                    }
                    if (!readInfo)
                    {
                        // ffmpeg读取信息
                        Logger.WarnMarkUp(ResString.readingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        lock (lockObj)
                        {
                            if (audioStart == null) audioStart = mediaInfos.FirstOrDefault(x => x.Type == "Audio")?.StartTime;
                        }
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
                    }
                }
                AddRecordedFileSize(task.Id, result);
            }

            // 开始下载
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
            };
            await Parallel.ForEachAsync(segments, options, async (seg, _) =>
            {
                // 获取文件名
                var filename = LiveSegmentTracker.GetFileName(seg, GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]));
                var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                if (result is { Success: true })
                    task.Increment(1);
                // 实时解密
                if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && result is { Success: true } && !string.IsNullOrEmpty(currentKID))
                {
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                    if (dResult)
                    {
                        File.Delete(enc);
                        result.ActualFilePath = dec;
                    }
                }
                AddRecordedFileSize(task.Id, result);
            });

            // 自动修复VTT raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
            {
                // 排序字幕并修正时间戳
                var keys = FileDic.Keys.OrderBy(GetRecordOrder).ToList();
                foreach (var seg in keys)
                {
                    var vttContent = await File.ReadAllTextAsync(FileDic[seg]!.ActualFilePath);
                    var waitCount = 0;
                    while (DownloaderConfig.MyOptions.LiveFixVttByAudio && audioStart == null && waitCount++ < 5)
                    {
                        await Task.Delay(1000);
                    }
                    var subOffset = audioStart != null ? (long)audioStart.Value.TotalMilliseconds : 0L;
                    var vtt = WebVttSub.Parse(vttContent, subOffset);
                    // 手动计算MPEGTS
                    if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration);
                    }
                    if (firstSub) { currentVtt = vtt; firstSub = false; }
                    else currentVtt.AddCuesFromOne(vtt);
                }
            }

            // 自动修复VTT mp4字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                                                           && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
            {
                var initFile = FileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init"));
                var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                var (sawVtt, timescale) = MP4VttUtil.CheckInit(iniFileBytes);
                if (sawVtt)
                {
                    var mp4s = FileDic.OrderBy(s => GetRecordOrder(s.Key)).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
                    if (firstSub)
                    {
                        currentVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                        firstSub = false;
                    }
                    else
                    {
                        var vtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                        currentVtt.AddCuesFromOne(vtt);
                    }
                }
            }

            // 自动修复TTML raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
            {
                var keys = FileDic.OrderBy(s => GetRecordOrder(s.Key)).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key).ToList();
                if (firstSub)
                {
                    if (baseTimestamp != 0)
                    {
                        var total = segmentsDuration;
                        baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                    }
                    var first = true;
                    foreach (var seg in keys)
                    {
                        var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration);
                        }
                        if (first) { currentVtt = vtt; first = false; }
                        else currentVtt.AddCuesFromOne(vtt);
                    }
                    firstSub = false;
                }
                else
                {
                    foreach (var seg in keys)
                    {
                        var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration));
                        }
                        currentVtt.AddCuesFromOne(vtt);
                    }
                }
            }

            // 自动修复TTML mp4字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("m4s")
                && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
            {
                // sawTtml暂时不判断
                // var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                var keys = FileDic.OrderBy(s => GetRecordOrder(s.Key)).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
                if (firstSub)
                {
                    if (baseTimestamp != 0)
                    {
                        var total = segmentsDuration;
                        baseTimestamp -= (long)TimeSpan.FromSeconds(total).TotalMilliseconds;
                    }
                    var first = true;
                    foreach (var seg in keys)
                    {
                        var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration);
                        }
                        if (first) { currentVtt = vtt; first = false; }
                        else currentVtt.AddCuesFromOne(vtt);
                    }
                    firstSub = false;
                }
                else
                {
                    foreach (var seg in keys)
                    {
                        var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                        // 手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration));
                        }
                        currentVtt.AddCuesFromOne(vtt);
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
                var outputExt = "." + streamSpec.Extension;
                if (streamSpec.Extension == null) outputExt = ".ts";
                else if (streamSpec is { MediaType: MediaType.AUDIO, Extension: "m4s" }) outputExt = ".m4a";
                else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension == "m4s") outputExt = ".mp4";
                else if (streamSpec.MediaType == MediaType.SUBTITLES)
                {
                    outputExt = DownloaderConfig.MyOptions.SubtitleFormat == Enum.SubtitleFormat.SRT ? ".srt" : ".vtt";
                }

                var output = Path.Combine(saveDir, saveName + outputExt);

                // 移除无效片段
                var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
                foreach (var badKey in badKeys)
                {
                    FileDic!.Remove(badKey, out _);
                }

                // 设置输出流
                if (fileOutputStream == null)
                {
                    // 检测目标文件是否存在，使用智能重命名
                    var finalOutput = OtherUtil.HandleFileCollision(output, streamSpec);
                    if (finalOutput != output)
                    {
                        Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(finalOutput)}");
                        output = finalOutput;
                    }

                    if (!DownloaderConfig.MyOptions.LivePipeMux || streamSpec.MediaType == MediaType.SUBTITLES)
                    {
                        fileOutputStream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                    }
                    else 
                    {
                        // 创建管道
                        output = Path.ChangeExtension(output, ".ts");
                        var pipeName = $"RE_pipe_{Guid.NewGuid()}";
                        fileOutputStream = PipeUtil.CreatePipe(pipeName);
                        Logger.InfoMarkUp($"{ResString.namedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                        PipeSteamNamesDic[task.Id] = pipeName;
                        if (PipeSteamNamesDic.Count == SelectedSteams.Count(x => x.MediaType != MediaType.SUBTITLES)) 
                        {
                            var names = PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value).ToArray();
                            Logger.WarnMarkUp($"{ResString.namedPipeMux} [deepskyblue1]{Path.GetFileName(output).EscapeMarkup()}[/]");
                            var t = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, names, output);
                        }

                        // Windows only
                        if (OperatingSystem.IsWindows())
                            await (fileOutputStream as NamedPipeServerStream)!.WaitForConnectionAsync();
                    }
                }

                if (streamSpec.MediaType != MediaType.SUBTITLES)
                {
                    var initResult = mediaInit != null ? FileDic[mediaInit!]! : null;
                    var files = FileDic.Where(f => f.Key.RecordingIndex != null).OrderBy(s => GetRecordOrder(s.Key)).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
                    if (initResult != null && mp4InitFile != "")
                    {
                        // shaka/ffmpeg实时解密不需要init文件用于合并，mp4decrpyt需要
                        if (string.IsNullOrEmpty(currentKID) || decryptEngine == DecryptEngine.MP4DECRYPT)
                        {
                            files = [initResult.ActualFilePath, ..files];
                        }
                    }
                    foreach (var inputFilePath in files)
                    {
                        using (var inputStream = File.OpenRead(inputFilePath))
                        {
                            var startPosition = inputStream.Position;
                            inputStream.CopyTo(fileOutputStream);
                            // CopyTo 成功后，用输入流的位置差累计实际写出的字节数。
                            mergedBytesWritten += inputStream.Position - startPosition;
                        }
                    }
                    if (!DownloaderConfig.MyOptions.LiveKeepSegments)
                    {
                        foreach (var inputFilePath in files.Where(x => !Path.GetFileName(x).StartsWith("_init")))
                        {
                            File.Delete(inputFilePath);
                        }
                    }
                    FileDic.Clear();
                    if (initResult != null)
                    {
                        FileDic[mediaInit!] = initResult;
                    }
                }
                else
                {
                    var initResult = mediaInit != null ? FileDic[mediaInit!]! : null;
                    var files = FileDic.OrderBy(s => GetRecordOrder(s.Key)).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
                    foreach (var inputFilePath in files)
                    {
                        if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init"))
                        {
                            File.Delete(inputFilePath);
                        }
                    }

                    // 处理图形字幕
                    await SubtitleUtil.TryWriteImagePngsAsync(currentVtt, tmpDir);

                    var subText = currentVtt.ToVtt();
                    if (outputExt == ".srt")
                    {
                        subText = currentVtt.ToSrt();
                    }
                    var subBytes = Encoding.UTF8.GetBytes(subText);
                    fileOutputStream.Position = 0;
                    fileOutputStream.Write(subBytes);
                    fileOutputStream.SetLength(subBytes.Length);
                    mergedBytesWritten = subBytes.Length;
                    FileDic.Clear();
                    if (initResult != null)
                    {
                        FileDic[mediaInit!] = initResult;
                    }
                }

                // 刷新buffer
                if (fileOutputStream != null)
                {
                    fileOutputStream.Flush();
                    RecordingSizeDic[task.Id] = mergedBytesWritten;
                }
            }

            if (STOP_FLAG && source.Count == 0) 
                break;
        }

        if (fileOutputStream == null) return true;
        
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
        var idleTimeoutSeconds = DownloaderConfig.MyOptions.LiveIdleTimeout;
        var refreshDelaySeconds = idleTimeoutSeconds is { } timeout ? Math.Min(WAIT_SEC, timeout) : WAIT_SEC;
        long lastNewSegmentTimestamp = Stopwatch.GetTimestamp();

        while (!STOP_FLAG)
        {
            if (WAIT_SEC == 0) continue;
            
            // 1. MPD 所有URL相同 单次请求即可获得所有轨道的信息
            // 2. M3U8 所有URL不同 才需要多次请求
            await Parallel.ForEachAsync(dic, async (dic, _) =>
            {
                var streamSpec = dic.Key;
                var task = dic.Value;

                // 达到上限 或 该流直播已结束时 不需要刷新了
                if (RecordLimitReachedDic[task.Id] || LiveEndDic[task.Id])
                    return;

                // 最终列表可能只有 #EXT-X-ENDLIST，没有剩余分片。
                if (streamSpec.Playlist!.MediaParts.Count == 0)
                {
                    if (!streamSpec.Playlist.IsLive)
                        LiveEndDic[task.Id] = true;
                    return;
                }

                var allHasDatetime = streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null);
                if (!SamePathDic.ContainsKey(task.Id))
                {
                    var allName = streamSpec.Playlist!.MediaParts[0].MediaSegments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    SamePathDic[task.Id] = allSamePath;
                }
                // 过滤不需要下载的片段
                FilterMediaSegments(streamSpec, task, allHasDatetime, SamePathDic[task.Id]);
                var newList = streamSpec.Playlist!.MediaParts[0].MediaSegments;
                // 过滤广告分片（在更新去重边界/时长记录之前剔除，避免污染统计）
                if (AdKeywordRegexList.Count > 0)
                {
                    newList = FilterUtil.CleanAdSegments(newList, AdKeywordRegexList);
                    streamSpec.Playlist!.MediaParts[0].MediaSegments = newList;
                }
                if (newList.Count > 0)
                {
                    Interlocked.Exchange(ref lastNewSegmentTimestamp, Stopwatch.GetTimestamp());
                    task.MaxValue += newList.Count;
                    // 保留源序号，单独分配录制顺序用于文件名和合并排序。
                    SegmentTrackers[task.Id].Record(newList);
                    // 推送给消费者
                    await BlockDic[task.Id].SendAsync(newList);
                    // 累加已获取到的时长
                    RefreshedDurDic[task.Id] += (int)newList.Sum(s => s.Duration);
                }

                if (!STOP_FLAG && RefreshedDurDic[task.Id] >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds)
                {
                    RecordLimitReachedDic[task.Id] = true;
                }

                // 检测直播是否结束 (出现 #EXT-X-ENDLIST 后 HLSExtractor 会将 IsLive 置为 false)
                // 此处在上方推送完最后一批片段之后再标记 避免漏掉收尾片段
                if (!STOP_FLAG && streamSpec.Playlist!.IsLive == false)
                {
                    LiveEndDic[task.Id] = true;
                }
            });

            // 所有轨道都已推送本轮分片后再判断停止，保证消费者能收尾混流。
            // 检测时长限制
            if (!STOP_FLAG && RecordLimitReachedDic.Values.All(x => x))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                STOP_FLAG = true;
                CancellationTokenSource.Cancel();
            }

            // 检测直播结束 所有流都已结束(或达到上限)时优雅停止 让消费者收尾混流
            if (!STOP_FLAG && RecordLimitReachedDic.Keys.All(id => RecordLimitReachedDic[id] || LiveEndDic[id]))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveStreamEnded}[/]");
                STOP_FLAG = true;
                CancellationTokenSource.Cancel();
            }

            if (!STOP_FLAG && idleTimeoutSeconds is { } idleSeconds &&
                Stopwatch.GetElapsedTime(Interlocked.Read(ref lastNewSegmentTimestamp)) >= TimeSpan.FromSeconds(idleSeconds))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{string.Format(ResString.liveIdleTimeoutReached, idleSeconds)}[/]");
                STOP_FLAG = true;
                CancellationTokenSource.Cancel();
            }

            try
            {
                // Logger.WarnMarkUp($"wait {waitSec}s");
                if (!STOP_FLAG) await Task.Delay(refreshDelaySeconds * 1000, CancellationTokenSource.Token);
                // 刷新列表
                if (!STOP_FLAG) await StreamExtractor.RefreshPlayListAsync(dic.Keys.ToList());
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
                foreach (var target in BlockDic.Values)
                {
                    target.Complete();
                }
            }
        }

        // 循环结束(直播结束/达到上限/异常) 标记所有Block完成
        // 确保即使最后一次刷新没有新片段 消费者也能被唤醒并收尾混流
        foreach (var target in BlockDic.Values)
        {
            target.Complete();
        }
    }

    private void FilterMediaSegments(StreamSpec streamSpec, ProgressTask task, bool allHasDatetime, bool allSamePath)
    {
        var segments = streamSpec.Playlist!.MediaParts[0].MediaSegments;
        streamSpec.Playlist.MediaParts[0].MediaSegments = SegmentTrackers[task.Id].Filter(
            segments,
            StreamExtractor.ExtractorType == ExtractorType.HLS,
            segment => GetSegmentName(segment, allHasDatetime, allSamePath));
    }

    public async Task<bool> StartRecordAsync()
    {
        var takeLastCount = DownloaderConfig.MyOptions.LiveTakeCount;
        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();
        // 同步流
        FilterUtil.SyncStreams(SelectedSteams, takeLastCount);
        // 初始化广告关键字正则，仅在启动时记录一次（直播刷新时复用，避免每次刷新刷屏）
        AdKeywordRegexList = FilterUtil.ParseAdKeywords(DownloaderConfig.MyOptions.AdKeywords);
        foreach (var reg in AdKeywordRegexList)
        {
            Logger.InfoMarkUp($"{ResString.customAdKeywordsFound}[Cyan underline]{reg}[/]");
        }
        // 设置等待时间
        if (WAIT_SEC == 0)
        {
            WAIT_SEC = LiveRefreshInterval.GetSeconds(SelectedSteams, StreamExtractor.ExtractorType,
                DownloaderConfig.MyOptions.LiveWaitTime);
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

        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;
        ConcurrentDictionary<int, StreamSpec> taskStreams = new();
            
        // 进度条的列定义
        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn() { Alignment = Justify.Left },
            new RecordingDurationColumn(RecordedDurDic, RefreshedDurDic), // 时长显示
            new RecordingSizeColumn(
                RecordingSizeDic,
                () => DownloaderConfig.MyOptions.LiveRealTimeMerge && DownloaderConfig.MyOptions.LiveKeepSegments,
                taskId => DownloaderConfig.MyOptions.LiveRealTimeMerge &&
                    (DownloaderConfig.MyOptions.LivePipeMux ||
                     taskStreams.TryGetValue(taskId, out var stream) && stream.MediaType == MediaType.SUBTITLES)),
            new RecordingStatusColumn(),
            new PercentageColumn(),
            new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
            new SpinnerColumn(),
        };
        if (DownloaderConfig.MyOptions.NoAnsiColor)
        {
            progressColumns = progressColumns.SkipLast(1).ToArray();
        }
        progress.Columns(progressColumns);

        await progress.StartAsync(async ctx =>
        {
            // 创建任务
            var dic = SelectedSteams.Select(item =>
            {
                var task = ctx.AddTask(item.ToShortShortString(), autoStart: false, maxValue: 0);
                taskStreams[task.Id] = item;
                SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                // 限速设置
                if (DownloaderConfig.MyOptions.MaxSpeed != null)
                {
                    SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                }
                RecordLimitReachedDic[task.Id] = false;
                LiveEndDic[task.Id] = false;
                RecordedDurDic[task.Id] = 0;
                RefreshedDurDic[task.Id] = 0;
                RecordingSizeDic[task.Id] = 0;
                SegmentTrackers[task.Id] = new LiveSegmentTracker();
                BlockDic[task.Id] = new BufferBlock<List<MediaSegment>>();
                return (item, task);
            }).ToDictionary(item => item.item, item => item.task);

            DownloaderConfig.MyOptions.ConcurrentDownload = true;
            DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
            DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
            if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");
            var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
            if (limit != TimeSpan.MaxValue)
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
            // 录制直播时，用户选了几个流就并发录几个
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = SelectedSteams.Count
            };
            // 开始刷新
            var producerTask = PlayListProduceAsync(dic);
            await Task.Delay(200);
            // 并发下载
            await Parallel.ForEachAsync(dic, options, async (kp, _) =>
            {
                var task = kp.Value;
                var consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], BlockDic[task.Id]);
                Results[kp.Key] = await consumerTask;
            });
        });

        var success = Results.Values.All(v => v == true);

        // 删除临时文件夹
        if (DownloaderConfig.MyOptions is { SkipMerge: false, DelAfterDone: true } && success)
        {
            foreach (var item in StreamExtractor.RawFiles)
            {
                var file = Path.Combine(DownloaderConfig.DirPrefix, item.Key);
                if (File.Exists(file)) File.Delete(file);
            }
            OtherUtil.SafeDeleteDir(DownloaderConfig.DirPrefix);
        }

        // 混流
        if (success && DownloaderConfig.MyOptions.MuxAfterDone && OutputFiles.Count > 0)
        {
            OutputFiles = OutputFiles.OrderBy(o => o.Index).ToList();
            // 是否跳过字幕
            if (DownloaderConfig.MyOptions.MuxOptions!.SkipSubtitle)
            {
                OutputFiles = OutputFiles.Where(o => o.MediaType != MediaType.SUBTITLES).ToList();
            }
            if (DownloaderConfig.MyOptions.MuxImports != null)
            {
                OutputFiles.AddRange(DownloaderConfig.MyOptions.MuxImports);
            }
            OutputFiles.ForEach(f => Logger.WarnMarkUp($"[grey]{Path.GetFileName(f.FilePath).EscapeMarkup()}[/]"));
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var ext = OtherUtil.GetMuxExtension(DownloaderConfig.MyOptions.MuxOptions.MuxFormat);
            var dirName = Path.GetFileName(DownloaderConfig.DirPrefix);
            // 为临时 .MUX 和最终媒体扩展名共同预留空间。
            var outName = OtherUtil.GetSafeFileName(dirName, ".MUX" + ext);
            var outPath = Path.Combine(saveDir, Path.GetFileNameWithoutExtension(outName));
            Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}[/]");
            var result = false;
            if (DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge) result = MergeUtil.MuxInputsByMkvmerge(DownloaderConfig.MyOptions.MkvmergeBinaryPath!, OutputFiles.ToArray(), outPath);
            else result = MergeUtil.MuxInputsByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, OutputFiles.ToArray(), outPath, DownloaderConfig.MyOptions.MuxOptions.MuxFormat, !DownloaderConfig.MyOptions.NoDateInfo);
            // 完成后删除各轨道文件
            if (result)
            {
                if (!DownloaderConfig.MyOptions.MuxOptions.KeepFiles)
                {
                    Logger.WarnMarkUp("[grey]Cleaning files...[/]");
                    OutputFiles.ForEach(f => File.Delete(f.FilePath));
                    var tmpDir = DownloaderConfig.MyOptions.TmpDir ?? Environment.CurrentDirectory;
                    OtherUtil.SafeDeleteDir(tmpDir);
                }
            }
            else
            {
                success = false;
                Logger.ErrorMarkUp($"Mux failed");
            }
            // 判断是否要改名
            var newPath = Path.ChangeExtension(outPath, ext);
            if (result && !File.Exists(newPath))
            {
                Logger.WarnMarkUp($"Rename to [grey]{Path.GetFileName(newPath).EscapeMarkup()}[/]");
                File.Move(outPath + ext, newPath);
            }
        }

        return success;
    }
}
