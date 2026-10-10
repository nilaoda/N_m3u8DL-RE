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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks.Dataflow;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.DownloadManager;

internal class SimpleLiveRecordManager2
{
    // 分片与对应 init 一起入队，清单刷新后消费者仍使用该批次的初始化信息。
    private sealed record LiveSegmentBatch(List<MediaSegment> Segments, MediaSegment? Init);

    // 网络失败后每秒重试，尽快恢复请求，减少分片滑出直播窗口。
    private const int NetworkRetryDelaySeconds = 1;
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    ConcurrentDictionary<int, string> PipeSteamNamesDic = new();
    List<OutputFile> OutputFiles = [];
    private readonly LiveRecordingCleanup recordingCleanup;
    private Task<bool>? pipeMuxTask;
    private readonly HashSet<string> reservedOutputPaths = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    DateTime? PublishDateTime;
    volatile bool STOP_FLAG = false;
    bool fatalError;
    long lastNewSegmentTimestamp;
    int WAIT_SEC = 0; // 刷新间隔
    ConcurrentDictionary<int, TimeSpan> RecordedDurDic = new(); // 已录制时长
    ConcurrentDictionary<int, TimeSpan> RefreshedDurDic = new(); // 已刷新出的时长
    ConcurrentDictionary<int, long> RecordingSizeDic = new(); // 已写入文件的大小
    ConcurrentDictionary<int, BufferBlock<LiveSegmentBatch>> BlockDic = new(); // 各流待录制的分片批次
    ConcurrentDictionary<int, bool> SamePathDic = new(); // 各流是否allSamePath
    ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); // 各流是否达到上限
    ConcurrentDictionary<int, bool> LiveEndDic = new(); // 各流是否已结束直播(出现ENDLIST)
    ConcurrentDictionary<int, LiveSegmentTracker> SegmentTrackers = new(); // 各流的去重边界与录制顺序
    ConcurrentDictionary<int, LiveSegmentNotFoundPolicy> NotFoundPolicies = new(); // 各流最新窗口与尾部 404 等待策略
    ConcurrentDictionary<int, (TimeSpan Request, TimeSpan Read)> RequestTimeouts = new(); // 各轨道的请求和无数据超时
    CancellationTokenSource CancellationTokenSource = new(); // 取消Wait
    CancellationTokenSource DownloadCancellationTokenSource = new(); // 取消网络恢复等待和分片下载
    List<Regex> AdKeywordRegexList = []; // 广告关键字正则（直播刷新时复用）

    private readonly Lock lockObj = new();
    private LiveCatchupWindow? catchupWindow;
    TimeSpan? audioStart = null;
    private readonly TaskCompletionSource<TimeSpan?> audioClockReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly HashSet<StreamSpec> pendingAudioClocks = [];

    public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
    {
        this.DownloaderConfig = downloaderConfig;
        recordingCleanup = new LiveRecordingCleanup(downloaderConfig.DirPrefix);
        Downloader = new SimpleDownloader(DownloaderConfig);
        PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
        StreamExtractor = streamExtractor;
        SelectedSteams = selectedSteams;
    }

    internal (string[] Names, string Output)? RegisterPipeStream(int taskId, string pipeName, string output, StreamSpec streamSpec)
    {
        // 管道轨道不落盘；全部就绪后只预留一次最终混流文件。
        lock (PipeSteamNamesDic)
        {
            if (!PipeSteamNamesDic.TryAdd(taskId, pipeName) ||
                PipeSteamNamesDic.Count != SelectedSteams.Count(x => x.MediaType != MediaType.SUBTITLES))
                return null;
            var finalOutput = OtherUtil.HandleFileCollision(output, streamSpec, reservedOutputPaths);
            var names = PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value).ToArray();
            return (names, finalOutput);
        }
    }

    private void ReportAudioClock(StreamSpec stream, TimeSpan? start)
    {
        lock (lockObj)
        {
            if (start != null)
            {
                audioStart ??= start;
                audioClockReady.TrySetResult(audioStart);
            }
            // 每条音频在首片探测或录制结束时报告一次；都没有 PTS 时明确使用源字幕时间。
            if (pendingAudioClocks.Remove(stream) && pendingAudioClocks.Count == 0)
                audioClockReady.TrySetResult(null);
        }
    }

    private void StopRecording(bool cancelDownloads = true)
    {
        STOP_FLAG = true;
        CancellationTokenSource.Cancel();
        // 正常结束和时长上限仅停止生产，最后一批已入队的分片仍须下载、合并。
        if (cancelDownloads)
            DownloadCancellationTokenSource.Cancel();
    }

    private async Task<DownloadResult?> DownloadLiveSegmentAsync(MediaSegment segment, string path,
        SpeedContainer speedContainer, Dictionary<string, string> headers, TimeSpan networkTimeout, int taskId, bool isInit = false)
    {
        var reconnecting = false;
        var notFoundFailures = 0;
        var notFoundStart = 0L;
        var publicationWait = TimeSpan.Zero;
        while (true)
        {
            try
            {
                var result = await Downloader.DownloadSegmentAsync(segment, path, speedContainer, headers,
                    cancellationToken: DownloadCancellationTokenSource.Token, throwOnFailure: true, networkTimeout: networkTimeout);
                if (reconnecting)
                    Logger.Info(ResString.liveNetworkRecovered);
                return result;
            }
            catch (HttpRequestException ex) when (!isInit && ex.StatusCode == HttpStatusCode.NotFound)
            {
                var policy = NotFoundPolicies[taskId];
                if (++notFoundFailures == 1)
                {
                    notFoundStart = Stopwatch.GetTimestamp();
                    publicationWait = policy.GetPublicationWait(segment);
                }
                if (!policy.ShouldRetry(segment, notFoundFailures, DownloaderConfig.MyOptions.DownloadRetryCount,
                    Stopwatch.GetElapsedTime(notFoundStart), publicationWait))
                {
                    Logger.Warn(ResString.liveSegmentUnavailable);
                    return null;
                }
                if (!reconnecting)
                    Logger.Warn(ResString.liveSegmentNotReady);
                reconnecting = true;
                await Task.Delay(TimeSpan.FromSeconds(NetworkRetryDelaySeconds), DownloadCancellationTokenSource.Token);
            }
            catch (HttpRequestException ex) when (!isInit && ex.StatusCode == HttpStatusCode.Gone)
            {
                // 410 表示服务端已移除媒体，保留后续分片并在录制结果中标记缺片。
                Logger.Warn(ResString.liveSegmentUnavailable);
                return null;
            }
            catch (Exception ex) when (!DownloadCancellationTokenSource.IsCancellationRequested && RetryUtil.IsTransientNetworkError(ex))
            {
                // 保留当前分片及原 init，网络恢复后重试；不要把临时断网误当成直播结束。
                if (!reconnecting)
                    Logger.Warn(ResString.liveNetworkRetry);
                reconnecting = true;
                await Task.Delay(TimeSpan.FromSeconds(NetworkRetryDelaySeconds), DownloadCancellationTokenSource.Token);
            }
        }
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

    private static bool SameInit(MediaSegment? first, MediaSegment? second) =>
        first?.Url == second?.Url && first?.StartRange == second?.StartRange &&
        first?.ExpectLength == second?.ExpectLength && first?.EncryptInfo.KID == second?.EncryptInfo.KID;

    private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, BufferBlock<LiveSegmentBatch> source)
    {
        var baseTimestamp = PublishDateTime == null ? 0L : (long)(PublishDateTime.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
        var decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
        var mediaInit = streamSpec.Playlist?.MediaParts.FirstOrDefault()?.MediaInit;
        var mp4InitFile = "";
        var currentKID = "";
        var readInfo = false; // 是否读取过
        bool useAACFilter = false; // ffmpeg合并flag
        bool initDownloaded = false; // 当前 init 是否已下载
        List<string> initFiles = []; // 当前 init 及其解密产物，成功写入输出后才允许清理
        var initIndex = 0; // init 切换时使用独立文件，保留已录制 Period 的初始化信息
        ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
        List<Mediainfo> mediaInfos = [];
        Stream? fileOutputStream = null;
        long mergedBytesWritten = 0;
        WebVttSub currentVtt = new(); // 字幕流始终维护一个实例
        bool recordingFailed = false;
        bool firstSub = true;
        bool CompleteRecording()
        {
            if (fileOutputStream != null && !DownloaderConfig.MyOptions.LivePipeMux)
            {
                lock (lockObj)
                {
                    OutputFiles.Add(new OutputFile
                    {
                        Index = task.Id,
                        FilePath = ((FileStream)fileOutputStream).Name,
                        LangCode = streamSpec.Language,
                        Description = streamSpec.Name,
                        Mediainfos = mediaInfos,
                        MediaType = streamSpec.MediaType,
                    });
                }
            }
            return !recordingFailed;
        }

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

        // 创建文件夹，并登记轨道目录供录制成功后检查是否为空。
        recordingCleanup.TrackDirectory(tmpDir);
        if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        try
        {
            while (true && await source.OutputAvailableAsync())
            {
                // 接收新片段，尽量合并使用相同 init 的待处理批次。
                // 有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
                // 合并相同 init 的已入队批次，初始化边界必须保留，不能借用刷新后的清单状态。
                var batch = await source.ReceiveAsync();
                IEnumerable<MediaSegment> segments = batch.Segments;
                while (source.TryReceive(next => SameInit(batch.Init, next.Init), out var next))
                    segments = segments.Concat(next.Segments);
                if (!segments.Any()) continue;
                // 每片时长四舍五入为 ticks 再累计；直接 FromSeconds 可能因浮点误差少一个 tick，导致多录一片。
                var segmentsDuration = TimeSpan.FromTicks(segments.Sum(s => (long)Math.Round(s.Duration * TimeSpan.TicksPerSecond)));
                Logger.DebugMarkUp(string.Join(",", segments.Select(sss => GetSegmentName(sss, false, false))));

                // 下载当前批次的 init；DASH 切换 init 时重新读取媒体信息和 KID。
                // 初始清单可能尚未发布 MAP，首片到来时取得该批次的 init；下载后保留原对象作字典键。
                if (StreamExtractor.ExtractorType == ExtractorType.MPEG_DASH && !SameInit(mediaInit, batch.Init))
                {
                    if (mediaInit != null)
                        FileDic.TryRemove(mediaInit, out _);
                    if (initDownloaded)
                        initIndex++;
                    mediaInit = batch.Init;
                    initDownloaded = false;
                    initFiles.Clear();
                    mp4InitFile = "";
                    currentKID = "";
                    readInfo = false;
                }
                else if (!initDownloaded)
                    mediaInit = batch.Init ?? mediaInit;
                if (!initDownloaded && mediaInit != null)
                {
                    task.MaxValue += 1;
                    // 对于fMP4，自动开启二进制合并
                    if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        DownloaderConfig.MyOptions.BinaryMerge = true;
                        Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]");
                    }

                    var path = Path.Combine(tmpDir, initIndex == 0 ? "_init.mp4.tmp" : $"_init_{initIndex}.mp4.tmp");
                    initFiles.Add(Path.ChangeExtension(path, null));
                    recordingCleanup.TrackFile(initFiles[^1]);
                    var result = await DownloadLiveSegmentAsync(mediaInit, path, speedContainer, headers, RequestTimeouts[task.Id].Read, task.Id, isInit: true);
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
                        if (mediaInit.EncryptInfo.KID != null)
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
                            initFiles.Add(dec);
                            recordingCleanup.TrackFile(dec);
                            var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                            if (dResult)
                            {
                                FileDic[mediaInit]!.ActualFilePath = dec;
                            }
                            else if (decryptEngine == DecryptEngine.MP4DECRYPT && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                                throw new InvalidOperationException(ResString.decryptionFailed);
                        }
                        // ffmpeg读取信息
                        if (!readInfo)
                        {
                            Logger.WarnMarkUp(ResString.readingInfo);
                            mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result.ActualFilePath);
                            mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
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
                var probeAudioClock = DownloaderConfig.MyOptions.LiveFixVttByAudio &&
                    streamSpec.MediaType == MediaType.AUDIO && audioStart == null && mediaInit != null;
                while ((!readInfo || probeAudioClock || StreamExtractor.ExtractorType == ExtractorType.MSS) && segments.Any())
                {
                    var seg = segments.First();
                    segments = segments.Skip(1);
                    // 获取文件名
                    var filename = LiveSegmentTracker.GetFileName(seg, GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]));
                    var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                    var result = await DownloadLiveSegmentAsync(seg, path, speedContainer, headers, RequestTimeouts[task.Id].Read, task.Id);
                    FileDic[seg] = result;
                    if (result is not { Success: true })
                    {
                        // 首片无法获取时继续找可用分片，不能跳过媒体探测或 MSS init 重建。
                        recordingFailed = true;
                        continue;
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
                                initFiles.Add(dec);
                                recordingCleanup.TrackFile(dec);
                                var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                                if (dResult)
                                {
                                    FileDic[mediaInit!]!.ActualFilePath = dec;
                                }
                                else if (decryptEngine == DecryptEngine.MP4DECRYPT && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                                    throw new InvalidOperationException(ResString.decryptionFailed);
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
                        if (probeAudioClock)
                        {
                            // fMP4 init 没有样本 PTS，须结合首个成功下载的分片读取源时钟。
                            // 外部解密工具可能重置 PTS，必须在调用工具前探测；加密样本无需解码。
                            var clockFile = Path.Combine(tmpDir, "_audio-clock.mp4");
                            try
                            {
                                MergeUtil.CombineMultipleFilesIntoSingleFile([mp4InitFile, result.ActualFilePath], clockFile);
                                var clockInfo = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, clockFile);
                                ReportAudioClock(streamSpec, clockInfo.FirstOrDefault(info => info.Type == "Audio")?.StartTime);
                            }
                            finally
                            {
                                File.Delete(clockFile);
                            }
                        }
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
                            else if (DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                                throw new InvalidOperationException(ResString.decryptionFailed);
                        }
                        if (!readInfo)
                        {
                            // ffmpeg读取信息
                            Logger.WarnMarkUp(ResString.readingInfo);
                            mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                            mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                            ReportAudioClock(streamSpec, mediaInfos.FirstOrDefault(info => info.Type == "Audio")?.StartTime);
                            ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                            readInfo = true;
                        }
                    }
                    AddRecordedFileSize(task.Id, result);
                    break;
                }

                // 开始下载
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
                };
                await Parallel.ForEachAsync(segments, options, async (seg, _) =>
                {
                    try
                    {
                        // 获取文件名
                        var filename = LiveSegmentTracker.GetFileName(seg, GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]));
                        var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                        var result = await DownloadLiveSegmentAsync(seg, path, speedContainer, headers, RequestTimeouts[task.Id].Read, task.Id);
                        FileDic[seg] = result;
                        if (result is { Success: true })
                            task.Increment(1);
                        else
                            recordingFailed = true;
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
                            else if (DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                                throw new InvalidOperationException(ResString.decryptionFailed);
                        }
                        AddRecordedFileSize(task.Id, result);
                    }
                    catch (Exception) when (!DownloadCancellationTokenSource.IsCancellationRequested)
                    {
                        // 并发下载会等待所有分片结束；任何致命错误都须立即取消其他分片的重试。
                        fatalError = true;
                        StopRecording();
                        throw;
                    }
                });

                var missingSegments = FileDic.Where(entry => entry.Key.RecordingIndex != null && entry.Value is not { Success: true })
                    .Select(entry => entry.Key).ToList();
                if (missingSegments.Count > 0)
                {
                    recordingFailed = true;
                    foreach (var missing in missingSegments)
                        FileDic.TryRemove(missing, out _);
                }

                // 自动修复VTT raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt"))
                {
                    // 排序字幕并修正时间戳
                    var keys = FileDic.Keys.OrderBy(GetRecordOrder).ToList();
                    // 音频首片可能仍在重试，不能先按源时钟写字幕、后续再切换为归零时钟。
                    // 音频探测/结束后仍无原点时保留源时间；停止录制可取消等待。
                    var origin = DownloaderConfig.MyOptions.LiveFixVttByAudio
                        ? await audioClockReady.Task.WaitAsync(DownloadCancellationTokenSource.Token) : audioStart;
                    foreach (var seg in keys)
                    {
                        var vttContent = await File.ReadAllTextAsync(FileDic[seg]!.ActualFilePath);
                        var subOffset = origin != null ? (long)origin.Value.TotalMilliseconds : 0L;
                        // 无 timestamp-map 的字幕可能已使用播放相对时间，沿用原来的偏移和逐片修复，不能强减广播原点。
                        var mapByAudio = DownloaderConfig.MyOptions.LiveFixVttByAudio && origin != null &&
                            HlsSubtitleTimeline.HasTimestampMap(vttContent);
                        var vtt = WebVttSub.Parse(vttContent, mapByAudio ? 0 : subOffset);
                        if (mapByAudio)
                        {
                            var segmentStart = RecordedDurDic[task.Id].TotalSeconds +
                                keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration);
                            HlsSubtitleTimeline.Normalize(vtt, vttContent, origin!.Value.TotalSeconds, segmentStart);
                            HlsSubtitleTimeline.ClipBeforeStart(vtt);
                        }
                        // 手动计算MPEGTS
                        if (!mapByAudio && currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = (long)(90000 * (RecordedDurDic[task.Id].TotalSeconds + keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration)));
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
                            baseTimestamp -= (long)segmentsDuration.TotalMilliseconds;
                        }
                        var first = true;
                        foreach (var seg in keys)
                        {
                            var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = (long)(90000 * keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration));
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
                                vtt.MpegtsTimestamp = (long)(90000 * (RecordedDurDic[task.Id].TotalSeconds + keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration)));
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
                            baseTimestamp -= (long)segmentsDuration.TotalMilliseconds;
                        }
                        var first = true;
                        foreach (var seg in keys)
                        {
                            var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0, baseTimestamp);
                            // 手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = (long)(90000 * keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration));
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
                                vtt.MpegtsTimestamp = (long)(90000 * (RecordedDurDic[task.Id].TotalSeconds + keys.Where(s => GetRecordOrder(s) < GetRecordOrder(seg)).Sum(s => s.Duration)));
                            }
                            currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                RecordedDurDic[task.Id] += segmentsDuration;

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
                        if (!DownloaderConfig.MyOptions.LivePipeMux || streamSpec.MediaType == MediaType.SUBTITLES)
                        {
                            // 检测目标文件及已预留路径，使用智能重命名
                            var finalOutput = OtherUtil.HandleFileCollision(output, streamSpec, reservedOutputPaths);
                            if (finalOutput != output)
                            {
                                Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(finalOutput)}");
                                output = finalOutput;
                            }
                            fileOutputStream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                        }
                        else
                        {
                            // 创建管道
                            output = Path.ChangeExtension(output, ".ts");
                            var pipeName = $"RE_pipe_{Guid.NewGuid()}";
                            fileOutputStream = PipeUtil.CreatePipe(pipeName);
                            Logger.InfoMarkUp($"{ResString.namedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                            var mux = RegisterPipeStream(task.Id, pipeName, output, streamSpec);
                            if (mux is { } ready)
                            {
                                Logger.WarnMarkUp($"{ResString.namedPipeMux} [deepskyblue1]{Path.GetFileName(ready.Output).EscapeMarkup()}[/]");
                                pipeMuxTask = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, ready.Names, ready.Output);
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
                        recordingCleanup.MarkMerged(initFiles);
                        RecordingSizeDic[task.Id] = mergedBytesWritten;
                    }
                }

                if (STOP_FLAG && source.Count == 0)
                    break;
            }

            return CompleteRecording();
        }
        catch (OperationCanceledException) when (DownloadCancellationTokenSource.IsCancellationRequested)
        {
            // 主动停止时仍登记已写出的输出，后续混流可正常处理完整的已录制部分。
            return CompleteRecording();
        }
        finally
        {
            fileOutputStream?.Dispose();
        }
    }

    private async Task WatchIdleAsync()
    {
        if (DownloaderConfig.MyOptions.LiveIdleTimeout is not { } idleSeconds)
            return;
        try
        {
            while (!DownloadCancellationTokenSource.IsCancellationRequested)
            {
                var remaining = TimeSpan.FromSeconds(idleSeconds) -
                    Stopwatch.GetElapsedTime(Interlocked.Read(ref lastNewSegmentTimestamp));
                if (remaining <= TimeSpan.Zero)
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{string.Format(ResString.liveIdleTimeoutReached, idleSeconds)}[/]");
                    StopRecording();
                    return;
                }
                // 独立监控空闲时间，清单请求、分片重试和最后一批收尾都不能阻塞停止条件。
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(remaining.TotalSeconds, 15)), DownloadCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException) when (DownloadCancellationTokenSource.IsCancellationRequested) { }
    }

    private async Task PlayListProduceAsync(Dictionary<StreamSpec, ProgressTask> dic)
    {
        try
        {
            await PlayListProduceCoreAsync(dic);
        }
        catch (Exception ex)
        {
            Logger.ErrorMarkUp(ex);
            fatalError = true;
            StopRecording();
        }
        finally
        {
            // 包括分片入队阶段的异常，所有退出路径都必须唤醒等待中的消费者。
            foreach (var target in BlockDic.Values)
                target.Complete();
        }
    }

    private async Task PlayListProduceCoreAsync(Dictionary<StreamSpec, ProgressTask> dic)
    {
        var idleTimeoutSeconds = DownloaderConfig.MyOptions.LiveIdleTimeout;
        var refreshDelaySeconds = idleTimeoutSeconds is { } timeout ? Math.Min(WAIT_SEC, timeout) : WAIT_SEC;
        Interlocked.Exchange(ref lastNewSegmentTimestamp, Stopwatch.GetTimestamp());
        var reconnecting = false;

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

                var parts = streamSpec.Playlist.MediaParts;
                var segments = parts.SelectMany(part => part.MediaSegments).ToList();
                // 空清单或去重后的空窗口沿用最近的超时，不能丢失已知的正常分片时长。
                if (streamSpec.Playlist.MediaParts.Any(part => part.MediaSegments.Any(segment => double.IsFinite(segment.Duration) && segment.Duration > 0)))
                    RequestTimeouts[task.Id] = LiveRequestTimeoutPolicy.GetTimeouts(streamSpec, DownloaderConfig.MyOptions);
                if (!SamePathDic.ContainsKey(task.Id))
                {
                    var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    SamePathDic[task.Id] = allSamePath;
                }
                NotFoundPolicies[task.Id].Update(segments, refreshDelaySeconds);
                // 回看只保留指定节目时间范围内已完整发布的分片。
                var catchupEnded = false;
                if (catchupWindow != null)
                    segments = catchupWindow.Filter(streamSpec, out catchupEnded);
                // 过滤不需要下载的片段：在候选窗口中定位上一片，只保留尚未入队的分片。
                var newList = FilterMediaSegments(segments, task);
                // 过滤广告分片（在更新去重边界/时长记录之前剔除，避免污染统计）
                if (AdKeywordRegexList.Count > 0)
                {
                    newList = FilterUtil.CleanAdSegments(newList, AdKeywordRegexList);
                }
                if (newList.Count > 0)
                {
                    catchupWindow?.Record(streamSpec, newList);
                    Interlocked.Exchange(ref lastNewSegmentTimestamp, Stopwatch.GetTimestamp());
                    task.MaxValue += newList.Count;
                    // 保留源序号，单独分配录制顺序用于文件名和合并排序。
                    SegmentTrackers[task.Id].Record(newList);
                    // 按 MediaPart 分批入队，保留每段对应的 init，消费者再合并可共用 init 的批次。
                    var newSegments = new HashSet<MediaSegment>(newList, ReferenceEqualityComparer.Instance);
                    foreach (var part in parts)
                    {
                        var pending = part.MediaSegments.Where(newSegments.Contains).ToList();
                        if (pending.Count > 0)
                            await BlockDic[task.Id].SendAsync(new LiveSegmentBatch(pending, part.MediaInit));
                    }
                    // 累加已获取到的时长
                    RefreshedDurDic[task.Id] += TimeSpan.FromTicks(newList.Sum(s => (long)Math.Round(s.Duration * TimeSpan.TicksPerSecond)));
                }

                if (!STOP_FLAG && (catchupWindow != null ? catchupEnded :
                    DownloaderConfig.MyOptions.LiveRecordLimit is { } limit && RefreshedDurDic[task.Id] >= limit))
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

            // 回看时字幕可能没有末尾 cue；音视频已到固定终点后，本轮字幕也已入队，可以一起收尾。
            if (catchupWindow?.End != null)
            {
                var mediaTasks = dic.Where(kp => kp.Key.MediaType != MediaType.SUBTITLES).Select(kp => kp.Value.Id).ToList();
                if (mediaTasks.Count > 0 && mediaTasks.All(id => RecordLimitReachedDic[id] || LiveEndDic[id]))
                    foreach (var kp in dic.Where(kp => kp.Key.MediaType == MediaType.SUBTITLES))
                        RecordLimitReachedDic[kp.Value.Id] = true;
            }

            // 所有轨道都已推送本轮分片后再判断停止，保证消费者能收尾混流。
            // 检测回看终点或录制时长限制；停止刷新后仍完成已入队分片的下载。
            if (!STOP_FLAG && RecordLimitReachedDic.Values.All(x => x))
            {
                var message = catchupWindow != null ? ResString.liveCatchupRangeReached : ResString.liveLimitReached;
                Logger.WarnMarkUp($"[darkorange3_1]{message}[/]");
                StopRecording(cancelDownloads: false);
            }

            // 检测直播结束 所有流都已结束(或达到上限)时优雅停止 让消费者收尾混流
            if (!STOP_FLAG && RecordLimitReachedDic.Keys.All(id => RecordLimitReachedDic[id] || LiveEndDic[id]))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveStreamEnded}[/]");
                StopRecording(cancelDownloads: false);
            }

            try
            {
                if (STOP_FLAG)
                    break;
                await Task.Delay(TimeSpan.FromSeconds(reconnecting ? NetworkRetryDelaySeconds : refreshDelaySeconds), CancellationTokenSource.Token);
                var activeStreams = dic.Keys
                    .Where(stream => !RecordLimitReachedDic[dic[stream].Id] && !LiveEndDic[dic[stream].Id]).ToList();
                var requestTimeout = activeStreams.Min(stream => RequestTimeouts[dic[stream].Id].Request);
                await StreamExtractor.RefreshPlayListAsync(activeStreams, CancellationTokenSource.Token, requestTimeout);
                if (reconnecting)
                    Logger.Info(ResString.liveNetworkRecovered);
                reconnecting = false;
            }
            catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
            {
                // 用户主动停止或其他轨道失败，交给消费者收尾。
            }
            catch (Exception e) when (RetryUtil.IsTransientNetworkError(e))
            {
                if (!reconnecting)
                    Logger.Warn(ResString.liveNetworkRetry);
                reconnecting = true;
            }
            catch (Exception e)
            {
                Logger.ErrorMarkUp(e);
                fatalError = true;
                StopRecording();
            }
        }
    }

    private List<MediaSegment> FilterMediaSegments(List<MediaSegment> segments, ProgressTask task)
    {
        // 只返回待下载的分片，不修改完整清单；后续按原 MediaPart 查找各分片的 init。
        return SegmentTrackers[task.Id].Filter(segments, StreamExtractor.ExtractorType == ExtractorType.HLS);
    }

    public async Task<bool> StartRecordAsync(CancellationToken cancellationToken = default)
    {
        ConsoleCancelEventHandler handler = (_, args) =>
        {
            // 第一次收尾，若外部工具仍阻塞，第二次 Ctrl+C 可直接退出。
            if (DownloadCancellationTokenSource.IsCancellationRequested)
                return;
            args.Cancel = true;
            StopRecording();
        };
        Console.CancelKeyPress += handler;
        using var registration = cancellationToken.Register(() => StopRecording());
        try
        {
            return await StartRecordCoreAsync();
        }
        finally
        {
            StopRecording();
            Console.CancelKeyPress -= handler;
        }
    }

    private async Task<bool> StartRecordCoreAsync()
    {
        var takeLastCount = DownloaderConfig.MyOptions.LiveTakeCount;
        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();
        // 同步流：回看使用固定节目时间，不能再裁成最新的 live-take-count 个分片。
        if (DownloaderConfig.MyOptions.LiveCatchup is { } catchup)
        {
            var now = DateTimeOffset.Now;
            var start = DownloaderConfig.MyOptions.LiveCatchupStart ?? catchup.Resolve(now);
            catchupWindow = new LiveCatchupWindow(start, DownloaderConfig.MyOptions.LiveRecordLimit);
            catchupWindow.Validate(SelectedSteams, now);
        }
        else
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
        pendingAudioClocks.UnionWith(SelectedSteams.Where(x => x.MediaType == MediaType.AUDIO));
        if (pendingAudioClocks.Count == 0)
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
                RecordedDurDic[task.Id] = TimeSpan.Zero;
                RefreshedDurDic[task.Id] = TimeSpan.Zero;
                RecordingSizeDic[task.Id] = 0;
                SegmentTrackers[task.Id] = new LiveSegmentTracker();
                NotFoundPolicies[task.Id] = new LiveSegmentNotFoundPolicy(StreamExtractor.ExtractorType == ExtractorType.HLS);
                RequestTimeouts[task.Id] = LiveRequestTimeoutPolicy.GetTimeouts(item, DownloaderConfig.MyOptions);
                BlockDic[task.Id] = new BufferBlock<LiveSegmentBatch>();
                return (item, task);
            }).ToDictionary(item => item.item, item => item.task);

            DownloaderConfig.MyOptions.ConcurrentDownload = true;
            DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
            DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
            if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");
            var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
            if (limit != TimeSpan.MaxValue)
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime(limit.Value)}[/]");
            // 录制直播时，用户选了几个流就并发录几个
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = SelectedSteams.Count
            };
            // 开始刷新
            var producerTask = PlayListProduceAsync(dic);
            var idleTask = WatchIdleAsync();
            await Task.Delay(200);
            // 并发下载；任一轨道失败时停止刷新，并等待所有任务退出，避免遗留后台请求。
            try
            {
                await Parallel.ForEachAsync(dic, options, async (kp, _) =>
                {
                    var task = kp.Value;
                    try
                    {
                        Results[kp.Key] = await RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], BlockDic[task.Id]);
                    }
                    catch (OperationCanceledException) when (DownloadCancellationTokenSource.IsCancellationRequested)
                    {
                        Results[kp.Key] = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.ErrorMarkUp(ex);
                        Results[kp.Key] = false;
                        StopRecording();
                    }
                    finally
                    {
                        ReportAudioClock(kp.Key, null);
                    }
                });
            }
            finally
            {
                StopRecording();
                await producerTask;
                await idleTask;
            }
        });

        var success = !fatalError && Results.Values.All(v => v == true);

        // 所有轨道关闭管道后等待 ffmpeg 收尾；输出失败时保留 init 供排查和恢复。
        if (pipeMuxTask != null)
        {
            try
            {
                var muxSuccess = await pipeMuxTask;
                if (!muxSuccess)
                    Logger.Error("Mux failed");
                success &= muxSuccess;
            }
            catch (Exception ex)
            {
                Logger.ErrorMarkUp(ex);
                success = false;
            }
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
            // 清理列表只包含本次录制参与混流的轨道，外部导入源始终保留。
            var recordedFiles = OutputFiles.Select(f => f.FilePath).ToArray();
            if (DownloaderConfig.MyOptions.MuxImports != null)
            {
                OutputFiles.AddRange(DownloaderConfig.MyOptions.MuxImports);
            }
            if (OutputFiles.Count == 0)
            {
                Logger.Warn(ResString.processingMuxNoInputs);
                CleanupRecording(success);
                return success;
            }
            OutputFiles.ForEach(f => Logger.WarnMarkUp($"[grey]{Path.GetFileName(f.FilePath).EscapeMarkup()}[/]"));
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var ext = OtherUtil.GetMuxExtension(DownloaderConfig.MyOptions.MuxOptions.MuxFormat);
            var dirName = Path.GetFileName(DownloaderConfig.DirPrefix);
            // 为临时 .MUX 和最终媒体扩展名共同预留空间。
            var outName = OtherUtil.GetSafeFileName(dirName, ".MUX" + ext);
            var outPath = Path.Combine(saveDir, Path.GetFileNameWithoutExtension(outName));
            Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}[/]");
            // 回看保留媒体的源 PTS，复用已有混流时间轴处理，共同归零媒体与广播字幕。
            using var timeline = catchupWindow != null && DownloaderConfig.MyOptions.AutoSubtitleFix
                ? await MuxSubtitleTimeline.CreateAsync(OutputFiles.Select(f => f.FilePath).ToArray(),
                    DownloaderConfig.MyOptions.FFmpegBinaryPath, CancellationToken.None) : null;
            var muxSuccess = await MediaProcessingProgress.RunAsync(ResString.processingMux, async processing =>
            {
                processing.Begin(ResString.processingMux, logStart: false);
                var exitCode = await MergeUtil.MuxInputsAsync(
                    DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge ? DownloaderConfig.MyOptions.MkvmergeBinaryPath! : DownloaderConfig.MyOptions.FFmpegBinaryPath!,
                    OutputFiles.ToArray(), outPath + ext, useMkvmerge: DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge,
                    downloadDefaults: true, dateinfo: !DownloaderConfig.MyOptions.NoDateInfo, timeline: timeline, progress: processing.Report);
                var result = exitCode == 0 || DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge && exitCode == 1;
                if (result)
                {
                    processing.Begin(ResString.processingFinishing);
                    processing.Report(new(Bytes: new FileInfo(outPath + ext).Length));
                }
                // 完成后删除本次录制的各轨道文件，保留外部导入源
                if (result)
                {
                    if (!DownloaderConfig.MyOptions.MuxOptions.KeepFiles)
                    {
                        Logger.WarnMarkUp("[grey]Cleaning files...[/]");
                        foreach (var file in recordedFiles)
                            File.Delete(file);
                    }
                }
                // 判断是否要改名
                var newPath = Path.ChangeExtension(outPath, ext);
                if (result && !File.Exists(newPath))
                {
                    Logger.WarnMarkUp($"Rename to [grey]{Path.GetFileName(newPath).EscapeMarkup()}[/]");
                    File.Move(outPath + ext, newPath);
                }
                return result;
            });
            success &= muxSuccess;
        }

        if (success && catchupWindow is { ActualStart: { } actualStart, ActualEnd: { } actualEnd })
            Logger.Info(ResString.liveCatchupActualRange, actualStart.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz"),
                actualEnd.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
        CleanupRecording(success);
        return success;
    }

    private void CleanupRecording(bool success)
    {
        // 录制和最终混流均成功后才清理；未实时合并或保留分片时不删除 init。
        if (!success || DownloaderConfig.MyOptions is not { SkipMerge: false, DelAfterDone: true })
            return;
        foreach (var file in DownloaderConfig.CreatedMetadataFiles)
            recordingCleanup.DeleteFile(file);
        if (DownloaderConfig.MyOptions is { LiveRealTimeMerge: true, LiveKeepSegments: false })
            recordingCleanup.Cleanup();
    }
}
