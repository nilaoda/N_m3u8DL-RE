using Mp4SubtitleParser;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Mp4;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System.Collections.Concurrent;
using System.Text;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.DownloadManager;

internal partial class SimpleDownloadManager
{
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    List<OutputFile> OutputFiles = [];
    // 预留路径贯穿合并和解密，不能以文件暂时不存在作为可复用的依据。
    private readonly HashSet<string> reservedOutputPaths = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private VodInitCache? initCache;
    private ConcurrentDictionary<long, double>? hlsMediaOrigins;
    private Task<bool>? hlsMediaReady;
    private bool hlsSubtitleOnlyCuts;
    private HashSet<string>? partLogMessages;

    // 同一逻辑轨道的各 part 共用日志集合；仍逐段探测和处理，只合并重复的界面提示。
    // 不同轨道各有自己的集合，新的编码/分辨率/声道等信息仍会显示。
    private void LogPartOnce(string key, Action write)
    {
        if (partLogMessages == null || partLogMessages.Add(key))
            write();
    }

    private void LogMediaInfo(List<Mediainfo> infos)
    {
        foreach (var info in infos)
            LogPartOnce("media:" + info.ToStringMarkUp(), () => Logger.InfoMarkUp(info.ToStringMarkUp()));
    }


    public SimpleDownloadManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor) 
    { 
        this.DownloaderConfig = downloaderConfig;
        this.SelectedSteams = selectedSteams;
        this.StreamExtractor = streamExtractor;
        Downloader = new SimpleDownloader(DownloaderConfig);
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

    private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
    {
        if (!DownloaderConfig.MyOptions.BinaryMerge && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.BinaryMerge = true;
            LogPartOnce("autoBinaryMerge2", () => Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge2}[/]"));
        }

        if (DownloaderConfig.MyOptions.MuxAfterDone && mediainfos.Any(m => m.DolbyVison))
        {
            DownloaderConfig.MyOptions.MuxAfterDone = false;
            LogPartOnce("autoBinaryMerge5", () => Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge5}[/]"));
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
                streamSpec.Extension = "vtt";
        }
    }

    private async Task<bool> DownloadStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, bool isPart = false)
    {
        // HLS 文本字幕可能使用连续 PTS，也可能在 discontinuity 重启。
        // 有共享媒体原点或纯字幕裁剪时按 part 映射；未裁剪的纯字幕沿用原有逐片修复流程。
        var rawHlsSubtitle = StreamExtractor.ExtractorType == ExtractorType.HLS &&
            streamSpec.MediaType == MediaType.SUBTITLES &&
            streamSpec.Playlist?.MediaParts.All(p => p.MediaInit == null) == true;
        var hasInit = streamSpec.Playlist?.MediaParts.Any(p => p.MediaInit != null) == true;
        // 单 init 的范围下载同样需要归零源时间；直接拼接会保留被跳过的起始空白。
        // FFmpeg/Packager 实时解密会输出完整容器，不能将这些容器按字节拼起来；
        // 请求合并时复用按 part 整体解密的路径，选项副本会关闭逐片解密。
        var needsPartProcessing = hasInit && (streamSpec.SkippedDuration > 0 ||
            !DownloaderConfig.MyOptions.SkipMerge && DownloaderConfig.MyOptions.MP4RealTimeDecryption &&
            DownloaderConfig.MyOptions.DecryptionEngine != DecryptEngine.MP4DECRYPT &&
            (DownloaderConfig.MyOptions.Keys is { Length: > 0 } || !string.IsNullOrEmpty(DownloaderConfig.MyOptions.KeyTextFile)) &&
            streamSpec.Playlist!.MediaParts.Any(p => p.MediaSegments.Any(s => s.IsEncrypted)));
        if (!isPart && streamSpec.Playlist is { } playlist &&
            (!rawHlsSubtitle || hlsMediaOrigins != null || hlsSubtitleOnlyCuts) && (needsPartProcessing || playlist.MediaParts.Count > 1 || playlist.MediaParts.Any(p => p.OutputDuration != null)))
            return await DownloadPartsAsync(streamSpec, task, speedContainer);
        if (!isPart)
            speedContainer.ResetVars();
        bool useAACFilter = false; // ffmpeg合并flag
        List<Mediainfo> mediaInfos = [];
        ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();

        var segments = streamSpec.Playlist?.MediaParts.SelectMany(m => m.MediaSegments);
        if (segments == null || !segments.Any()) return false;
        var mediaInit = streamSpec.Playlist!.MediaParts[0].MediaInit;
        var originalCount = segments.Count() + (mediaInit != null ? 1 : 0);
        var singleSegment = false;
        // MPD 整文件必须先下载完整字节再交给现有探测/解密/合并流程，
        // 字节范围已由清单明确指定的分片继续沿用原来的范围语义。
        var singleFile = StreamExtractor.ExtractorType == ExtractorType.MPEG_DASH && segments.Count() == 1 &&
            segments.First().StartRange == null && segments.First().StopRange == null &&
            Uri.TryCreate(segments.First().Url, UriKind.Absolute, out var singleUri) && singleUri.Scheme is "http" or "https";
        // 单分段尝试切片并行下载
        if (singleFile)
        {
            singleSegment = true;
        }
        else if (segments.Count() == 1)
        {
            var splitSegments = await LargeSingleFileSplitUtil.SplitUrlAsync(segments.First(), DownloaderConfig.Headers);
            if (splitSegments != null)
            {
                segments = splitSegments;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.singleFileSplitWarn}[/]");
                if (DownloaderConfig.MyOptions.MP4RealTimeDecryption)
                {
                    DownloaderConfig.MyOptions.MP4RealTimeDecryption = false;
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.singleFileRealtimeDecryptWarn}[/]");
                }
            }
            else
            {
                singleSegment = true;
            }
        }

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

        var decryptionBinaryPath = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
        var decryptEngine = DownloaderConfig.MyOptions.DecryptionEngine;
        var mp4InitFile = "";
        var currentKID = "";
        var readInfo = false; // 是否读取过
        var mp4Info = new ParsedMP4Info();

        // 用户自定义范围导致被跳过的时长 计算字幕偏移使用
        var skippedDur = streamSpec.SkippedDuration ?? 0d;

        Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

        // 创建文件夹
        if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
        if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

        var totalCount = segments.Count();
        if (mediaInit != null)
        {
            totalCount++;
        }

        if (isPart)
            task.MaxValue += totalCount - originalCount;
        else
        {
            task.MaxValue = totalCount;
            task.StartTask();
        }

        // 开始下载
        LogPartOnce("start", () => Logger.InfoMarkUp(ResString.startDownloading + streamSpec.ToShortString()));

        void CompleteSegment()
        {
            // 只有文件下载及校验成功后才能置满；init 下载期间仍按分片数量推进。
            if (speedContainer.SingleSegment && speedContainer.ResponseLength != null)
            {
                task.MaxValue = Math.Max(1, speedContainer.ResponseLength.Value);
                task.Value = task.MaxValue;
            }
            else
            {
                task.Increment(1);
            }
        }

        // 对于CENC，全部自动开启二进制合并
        if (!DownloaderConfig.MyOptions.BinaryMerge && totalCount >= 1 && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method == Common.Enum.EncryptMethod.CENC)
        {
            DownloaderConfig.MyOptions.BinaryMerge = true;
            LogPartOnce("autoBinaryMerge4", () => Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge4}[/]"));
        }

        // 下载init
        if (mediaInit != null)
        {
            // 对于fMP4，自动开启二进制合并
            if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
            {
                DownloaderConfig.MyOptions.BinaryMerge = true;
                LogPartOnce("autoBinaryMerge", () => Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]"));
            }

            var path = Path.Combine(tmpDir, "_init.mp4.tmp");
            var result = initCache == null
                ? await Downloader.DownloadSegmentAsync(mediaInit, path, speedContainer, headers)
                : await initCache.DownloadAsync(mediaInit, path, speedContainer,
                    cachePath => Downloader.DownloadSegmentAsync(mediaInit, cachePath, speedContainer, headers));
            FileDic[mediaInit] = result;
            if (result is not { Success: true })
            {
                throw new Exception("Download init file failed!");
            }
            mp4InitFile = result.ActualFilePath;
            CompleteSegment();

            // 读取mp4信息
            if (result is { Success: true }) 
            {
                mp4Info = MP4DecryptUtil.GetMP4Info(result.ActualFilePath);
                // MPD的cenc:default_KID优先
                if (mediaInit.EncryptInfo.KID != null)
                {
                    currentKID = mediaInit.EncryptInfo.KID;
                    Logger.WarnMarkUp($"[grey]KID (from MPD): {currentKID}[/]");
                }
                else
                {
                    currentKID = mp4Info.KID;
                }
                // try shaka packager, which can handle WebM
                if (string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER) {
                    currentKID = MP4DecryptUtil.ReadInitShaka(result.ActualFilePath, decryptionBinaryPath);
                }
                // 从文件读取KEY
                await SearchKeyAsync(currentKID);
                // 实时解密
                if ((mediaInit.IsEncrypted || !string.IsNullOrEmpty(currentKID)) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                {
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, isMultiDRM: mp4Info.isMultiDRM);
                    if (dResult)
                    {
                        FileDic[mediaInit]!.ActualFilePath = dec;
                    }
                    else if (isPart && decryptEngine == DecryptEngine.MP4DECRYPT && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                        return false;
                }
                // ffmpeg读取信息
                if (!readInfo)
                {
                    LogPartOnce("probe", () => Logger.WarnMarkUp(ResString.readingInfo));
                    mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result.ActualFilePath);
                    LogMediaInfo(mediaInfos);
                    ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                    readInfo = true;
                }
            }
        }

        // 计算填零个数
        var pad = "0".PadLeft(segments.Count().ToString().Length, '0');

        // 下载第一个分片
        if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
        {
            var seg = segments.First();
            segments = segments.Skip(1);

            var index = seg.Index;
            var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
            // 多 part 共用按段计数的父进度条，不能让其中一个整文件改写父任务的单位。
            speedContainer.SingleSegment = singleSegment && !isPart;
            var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers, singleFile);
            FileDic[seg] = result;
            if (result is not { Success: true })
            {
                throw new Exception("Download first segment failed!");
            }
            CompleteSegment();
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
                        else if (isPart && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                            return false;
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
                // try shaka packager, which can handle WebM
                if (string.IsNullOrEmpty(currentKID) &&  DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER) {
                    currentKID = MP4DecryptUtil.ReadInitShaka(result.ActualFilePath, decryptionBinaryPath);
                }
                // 从文件读取KEY
                await SearchKeyAsync(currentKID);
                // 实时解密
                if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                {
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    mp4Info = MP4DecryptUtil.GetMP4Info(enc);
                    var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile, isMultiDRM: mp4Info.isMultiDRM);
                    if (dResult)
                    {
                        File.Delete(enc);
                        result.ActualFilePath = dec;
                    }
                    else if (isPart && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                        return false;
                }
                if (!readInfo)
                {
                    // ffmpeg读取信息
                    LogPartOnce("probe", () => Logger.WarnMarkUp(ResString.readingInfo));
                    mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                    LogMediaInfo(mediaInfos);
                    ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                    readInfo = true;
                }
            }
        }

        // 开始下载
        var options = new ParallelOptions()
        {
            MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
        };
        speedContainer.SingleSegment = singleSegment && !isPart;
        await Parallel.ForEachAsync(segments, options, async (seg, _) =>
        {
            var index = seg.Index;
            var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
            var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers, singleFile);
            FileDic[seg] = result;
            if (result is { Success: true })
                CompleteSegment();
            // 实时解密
            if (seg.IsEncrypted && DownloaderConfig.MyOptions.MP4RealTimeDecryption && result is { Success: true } && !string.IsNullOrEmpty(currentKID)) 
            {
                var enc = result.ActualFilePath;
                var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                mp4Info = MP4DecryptUtil.GetMP4Info(enc);
                var dResult = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile, isMultiDRM: mp4Info.isMultiDRM);
                if (dResult)
                {
                    File.Delete(enc);
                    result.ActualFilePath = dec;
                }
                else if (isPart && DownloaderConfig.MyOptions.Keys is { Length: > 0 })
                    result.ActualContentLength = null;
            }
        });

        // 修改输出后缀
        var outputExt = "." + streamSpec.Extension;
        if (streamSpec.Extension == null) outputExt = ".ts";
        else if (streamSpec is { MediaType: MediaType.AUDIO, Extension: "m4s" or "mp4" }) outputExt = ".m4a";
        else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension is "m4s" or "mp4") outputExt = ".mp4";

        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == MediaType.SUBTITLES)
        {
            outputExt = DownloaderConfig.MyOptions.SubtitleFormat == Enum.SubtitleFormat.SRT ? ".srt" : ".vtt";
        }
        var output = Path.Combine(saveDir, saveName + outputExt);

        if (!string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, Keys.Length: > 0 } && mp4InitFile != "")
        {
            File.Delete(mp4InitFile);
            // shaka/ffmpeg实时解密不需要init文件用于合并
            if (decryptEngine != DecryptEngine.MP4DECRYPT)
            {
                FileDic!.Remove(mediaInit, out _);
            }
        }

        // 校验分片数量
        if (DownloaderConfig.MyOptions.CheckSegmentsCount && FileDic.Values.Any(s => s == null))
        {
            Logger.ErrorMarkUp(ResString.segmentCountCheckNotPass, totalCount, FileDic.Values.Count(s => s != null));
            return false;
        }

        // 分段拼接不能将缺片的 part 当成完整媒体推进时间轴，即使关闭了数量校验。
        if (isPart && FileDic.Values.Any(value => value is not { Success: true }))
            return false;

        // 移除无效片段
        var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
        foreach (var badKey in badKeys)
        {
            FileDic!.Remove(badKey, out _);
        }

        // 校验完整性
        if ((isPart || DownloaderConfig.CheckContentLength) && FileDic.Values.Any(a => a!.Success == false))
        {
            return false;
        }

        // 字幕分片可以与音视频并发下载；仅修正时间轴时才依赖媒体的源 PTS。
        // 媒体失败时结束等待并保留字幕输入，避免挂起或使用不完整的原点。
        if (streamSpec.MediaType == MediaType.SUBTITLES && hlsMediaReady != null && !await hlsMediaReady)
            return false;

        if (isPart && hlsMediaOrigins != null && streamSpec.MediaType != MediaType.SUBTITLES)
        {
            var part = streamSpec.Playlist!.MediaParts[0];
            var first = FileDic.Keys.Where(s => s.Index != -1).OrderBy(s => s.Index).First();
            var source = FileDic[first]!.ActualFilePath;
            string? probeFile = null;
            try
            {
                if (mediaInit != null)
                {
                    probeFile = Path.Combine(tmpDir, "_hls-clock.mp4");
                    MergeUtil.CombineMultipleFilesIntoSingleFile([FileDic[mediaInit]!.ActualFilePath, source], probeFile);
                    source = probeFile;
                }
                // 必须在 remux 前读取源 PTS；TS remux 可能重置时钟，不能用最终输出推算字幕原点。
                var info = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, source);
                var firstPts = info.FirstOrDefault(i => i.StartTime != null)?.StartTime?.TotalSeconds;
                if (firstPts == null)
                    throw new InvalidOperationException(ResString.hlsMediaOriginReadFailed);
                var origin = firstPts.Value - (part.MediaSegments[0].HlsTime ?? 0);
                if (streamSpec.MediaType is null or MediaType.VIDEO)
                    hlsMediaOrigins[part.DiscontinuitySequence ?? 0] = origin;
                else
                    hlsMediaOrigins.TryAdd(part.DiscontinuitySequence ?? 0, origin);
            }
            finally
            {
                if (probeFile != null)
                    File.Delete(probeFile);
            }
        }

        // 自动修复VTT raw字幕
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("vtt")) 
        {
            Logger.WarnMarkUp(ResString.fixingVTT);
            // 排序字幕并修正时间戳
            bool first = true;
            var finalVtt = new WebVttSub();
            var keys = FileDic.Keys.OrderBy(k => k.Index);
            foreach (var seg in keys)
            {
                var vttContent = File.ReadAllText(FileDic[seg]!.ActualFilePath);
                var vtt = WebVttSub.Parse(vttContent);
                var mapHlsSubtitle = isPart && (hlsMediaOrigins != null || hlsSubtitleOnlyCuts);
                if (mapHlsSubtitle && seg.HlsTime != null)
                {
                    var part = streamSpec.Playlist!.MediaParts[0];
                    if (hlsSubtitleOnlyCuts)
                        HlsSubtitleTimeline.NormalizeSubtitleOnly(vtt, vttContent, seg);
                    else
                    {
                        if (!hlsMediaOrigins!.TryGetValue(part.DiscontinuitySequence ?? 0, out var origin))
                            throw new InvalidOperationException(ResString.hlsSubtitleOriginMissing);
                        HlsSubtitleTimeline.Normalize(vtt, vttContent, origin, seg.HlsTime.Value);
                    }
                }
                // 手动计算MPEGTS
                if (!mapHlsSubtitle && finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                {
                    vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                }
                if (first) { finalVtt = vtt; first = false; }
                else finalVtt.AddCuesFromOne(vtt);
            }
            if (isPart && (hlsMediaOrigins != null || hlsSubtitleOnlyCuts))
                HlsSubtitleTimeline.ClipBeforeStart(finalVtt);
            // 写出字幕
            var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
            foreach (var item in files) File.Delete(item);
            FileDic.Clear();
            var index = 0;
            var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
            // 设置字幕偏移
            finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
            var subContentFixed = finalVtt.ToVtt();
            // 转换字幕格式
            if (DownloaderConfig.MyOptions.SubtitleFormat != Enum.SubtitleFormat.VTT)
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
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                                                       && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
        {
            var initFile = FileDic.Values.FirstOrDefault(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init"));
            var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
            var (sawVtt, timescale) = MP4VttUtil.CheckInit(iniFileBytes);
            if (sawVtt)
            {
                Logger.WarnMarkUp(ResString.fixingVTTmp4);
                var mp4s = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
                if (isPart && StreamExtractor.ExtractorType == ExtractorType.HLS && hlsMediaOrigins == null && mp4s.Length > 0)
                {
                    // HLS WVTT 的 cue 使用 tfdt 的源时间，可能从数百秒开始；
                    // 必须从首个媒体样本取原点，不能把首句对白当作本段开始。
                    streamSpec.Playlist!.MediaParts[0].OutputInpoint =
                        MP4VttUtil.ReadStartTime(File.ReadAllBytes(mp4s[0]), timescale);
                }
                var finalVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                // 写出字幕
                var firstKey = FileDic.Keys.First();
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                // 设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                var subContentFixed = finalVtt.ToVtt();
                // 转换字幕格式
                if (DownloaderConfig.MyOptions.SubtitleFormat != Enum.SubtitleFormat.VTT)
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
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("ttml"))
        {
            Logger.WarnMarkUp(ResString.fixingTTML);
            var first = true;
            var finalVtt = new WebVttSub();
            var keys = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Key);
            foreach (var seg in keys)
            {
                var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0);
                // 手动计算MPEGTS
                if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                {
                    vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                }
                if (first) { finalVtt = vtt; first = false; }
                else finalVtt.AddCuesFromOne(vtt);
            }
            // 写出字幕
            var firstKey = FileDic.Keys.First();
            var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();

            // 处理图形字幕
            await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

            var keepSegments = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReKeepImageSegments);
            if (keepSegments != "1")
                foreach (var item in files) File.Delete(item);
            FileDic.Clear();
            var index = 0;
            var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
            // 设置字幕偏移
            finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
            var subContentFixed = finalVtt.ToVtt();
            // 转换字幕格式
            if (DownloaderConfig.MyOptions.SubtitleFormat != Enum.SubtitleFormat.VTT)
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
        if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec is { MediaType: Common.Enum.MediaType.SUBTITLES, Extension: not null } && streamSpec.Extension.Contains("m4s")
            && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp")) 
        {
            Logger.WarnMarkUp(ResString.fixingTTMLmp4);
            // sawTtml暂时不判断
            // var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
            // var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
            // var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
            var first = true;
            var finalVtt = new WebVttSub();
            var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
            foreach (var seg in keys)
            {
                var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0);
                // 手动计算MPEGTS
                if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                {
                    vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                }
                if (first) { finalVtt = vtt; first = false; }
                else finalVtt.AddCuesFromOne(vtt);
            }

            // 写出字幕
            var firstKey = FileDic.Keys.First();
            var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();

            // 处理图形字幕
            await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

            var keepSegments = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReKeepImageSegments);
            if (keepSegments != "1")
                foreach (var item in files) File.Delete(item);
            FileDic.Clear();
            var index = 0;
            var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
            // 设置字幕偏移
            finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
            var subContentFixed = finalVtt.ToVtt();
            // 转换字幕格式
            if (DownloaderConfig.MyOptions.SubtitleFormat != Enum.SubtitleFormat.VTT)
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
                // 检测目标文件及已预留路径，使用智能重命名；只预留实际写入的文件。
                var finalOutput = OtherUtil.HandleFileCollision(output, streamSpec, reservedOutputPaths);
                if (finalOutput != output)
                {
                    Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(finalOutput)}");
                    output = finalOutput;
                }
                LogPartOnce("binary-merge", () => Logger.InfoMarkUp(ResString.binaryMerge));
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                MergeUtil.CombineMultipleFilesIntoSingleFile(files, output);
                mergeSuccess = true;
            }
            else
            {
                // ffmpeg合并
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                LogPartOnce("ffmpeg-merge", () => Logger.InfoMarkUp(ResString.ffmpegMerge));
                var ext = streamSpec.MediaType == MediaType.AUDIO ? "m4a" : "mp4";
                var ffOut = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + $".{ext}");
                // 检测目标文件及已预留路径，使用智能重命名；只预留转换扩展名后的文件。
                var finalFfOut = OtherUtil.HandleFileCollision(ffOut, streamSpec, reservedOutputPaths);
                if (finalFfOut != ffOut)
                {
                    Logger.WarnMarkUp($"{Path.GetFileName(ffOut)} => {Path.GetFileName(finalFfOut)}");
                    ffOut = finalFfOut;
                }
                // 兼容旧参数；只有直接 concat 协议受命令行长度和分片句柄数量限制。
                var concatMode = DownloaderConfig.MyOptions.EffectiveConcatMode;
                if (MergeUtil.ShouldPartialMerge(files.Length, concatMode))
                {
                    Logger.WarnMarkUp(ResString.partMerge);
                    files = MergeUtil.PartialCombineMultipleFiles(files);
                    FileDic.Clear();
                    foreach (var item in files)
                    {
                        FileDic[new MediaSegment() { Url = item }] = new DownloadResult()
                        {
                            ActualFilePath = item
                        };
                    }
                }
                mergeSuccess = MergeUtil.MergeByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, files, Path.ChangeExtension(ffOut, null), ext, useAACFilter, writeDate: !DownloaderConfig.MyOptions.NoDateInfo, concatMode: concatMode);
                if (mergeSuccess) output = ffOut;
            }
        }

        if (DownloaderConfig.MyOptions.SkipMerge)
            return true;
        if (!mergeSuccess || !File.Exists(output))
            return false;

        // 重新读取init信息
        if (mergeSuccess && totalCount >= 1 && string.IsNullOrEmpty(currentKID) && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method != Common.Enum.EncryptMethod.NONE)
        {
            // MPD的cenc:default_KID优先
            if (mediaInit?.EncryptInfo.KID != null)
            {
                currentKID = mediaInit.EncryptInfo.KID;
                Logger.WarnMarkUp($"[grey]KID (from MPD): {currentKID}[/]");
            }
            else
            {
                currentKID = MP4DecryptUtil.GetMP4Info(output).KID;
            }
            // try shaka packager, which can handle WebM
            if (string.IsNullOrEmpty(currentKID) &&  DownloaderConfig.MyOptions.DecryptionEngine == DecryptEngine.SHAKA_PACKAGER) {
                currentKID = MP4DecryptUtil.ReadInitShaka(output, decryptionBinaryPath);
            }
            // 从文件读取KEY
            await SearchKeyAsync(currentKID);
        }

        // 调用mp4decrypt解密
        if (mergeSuccess && File.Exists(output) && !string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions is { MP4RealTimeDecryption: false, Keys.Length: > 0 })
        {
            var enc = output;
            // 整轨解密使用独立临时文件，不能让固定的 _dec 名称覆盖另一条轨道的输出。
            var dec = Path.Combine(tmpDir, $"{Guid.NewGuid():N}{Path.GetExtension(enc)}");
            mp4Info = MP4DecryptUtil.GetMP4Info(enc);
            Logger.InfoMarkUp($"[grey]Decrypting using {decryptEngine}...[/]");
            try
            {
                var result = await MP4DecryptUtil.DecryptAsync(decryptEngine, decryptionBinaryPath, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, isMultiDRM: mp4Info.isMultiDRM, preserveTimestamp: isPart);
                if (!result)
                    return false;
                File.Move(dec, enc, true);
            }
            finally
            {
                File.Delete(dec);
            }
        }

        // FFmpeg 的 stream copy 会丢掉 CENC 描述而保留密文，退出码 0 不代表已解密。
        // 在任何跨 part remux 前检查实际输出，缺 key 或解密器未移除加密时保留下载。
        if (isPart && MP4DecryptUtil.HasEncryptedTracks(output))
        {
            Logger.Error(ResString.vodPartStillEncrypted);
            return false;
        }

        // 删除临时文件夹：合并及解密都成功后再清理，失败时保留可重试的输入。
        if (DownloaderConfig.MyOptions.DelAfterDone)
        {
            foreach (var file in FileDic.Values.Select(v => v!.ActualFilePath)) File.Delete(file);
            OtherUtil.SafeDeleteDir(tmpDir);
        }

        // 记录所有文件信息
        if (File.Exists(output))
        {
            lock (OutputFiles)
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
        var alignedHlsSubtitles = DownloaderConfig.MyOptions.AutoSubtitleFix && StreamExtractor.ExtractorType == ExtractorType.HLS &&
            SelectedSteams.Any(s => s.MediaType == MediaType.SUBTITLES) &&
            SelectedSteams.Any(s => s.MediaType != MediaType.SUBTITLES &&
                s.Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.HlsTime != null)));
        if (alignedHlsSubtitles)
            hlsMediaOrigins = new();
        // 没有音视频时用字幕自身的源位置映射显式裁剪；未裁剪的字幕沿用原流程。
        hlsSubtitleOnlyCuts = DownloaderConfig.MyOptions.AutoSubtitleFix && StreamExtractor.ExtractorType == ExtractorType.HLS &&
            SelectedSteams.All(s => s.MediaType == MediaType.SUBTITLES && s.Playlist!.MediaParts.All(p => p.MediaInit == null)) &&
            SelectedSteams.SelectMany(s => s.Playlist!.MediaParts).Any(p =>
                p.MediaSegments.FirstOrDefault()?.SourceTime is { } source && p.OutputStart is { } output &&
                (DownloaderConfig.MyOptions.VodDropParts != null || DownloaderConfig.MyOptions.CustomRange != null ||
                 DownloaderConfig.MyOptions.AdKeywords is { Length: > 0 } ||
                 Math.Abs(source - output - (p.MediaSegments[0].HlsTime ?? 0) + (p.OutputInpoint ?? 0)) > 0.001));
        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();
            
        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

        // 进度条的列定义
        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn() { Alignment = Justify.Left },
            new ProgressBarColumn(){ Width = 30 },
            new MyPercentageColumn(),
            new DownloadStatusColumn(SpeedContainerDic),
            new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
            new RemainingTimeColumn(),
            new SpinnerColumn(),
        };
        if (DownloaderConfig.MyOptions.NoAnsiColor)
        {
            progressColumns = progressColumns.SkipLast(1).ToArray();
        }
        progress.Columns(progressColumns);

        if (DownloaderConfig.MyOptions is { MP4RealTimeDecryption: true, DecryptionEngine: not DecryptEngine.SHAKA_PACKAGER, Keys.Length: > 0 })
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");

        await progress.StartAsync(async ctx =>
        {
            // 创建任务
            var dic = SelectedSteams.Select(item =>
            {
                var description = item.ToShortShortString();
                var task = ctx.AddTask(description, autoStart: false);
                SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                // 限速设置
                if (DownloaderConfig.MyOptions.MaxSpeed != null)
                {
                    SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                }
                return (item, task);
            }).ToDictionary(item => item.item, item => item.task);

            // 顺序模式仍先处理媒体；并发模式同时下载两类轨道，只让字幕修复等待媒体。
            List<KeyValuePair<StreamSpec, ProgressTask>>[] batches = alignedHlsSubtitles
                ? [dic.Where(kp => kp.Key.MediaType != MediaType.SUBTITLES).ToList(),
                    dic.Where(kp => kp.Key.MediaType == MediaType.SUBTITLES).ToList()]
                : [dic.ToList()];
            async Task DownloadBatchAsync(List<KeyValuePair<StreamSpec, ProgressTask>> batch)
            {
                if (!DownloaderConfig.MyOptions.ConcurrentDownload)
                {
                    foreach (var kp in batch)
                    {
                        var result = await DownloadStreamAsync(kp.Key, kp.Value, SpeedContainerDic[kp.Value.Id]);
                        Results[kp.Key] = result;
                        if (!result)
                            break;
                    }
                }
                else
                {
                    await Parallel.ForEachAsync(batch, async (kp, _) =>
                        Results[kp.Key] = await DownloadStreamAsync(kp.Key, kp.Value, SpeedContainerDic[kp.Value.Id]));
                }
            }
            if (alignedHlsSubtitles && DownloaderConfig.MyOptions.ConcurrentDownload)
            {
                var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                hlsMediaReady = ready.Task;
                async Task DownloadMediaAsync()
                {
                    var completed = false;
                    try
                    {
                        await DownloadBatchAsync(batches[0]);
                        completed = batches[0].All(kp => Results.TryGetValue(kp.Key, out var result) && result == true);
                    }
                    finally
                    {
                        ready.TrySetResult(completed);
                    }
                }
                await Task.WhenAll(DownloadMediaAsync(), DownloadBatchAsync(batches[1]));
            }
            else
            {
                foreach (var batch in batches)
                {
                    await DownloadBatchAsync(batch);
                    if (Results.Values.Any(v => v != true))
                        break;
                }
            }
        });

        var success = Results.Count == SelectedSteams.Count && Results.Values.All(v => v == true);

        // 删除临时文件夹
        if (DownloaderConfig.MyOptions is { SkipMerge: false, DelAfterDone: true } && success)
        {
            foreach (var item in StreamExtractor.RawFiles)
            {
                var file = Path.Combine(DownloaderConfig.DirPrefix, item.Key);
                if (File.Exists(file)) File.Delete(file);
            }
            OtherUtil.SafeDeleteDir(DownloaderConfig.DirPrefix, cleanMetadata: true);
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
