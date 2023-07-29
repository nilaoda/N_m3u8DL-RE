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
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class SimpleLiveRecordManager2
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        StreamExtractor StreamExtractor;
        List<StreamSpec> SelectedSteams;
        ConcurrentDictionary<int, string> PipeSteamNamesDic = new();
        List<OutputFile> OutputFiles = new();
        DateTime? PublishDateTime;
        bool STOP_FLAG = false;
        int WAIT_SEC = 0; //刷新间隔
        ConcurrentDictionary<int, int> RecordedDurDic = new(); //已录制时长
        ConcurrentDictionary<int, int> RefreshedDurDic = new(); //已刷新出的时长
        ConcurrentDictionary<int, BufferBlock<List<MediaSegment>>> BlockDic = new(); //各流的Block
        ConcurrentDictionary<int, bool> SamePathDic = new(); //各流是否allSamePath
        ConcurrentDictionary<int, bool> RecordLimitReachedDic = new(); //各流是否达到上限
        ConcurrentDictionary<int, string> LastFileNameDic = new(); //上次下载的文件名
        ConcurrentDictionary<int, long> MaxIndexDic = new(); //最大Index
        ConcurrentDictionary<int, long> DateTimeDic = new(); //上次下载的dateTime
        CancellationTokenSource CancellationTokenSource = new(); //取消Wait

        private readonly object lockObj = new object();
        TimeSpan? audioStart = null;

        public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            this.DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
            StreamExtractor = streamExtractor;
            SelectedSteams = selectedSteams;
        }

        //从文件读取KEY
        private async Task SearchKeyAsync(string? currentKID)
        {
            var _key = await MP4DecryptUtil.SearchKeyFromFileAsync(DownloaderConfig.MyOptions.KeyTextFile, currentKID);
            if (_key != null)
            {
                if (DownloaderConfig.MyOptions.Keys == null)
                    DownloaderConfig.MyOptions.Keys = new string[] { _key };
                else
                    DownloaderConfig.MyOptions.Keys = DownloaderConfig.MyOptions.Keys.Concat(new string[] { _key }).ToArray();
            }
        }

        /// <summary>
        /// 获取时间戳
        /// </summary>
        /// <param name="dateTime"></param>
        /// <returns></returns>
        private long GetUnixTimestamp(DateTime dateTime)
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
                name = GetUnixTimestamp(segment.DateTime!.Value).ToString();
            }
            else if (hls)
            {
                name = segment.Index.ToString();
            }

            return name;
        }

        private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
        {
            if (!DownloaderConfig.MyOptions.BinaryMerge && mediainfos.Any(m => m.DolbyVison == true))
            {
                DownloaderConfig.MyOptions.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge2}[/]");
            }

            if (DownloaderConfig.MyOptions.MuxAfterDone && mediainfos.Any(m => m.DolbyVison == true))
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

                if (streamSpec.Extension == null || streamSpec.Extension == "ts")
                    streamSpec.Extension = "vtt";
            }
        }

        private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, BufferBlock<List<MediaSegment>> source)
        {
            var baseTimestamp = PublishDateTime == null ? 0L : (long)(PublishDateTime.Value.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalMilliseconds;
            //mp4decrypt
            var mp4decrypt = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
            var mp4InitFile = "";
            var currentKID = "";
            var readInfo = false; //是否读取过
            bool useAACFilter = false; //ffmpeg合并flag
            bool initDownloaded = false; //是否下载过init文件
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
            List<Mediainfo> mediaInfos = new();
            Stream? fileOutputStream = null;
            WebVttSub currentVtt = new(); //字幕流始终维护一个实例
            bool firstSub = true;
            task.StartTask();

            var name = streamSpec.ToShortString();
            var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
            var dirName = $"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            var tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;
            var headers = DownloaderConfig.Headers;

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

            //创建文件夹
            if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            while (true && await source.OutputAvailableAsync())
            {
                //接收新片段 且总是拿全部未处理的片段
                //有时每次只有很少的片段，但是之前的片段下载慢，导致后面还没下载的片段都失效了
                //TryReceiveAll可以稍微缓解一下
                source.TryReceiveAll(out IList<List<MediaSegment>>? segmentsList);
                var segments = segmentsList!.SelectMany(s => s);
                if (segments == null || !segments.Any()) continue;
                var segmentsDuration = segments.Sum(s => s.Duration);
                Logger.DebugMarkUp(string.Join(",", segments.Select(sss => GetSegmentName(sss, false, false))));

                //下载init
                if (!initDownloaded && streamSpec.Playlist?.MediaInit != null) 
                {
                    task.MaxValue += 1;
                    //对于fMP4，自动开启二进制合并
                    if (!DownloaderConfig.MyOptions.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        DownloaderConfig.MyOptions.BinaryMerge = true;
                        Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]");
                    }

                    var path = Path.Combine(tmpDir, "_init.mp4.tmp");
                    var result = await Downloader.DownloadSegmentAsync(streamSpec.Playlist.MediaInit, path, speedContainer, headers);
                    FileDic[streamSpec.Playlist.MediaInit] = result;
                    if (result == null || !result.Success)
                    {
                        throw new Exception("Download init file failed!");
                    }
                    mp4InitFile = result.ActualFilePath;
                    task.Increment(1);

                    //读取mp4信息
                    if (result != null && result.Success)
                    {
                        currentKID = MP4DecryptUtil.ReadInit(result.ActualFilePath);
                        //从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        //实时解密
                        if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID) && StreamExtractor.ExtractorType != ExtractorType.MSS)
                        {
                            var enc = result.ActualFilePath;
                            var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.MyOptions.UseShakaPackager, mp4decrypt, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                            if (dResult)
                            {
                                FileDic[streamSpec.Playlist.MediaInit]!.ActualFilePath = dec;
                            }
                        }
                        //ffmpeg读取信息
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
                }

                var allHasDatetime = segments.All(s => s.DateTime != null);
                if (!SamePathDic.ContainsKey(task.Id))
                {
                    var allName = segments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                    var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                    SamePathDic[task.Id] = allSamePath;
                }

                //下载第一个分片
                if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
                {
                    var seg = segments.First();
                    segments = segments.Skip(1);
                    //获取文件名
                    var filename = GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]);
                    var index = seg.Index;
                    var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                    var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                    FileDic[seg] = result;
                    if (result == null || !result.Success)
                    {
                        throw new Exception("Download first segment failed!");
                    }
                    task.Increment(1);
                    if (result != null && result.Success)
                    {
                        //修复MSS init
                        if (StreamExtractor.ExtractorType == ExtractorType.MSS)
                        {
                            var processor = new MSSMoovProcessor(streamSpec);
                            var header = processor.GenHeader(File.ReadAllBytes(result.ActualFilePath));
                            await File.WriteAllBytesAsync(FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath, header);
                            if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                            {
                                //需要重新解密init
                                var enc = FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath;
                                var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                                var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.MyOptions.UseShakaPackager, mp4decrypt, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                                if (dResult)
                                {
                                    FileDic[streamSpec.Playlist!.MediaInit!]!.ActualFilePath = dec;
                                }
                            }
                        }
                        //读取init信息
                        if (string.IsNullOrEmpty(currentKID))
                        {
                            currentKID = MP4DecryptUtil.ReadInit(result.ActualFilePath);
                        }
                        //从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        //实时解密
                        if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
                        {
                            var enc = result.ActualFilePath;
                            var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                            var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.MyOptions.UseShakaPackager, mp4decrypt, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                            if (dResult)
                            {
                                File.Delete(enc);
                                result.ActualFilePath = dec;
                            }
                        }
                        if (!readInfo)
                        {
                            //ffmpeg读取信息
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
                }

                //开始下载
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = DownloaderConfig.MyOptions.ThreadCount
                };
                await Parallel.ForEachAsync(segments, options, async (seg, _) =>
                {
                    //获取文件名
                    var filename = GetSegmentName(seg, allHasDatetime, SamePathDic[task.Id]);
                    var index = seg.Index;
                    var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                    var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                    FileDic[seg] = result;
                    if (result != null && result.Success)
                        task.Increment(1);
                    //实时解密
                    if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && result != null && result.Success && !string.IsNullOrEmpty(currentKID))
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.MyOptions.UseShakaPackager, mp4decrypt, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID, mp4InitFile);
                        if (dResult)
                        {
                            File.Delete(enc);
                            result.ActualFilePath = dec;
                        }
                    }
                });

                //自动修复VTT raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("vtt"))
                {
                    //排序字幕并修正时间戳
                    var keys = FileDic.Keys.OrderBy(k => k.Index);
                    foreach (var seg in keys)
                    {
                        var vttContent = File.ReadAllText(FileDic[seg]!.ActualFilePath);
                        var waitCount = 0;
                        while (DownloaderConfig.MyOptions.LiveFixVttByAudio && audioStart == null && waitCount++ < 5)
                        {
                            await Task.Delay(1000);
                        }
                        var subOffset = audioStart != null ? (long)audioStart.Value.TotalMilliseconds : 0L;
                        var vtt = WebVttSub.Parse(vttContent, subOffset);
                        //手动计算MPEGTS
                        if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
                        }
                        if (firstSub) { currentVtt = vtt; firstSub = false; }
                        else currentVtt.AddCuesFromOne(vtt);
                    }
                }

                //自动修复VTT mp4字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Codecs != "stpp" && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s"))
                {
                    var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                    var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                    var (sawVtt, timescale) = MP4VttUtil.CheckInit(iniFileBytes);
                    if (sawVtt)
                    {
                        var mp4s = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
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

                //自动修复TTML raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("ttml"))
                {
                    var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
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
                            //手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
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
                            //手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                            }
                            currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                //自动修复TTML mp4字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s")
                    && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
                {
                    //sawTtml暂时不判断
                    //var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                    //var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                    //var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                    var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
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
                            //手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration);
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
                            //手动计算MPEGTS
                            if (currentVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                            {
                                vtt.MpegtsTimestamp = 90000 * (RecordedDurDic[task.Id] + (long)keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                            }
                            currentVtt.AddCuesFromOne(vtt);
                        }
                    }
                }

                RecordedDurDic[task.Id] += (int)segmentsDuration;

                /*//写出m3u8
                if (DownloaderConfig.MyOptions.LiveWriteHLS)
                {
                    var _saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                    var _saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                    await StreamingUtil.WriteStreamListAsync(FileDic, task.Id, 0, _saveName, _saveDir);
                }*/

                //合并逻辑
                if (DownloaderConfig.MyOptions.LiveRealTimeMerge)
                {
                    //合并
                    var outputExt = "." + streamSpec.Extension;
                    if (streamSpec.Extension == null) outputExt = ".ts";
                    else if (streamSpec.MediaType == MediaType.AUDIO && streamSpec.Extension == "m4s") outputExt = ".m4a";
                    else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension == "m4s") outputExt = ".mp4";
                    else if (streamSpec.MediaType == MediaType.SUBTITLES)
                    {
                        if (DownloaderConfig.MyOptions.SubtitleFormat == Enum.SubtitleFormat.SRT) outputExt = ".srt";
                        else outputExt = ".vtt";
                    }

                    var output = Path.Combine(saveDir, saveName + outputExt);

                    //移除无效片段
                    var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
                    foreach (var badKey in badKeys)
                    {
                        FileDic!.Remove(badKey, out _);
                    }

                    //设置输出流
                    if (fileOutputStream == null)
                    {
                        //检测目标文件是否存在
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
                            //创建管道
                            output = Path.ChangeExtension(output, ".ts");
                            var pipeName = $"RE_pipe_{Guid.NewGuid()}";
                            fileOutputStream = PipeUtil.CreatePipe(pipeName);
                            Logger.InfoMarkUp($"{ResString.namedPipeCreated} [cyan]{pipeName.EscapeMarkup()}[/]");
                            PipeSteamNamesDic[task.Id] = pipeName;
                            if (PipeSteamNamesDic.Count == SelectedSteams.Where(x => x.MediaType != MediaType.SUBTITLES).Count()) 
                            {
                                var names = PipeSteamNamesDic.OrderBy(i => i.Key).Select(k => k.Value).ToArray();
                                Logger.WarnMarkUp($"{ResString.namedPipeMux} [deepskyblue1]{Path.GetFileName(output).EscapeMarkup()}[/]");
                                var t = PipeUtil.StartPipeMuxAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, names, output);
                            }

                            //Windows only
                            if (OperatingSystem.IsWindows())
                                await (fileOutputStream as NamedPipeServerStream)!.WaitForConnectionAsync();
                        }
                    }

                    if (streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        var initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                        var files = FileDic.Where(f => f.Key != streamSpec.Playlist!.MediaInit).OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
                        if (initResult != null && mp4InitFile != "")
                        {
                            //shaka实时解密不需要init文件用于合并，mp4decrpyt需要
                            if (!DownloaderConfig.MyOptions.UseShakaPackager)
                            {
                                files = new string[] { initResult.ActualFilePath }.Concat(files).ToArray();
                            }
                        }
                        foreach (var inputFilePath in files)
                        {
                            using (var inputStream = File.OpenRead(inputFilePath))
                            {
                                inputStream.CopyTo(fileOutputStream);
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
                            FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                        }
                    }
                    else
                    {
                        var initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                        var files = FileDic.OrderBy(s => s.Key.Index).Select(f => f.Value).Select(v => v!.ActualFilePath).ToArray();
                        foreach (var inputFilePath in files)
                        {
                            if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init"))
                            {
                                File.Delete(inputFilePath);
                            }
                        }

                        //处理图形字幕
                        await SubtitleUtil.TryWriteImagePngsAsync(currentVtt, tmpDir);

                        var subText = currentVtt.ToVtt();
                        if (outputExt == ".srt")
                        {
                            subText = currentVtt.ToSrt();
                        }
                        var subBytes = Encoding.UTF8.GetBytes(subText);
                        fileOutputStream.Position = 0;
                        fileOutputStream.Write(subBytes);
                        FileDic.Clear();
                        if (initResult != null)
                        {
                            FileDic[streamSpec.Playlist!.MediaInit!] = initResult;
                        }
                    }

                    //刷新buffer
                    if (fileOutputStream != null)
                    {
                        fileOutputStream.Flush();
                    }
                }

                if (STOP_FLAG && source.Count == 0) 
                    break;
            }

            if (fileOutputStream != null)
            {
                if (!DownloaderConfig.MyOptions.LivePipeMux)
                {
                    //记录所有文件信息
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
            }

            return true;
        }

        private async Task PlayListProduceAsync(Dictionary<StreamSpec, ProgressTask> dic)
        {
            while (!STOP_FLAG)
            {
                if (WAIT_SEC != 0)
                {
                    //1. MPD 所有URL相同 单次请求即可获得所有轨道的信息
                    //2. M3U8 所有URL不同 才需要多次请求

                    await Parallel.ForEachAsync(dic, async (dic, _) =>
                    {
                        var streamSpec = dic.Key;
                        var task = dic.Value;

                        //达到上限时 不需要刷新了
                        if (RecordLimitReachedDic[task.Id])
                            return;

                        var allHasDatetime = streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null);
                        if (!SamePathDic.ContainsKey(task.Id))
                        {
                            var allName = streamSpec.Playlist!.MediaParts[0].MediaSegments.Select(s => OtherUtil.GetFileNameFromInput(s.Url, false));
                            var allSamePath = allName.Count() > 1 && allName.Distinct().Count() == 1;
                            SamePathDic[task.Id] = allSamePath;
                        }
                        //过滤不需要下载的片段
                        FilterMediaSegments(streamSpec, task, allHasDatetime, SamePathDic[task.Id]);
                        var newList = streamSpec.Playlist!.MediaParts[0].MediaSegments;
                        if (newList.Count > 0)
                        {
                            task.MaxValue += newList.Count;
                            //推送给消费者
                            await BlockDic[task.Id].SendAsync(newList);
                            //更新最新链接
                            LastFileNameDic[task.Id] = GetSegmentName(newList.Last(), allHasDatetime, SamePathDic[task.Id]);
                            //尝试更新时间戳
                            var dt = newList.Last().DateTime;
                            DateTimeDic[task.Id] = dt != null ? GetUnixTimestamp(dt.Value) : 0L;
                            //累加已获取到的时长
                            RefreshedDurDic[task.Id] += (int)newList.Sum(s => s.Duration);
                        }

                        if (!STOP_FLAG && RefreshedDurDic[task.Id] >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds)
                        {
                            RecordLimitReachedDic[task.Id] = true;
                        }

                        //检测时长限制
                        if (!STOP_FLAG && RecordLimitReachedDic.Values.All(x => x == true))
                        {
                            Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                            STOP_FLAG = true;
                            CancellationTokenSource.Cancel();
                        }
                    });

                    try
                    {
                        //Logger.WarnMarkUp($"wait {waitSec}s");
                        if (!STOP_FLAG) await Task.Delay(WAIT_SEC * 1000, CancellationTokenSource.Token);
                        //刷新列表
                        if (!STOP_FLAG) await StreamExtractor.RefreshPlayListAsync(dic.Keys.ToList());
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationTokenSource.Token)
                    {
                        //不需要做事
                    }
                    catch (Exception e)
                    {
                        Logger.ErrorMarkUp(e);
                        STOP_FLAG = true;
                        //停止所有Block
                        foreach (var target in BlockDic.Values)
                        {
                            target.Complete();
                        }
                    }
                }
            }
        }

        private void FilterMediaSegments(StreamSpec streamSpec, ProgressTask task, bool allHasDatetime, bool allSamePath)
        {
            if (string.IsNullOrEmpty(LastFileNameDic[task.Id]) && DateTimeDic[task.Id] == 0) return;

            var index = -1;
            var dateTime = DateTimeDic[task.Id];
            var lastName = LastFileNameDic[task.Id];

            //优先使用dateTime判断
            if (dateTime != 0 && streamSpec.Playlist!.MediaParts[0].MediaSegments.All(s => s.DateTime != null)) 
            {
                index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetUnixTimestamp(s.DateTime!.Value) == dateTime);
            }
            else
            {
                index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetSegmentName(s, allHasDatetime, allSamePath) == lastName);
            }

            if (index > -1)
            {
                //修正Index
                var list = streamSpec.Playlist!.MediaParts[0].MediaSegments.Skip(index + 1).ToList();
                if (list.Count > 0)
                {
                    var newMin = list.Min(s => s.Index);
                    var oldMax = MaxIndexDic[task.Id];
                    if (newMin < oldMax)
                    {
                        var offset = oldMax - newMin + 1;
                        foreach (var item in list)
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
            var takeLastCount = DownloaderConfig.MyOptions.LiveTakeCount;
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); //速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();
            //同步流
            FilterUtil.SyncStreams(SelectedSteams, takeLastCount);
            //设置等待时间
            if (WAIT_SEC == 0)
            {
                WAIT_SEC = (int)(SelectedSteams.Min(s => s.Playlist!.MediaParts[0].MediaSegments.Sum(s => s.Duration)) / 2);
                WAIT_SEC -= 2; //再提前两秒吧 留出冗余
                if (DownloaderConfig.MyOptions.LiveWaitTime != null)
                    WAIT_SEC = DownloaderConfig.MyOptions.LiveWaitTime.Value;
                if (WAIT_SEC <= 0) WAIT_SEC = 1;
                Logger.WarnMarkUp($"set refresh interval to {WAIT_SEC} seconds");
            }
            //如果没有选中音频 取消通过音频修复vtt时间轴
            if (!SelectedSteams.Any(x => x.MediaType == MediaType.AUDIO))
            {
                DownloaderConfig.MyOptions.LiveFixVttByAudio = false;
            }

            /*//写出master
            if (DownloaderConfig.MyOptions.LiveWriteHLS)
            {
                var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                var saveName = DownloaderConfig.MyOptions.SaveName ?? DateTime.Now.ToString("yyyyMMddHHmmss");
                await StreamingUtil.WriteMasterListAsync(SelectedSteams, saveName, saveDir);
            }*/

            var progress = AnsiConsole.Progress().AutoClear(true);

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new RecordingDurationColumn(RecordedDurDic, RefreshedDurDic), //时长显示
                new RecordingStatusColumn(),
                new PercentageColumn(),
                new DownloadSpeedColumn(SpeedContainerDic), //速度计算
                new SpinnerColumn(),
            });

            await progress.StartAsync(async ctx =>
            {
                //创建任务
                var dic = SelectedSteams.Select(item =>
                {
                    var task = ctx.AddTask(item.ToShortShortString(), autoStart: false, maxValue: 0);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); //速度计算
                    //限速设置
                    if (DownloaderConfig.MyOptions.MaxSpeed != null)
                    {
                        SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                    }
                    LastFileNameDic[task.Id] = "";
                    RecordLimitReachedDic[task.Id] = false;
                    DateTimeDic[task.Id] = 0L;
                    RecordedDurDic[task.Id] = 0;
                    RefreshedDurDic[task.Id] = 0;
                    MaxIndexDic[task.Id] = item.Playlist?.MediaParts[0].MediaSegments.LastOrDefault()?.Index ?? 0L; //最大Index
                    BlockDic[task.Id] = new BufferBlock<List<MediaSegment>>();
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                DownloaderConfig.MyOptions.ConcurrentDownload = true;
                DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
                DownloaderConfig.MyOptions.LiveRecordLimit = DownloaderConfig.MyOptions.LiveRecordLimit ?? TimeSpan.MaxValue;
                if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !DownloaderConfig.MyOptions.UseShakaPackager
                    && DownloaderConfig.MyOptions.Keys != null && DownloaderConfig.MyOptions.Keys.Length > 0)
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");
                var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
                if (limit != TimeSpan.MaxValue)
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
                //录制直播时，用户选了几个流就并发录几个
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = SelectedSteams.Count
                };
                //开始刷新
                var producerTask = PlayListProduceAsync(dic);
                await Task.Delay(200);
                //并发下载
                await Parallel.ForEachAsync(dic, options, async (kp, _) =>
                {
                    var task = kp.Value;
                    var consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], BlockDic[task.Id]);
                    Results[kp.Key] = await consumerTask;
                });
            });

            var success = Results.Values.All(v => v == true);

            //删除临时文件夹
            if (!DownloaderConfig.MyOptions.SkipMerge && DownloaderConfig.MyOptions.DelAfterDone && success)
            {
                foreach (var item in StreamExtractor.RawFiles)
                {
                    var file = Path.Combine(DownloaderConfig.DirPrefix, item.Key);
                    if (File.Exists(file)) File.Delete(file);
                }
                OtherUtil.SafeDeleteDir(DownloaderConfig.DirPrefix);
            }

            //混流
            if (success && DownloaderConfig.MyOptions.MuxAfterDone && OutputFiles.Count > 0)
            {
                OutputFiles = OutputFiles.OrderBy(o => o.Index).ToList();
                //是否跳过字幕
                if (DownloaderConfig.MyOptions.MuxOptions.SkipSubtitle)
                {
                    OutputFiles = OutputFiles.Where(o => o.MediaType != MediaType.SUBTITLES).ToList();
                }
                if (DownloaderConfig.MyOptions.MuxImports != null)
                {
                    OutputFiles.AddRange(DownloaderConfig.MyOptions.MuxImports);
                }
                OutputFiles.ForEach(f => Logger.WarnMarkUp($"[grey]{Path.GetFileName(f.FilePath).EscapeMarkup()}[/]"));
                var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
                var ext = DownloaderConfig.MyOptions.MuxOptions.MuxToMp4 ? ".mp4" : ".mkv";
                var dirName = Path.GetFileName(DownloaderConfig.DirPrefix);
                var outName = $"{dirName}.MUX";
                var outPath = Path.Combine(saveDir, outName);
                Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}{ext}[/]");
                var result = false;
                if (DownloaderConfig.MyOptions.MuxOptions.UseMkvmerge) result = MergeUtil.MuxInputsByMkvmerge(DownloaderConfig.MyOptions.MkvmergeBinaryPath!, OutputFiles.ToArray(), outPath);
                else result = MergeUtil.MuxInputsByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, OutputFiles.ToArray(), outPath, DownloaderConfig.MyOptions.MuxOptions.MuxToMp4, !DownloaderConfig.MyOptions.NoDateInfo);
                //完成后删除各轨道文件
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
                //判断是否要改名
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
}
