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
using N_m3u8DL_RE.Util;
using NiL.JS;
using NiL.JS.BaseLibrary;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine.Parsing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;
using static System.Net.Mime.MediaTypeNames;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class SimpleLiveRecordManager2
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        StreamExtractor StreamExtractor;
        List<StreamSpec> SelectedSteams;
        DateTime NowDateTime;
        bool STOP_FLAG = false;
        int WAIT_SEC = 0; //刷新间隔
        ConcurrentDictionary<int, int> RecordingDurDic = new(); //已录制时长
        ConcurrentDictionary<string, string> LastUrlDic = new(); //上次下载的url
        CancellationTokenSource CancellationTokenSource = new(); //取消Wait

        public SimpleLiveRecordManager2(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            this.DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            NowDateTime = DateTime.Now;
            StreamExtractor = streamExtractor;
            SelectedSteams = selectedSteams;
        }

        private string? ReadInit(byte[] data)
        {
            var info = MP4InitUtil.ReadInit(data);
            if (info.Scheme != null) Logger.WarnMarkUp($"[grey]Type: {info.Scheme}[/]");
            if (info.PSSH != null) Logger.WarnMarkUp($"[grey]PSSH(WV): {info.PSSH}[/]");
            if (info.KID != null) Logger.WarnMarkUp($"[grey]KID: {info.KID}[/]");
            return info.KID;
        }

        private string? ReadInit(string output)
        {
            using (var fs = File.OpenRead(output))
            {
                var header = new byte[4096]; //4KB
                fs.Read(header);
                return ReadInit(header);
            }
        }

        //从文件读取KEY
        private async Task SearchKeyAsync(string? currentKID)
        {
            var _key = await MP4DecryptUtil.SearchKeyFromFile(DownloaderConfig.MyOptions.KeyTextFile, currentKID);
            if (_key != null)
            {
                if (DownloaderConfig.MyOptions.Keys == null)
                    DownloaderConfig.MyOptions.Keys = new string[] { _key };
                else
                    DownloaderConfig.MyOptions.Keys = DownloaderConfig.MyOptions.Keys.Concat(new string[] { _key }).ToArray();
            }
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

            if (mediainfos.All(m => m.Type == "Audio"))
            {
                streamSpec.MediaType = MediaType.AUDIO;
            }
            else if (mediainfos.All(m => m.Type == "Subtitle"))
            {
                streamSpec.MediaType = MediaType.SUBTITLES;
                if (streamSpec.Extension == null || streamSpec.Extension == "ts")
                    streamSpec.Extension = "vtt";
            }
        }

        private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer, ISourceBlock<List<MediaSegment>> source)
        {
            //mp4decrypt
            var mp4decrypt = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
            var mp4InitFile = "";
            var currentKID = "";
            var readInfo = false; //是否读取过
            bool useAACFilter = false; //ffmpeg合并flag
            bool initDownloaded = false; //是否下载过init文件
            bool hls = StreamExtractor.ExtractorType == ExtractorType.HLS;
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();
            List<Mediainfo> mediaInfos = new();
            FileStream? fileOutputStream = null;
            task.MaxValue = 0;
            task.StartTask();

            var name = streamSpec.ToShortString();
            var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
            var dirName = $"{DownloaderConfig.MyOptions.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{task.Id}_{streamSpec.GroupId}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            var tmpDir = Path.Combine(DownloaderConfig.MyOptions.TmpDir ?? Environment.CurrentDirectory, dirName);
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;
            var headers = DownloaderConfig.Headers;

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

            //创建文件夹
            if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            while (!STOP_FLAG && await source.OutputAvailableAsync())
            {
                //接收新片段
                var segments = (await source.ReceiveAsync()).AsEnumerable();

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
                        var data = File.ReadAllBytes(result.ActualFilePath);
                        currentKID = ReadInit(data);
                        //从文件读取KEY
                        await SearchKeyAsync(currentKID);
                        //实时解密
                        if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !string.IsNullOrEmpty(currentKID))
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
                            ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                            readInfo = true;
                        }
                        initDownloaded = true;
                    }
                }

                //计算填零个数
                var pad = "0".PadLeft(segments.Count().ToString().Length, '0');

                //下载第一个分片
                if (!readInfo)
                {
                    var seg = segments.First();
                    segments = segments.Skip(1);
                    //获取文件名
                    var filename = hls ? seg.Index.ToString(pad) : OtherUtil.GetFileNameFromInput(seg.Url, false);
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
                        //读取init信息
                        if (string.IsNullOrEmpty(currentKID))
                        {
                            currentKID = ReadInit(result.ActualFilePath);
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
                        //ffmpeg读取信息
                        Logger.WarnMarkUp(ResString.readingInfo);
                        mediaInfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.MyOptions.FFmpegBinaryPath!, result!.ActualFilePath);
                        mediaInfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
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
                    var filename = hls ? seg.Index.ToString(pad) : OtherUtil.GetFileNameFromInput(seg.Url, false);
                    var index = seg.Index;
                    var path = Path.Combine(tmpDir, filename + $".{streamSpec.Extension ?? "clip"}.tmp");
                    var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                    FileDic[seg] = result;
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

                RecordingDurDic[task.Id] += (int)segments.Sum(s => s.Duration);

                //自动修复VTT raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("vtt"))
                {
                    Logger.WarnMarkUp(ResString.fixingVTT);
                    //排序字幕并修正时间戳
                    bool first = true;
                    var finalVtt = new WebVttSub();
                    var keys = FileDic.Keys.OrderBy(k => k.Index);
                    foreach (var seg in keys)
                    {
                        var vttContent = File.ReadAllText(FileDic[seg]!.ActualFilePath);
                        var vtt = WebVttSub.Parse(vttContent);
                        //手动计算MPEGTS
                        if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                        {
                            vtt.MpegtsTimestamp = 90 * (long)(seg.Duration * 1000) * seg.Index;
                        }
                        if (first)
                        {
                            finalVtt = vtt;
                            first = false;
                        }
                        else
                        {
                            finalVtt.AddCuesFromOne(vtt);
                        }
                    }
                    //写出字幕
                    var files = FileDic.Values.Where(v => !Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                    foreach (var item in files) File.Delete(item);
                    FileDic.Clear();
                    var path = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(files.Last()) + ".vtt");
                    var subContentFixed = finalVtt.ToString();
                    await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                    FileDic[keys.First()] = new DownloadResult()
                    {
                        ActualContentLength = subContentFixed.Length,
                        ActualFilePath = path
                    };
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
                        Logger.WarnMarkUp(ResString.fixingVTTmp4);
                        var mp4s = FileDic.Values.Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).OrderBy(s => s).ToArray();
                        var finalVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                        //写出字幕
                        var firstKey = FileDic.Keys.First();
                        foreach (var item in mp4s) File.Delete(item);
                        FileDic.Clear();
                        var path = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(mp4s.Last()) + ".vtt");
                        var subContentFixed = finalVtt.ToString();
                        await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                        FileDic[firstKey] = new DownloadResult()
                        {
                            ActualContentLength = subContentFixed.Length,
                            ActualFilePath = path
                        };
                    }
                }

                //自动修复TTML raw字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("ttml"))
                {
                    Logger.WarnMarkUp(ResString.fixingTTML);
                    var mp4s = FileDic.Values.Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".ttml")).OrderBy(s => s).ToArray();
                    var finalVtt = MP4TtmlUtil.ExtractFromTTMLs(mp4s, 0);
                    //写出字幕
                    var firstKey = FileDic.Keys.First();
                    foreach (var item in mp4s) File.Delete(item);
                    FileDic.Clear();
                    var path = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(mp4s.Last()) + ".vtt");
                    var subContentFixed = finalVtt.ToString();
                    await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                    FileDic[firstKey] = new DownloadResult()
                    {
                        ActualContentLength = subContentFixed.Length,
                        ActualFilePath = path
                    };
                }

                //自动修复TTML mp4字幕
                if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                    && streamSpec.Extension != null && streamSpec.Extension.Contains("m4s")
                    && streamSpec.Codecs != null && streamSpec.Codecs.Contains("stpp"))
                {
                    Logger.WarnMarkUp(ResString.fixingTTMLmp4);
                    //sawTtml暂时不判断
                    //var initFile = FileDic.Values.Where(v => Path.GetFileName(v!.ActualFilePath).StartsWith("_init")).FirstOrDefault();
                    //var iniFileBytes = File.ReadAllBytes(initFile!.ActualFilePath);
                    //var sawTtml = MP4TtmlUtil.CheckInit(iniFileBytes);
                    var mp4s = FileDic.Values.Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).OrderBy(s => s).ToArray();
                    var finalVtt = MP4TtmlUtil.ExtractFromMp4s(mp4s, 0);
                    //写出字幕
                    var firstKey = FileDic.Keys.First();
                    foreach (var item in mp4s) File.Delete(item);
                    FileDic.Clear();
                    var path = Path.Combine(tmpDir, Path.GetFileNameWithoutExtension(mp4s.Last()) + ".vtt");
                    var subContentFixed = finalVtt.ToString();
                    await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                    FileDic[firstKey] = new DownloadResult()
                    {
                        ActualContentLength = subContentFixed.Length,
                        ActualFilePath = path
                    };
                }

                //合并逻辑
                if (DownloaderConfig.MyOptions.LiveRealTimeMerge)
                {
                    //合并
                    var outputExt = "." + streamSpec.Extension;
                    if (streamSpec.Extension == null) outputExt = ".ts";
                    else if (streamSpec.MediaType == MediaType.AUDIO && streamSpec.Extension == "m4s") outputExt = ".m4a";
                    else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension == "m4s") outputExt = ".mp4";
                    else if (streamSpec.MediaType == MediaType.SUBTITLES) outputExt = ".vtt";

                    var output = Path.Combine(saveDir, saveName + outputExt);

                    //移除无效片段
                    var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
                    foreach (var badKey in badKeys)
                    {
                        FileDic!.Remove(badKey, out _);
                    }

                    //检测目标文件是否存在
                    while (!readInfo && File.Exists(output))
                    {
                        Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(output = Path.ChangeExtension(output, $"copy" + Path.GetExtension(output)))}");
                    }

                    //设置输出流
                    if (fileOutputStream == null)
                    {
                        fileOutputStream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                    }

                    if (streamSpec.MediaType != MediaType.SUBTITLES)
                    {
                        var initResult = streamSpec.Playlist!.MediaInit != null ? FileDic[streamSpec.Playlist!.MediaInit!]! : null;
                        var files = FileDic.Where(f => f.Key != streamSpec.Playlist!.MediaInit).Select(f => f.Value).Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
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
                            if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init"))
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
                        var files = FileDic.Select(f => f.Value).Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                        foreach (var inputFilePath in files)
                        {
                            using (var inputStream = File.OpenRead(inputFilePath))
                            {
                                inputStream.CopyTo(fileOutputStream);
                            }
                            if (!DownloaderConfig.MyOptions.LiveKeepSegments && !Path.GetFileName(inputFilePath).StartsWith("_init"))
                            {
                                File.Delete(inputFilePath);
                            }
                        }
                    }

                    //刷新buffer
                    if (fileOutputStream != null)
                    {
                        fileOutputStream.Flush();
                    }
                }

                //检测时长限制
                if (!STOP_FLAG && RecordingDurDic.All(d => d.Value >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds))
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                    STOP_FLAG = true;
                    CancellationTokenSource.Cancel();
                }
            }

            if (fileOutputStream != null)
            {
                fileOutputStream.Close();
                fileOutputStream.Dispose();
            }

            return true;
        }

        private async Task PlayListProduceAsync(StreamSpec streamSpec, ProgressTask task, ITargetBlock<List<MediaSegment>> target)
        {
            while (!STOP_FLAG)
            {
                if (WAIT_SEC != 0)
                {
                    //过滤不需要下载的片段
                    FilterMediaSegments(streamSpec, LastUrlDic[streamSpec.ToShortString()]);
                    var newList = streamSpec.Playlist!.MediaParts[0].MediaSegments;
                    if (newList.Count > 0)
                    {
                        //推送给消费者
                        await target.SendAsync(newList);
                        //更新最新链接
                        LastUrlDic[streamSpec.ToShortString()] = GetPath(newList.Last().Url);
                        task.MaxValue += newList.Count;
                    }
                    try
                    {
                        //Logger.WarnMarkUp($"wait {waitSec}s");
                        if (!STOP_FLAG) await Task.Delay(WAIT_SEC * 1000, CancellationTokenSource.Token);
                        //刷新列表
                        if (!STOP_FLAG) await StreamExtractor.RefreshPlayListAsync(new List<StreamSpec>() { streamSpec });
                    }
                    catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationTokenSource.Token)
                    {
                        //不需要做事
                    }
                }
            }

            target.Complete();
        }

        private void FilterMediaSegments(StreamSpec streamSpec, string lastUrl)
        {
            if (string.IsNullOrEmpty(lastUrl)) return;

            var index = streamSpec.Playlist!.MediaParts[0].MediaSegments.FindIndex(s => GetPath(s.Url) == lastUrl);
            if (index > -1)
            {
                streamSpec.Playlist!.MediaParts[0].MediaSegments = streamSpec.Playlist!.MediaParts[0].MediaSegments.Skip(index + 1).ToList();
            }
        }

        private string GetPath(string url)
        {
            return new Uri(url).GetLeftPart(UriPartial.Path);
        }

        public async Task<bool> StartRecordAsync()
        {
            var takeLastCount = 15;
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); //速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();
            //取最后15个分片
            var minIndex = SelectedSteams.Max(s => s.Playlist!.MediaParts.Min(p => p.MediaSegments.Min(s => s.Index)));
            foreach (var item in SelectedSteams)
            {
                foreach (var part in item.Playlist!.MediaParts)
                {
                    part.MediaSegments = part.MediaSegments.Where(s => s.Index >= minIndex).TakeLast(takeLastCount).ToList();
                }
            }
            //初始化dic
            foreach (var item in SelectedSteams)
            {
                LastUrlDic[item.ToShortString()] = "";
            }
            //设置等待时间
            if (WAIT_SEC == 0)
            {
                WAIT_SEC = (int)(SelectedSteams.Min(s => s.Playlist!.MediaParts[0].MediaSegments.Sum(s => s.Duration)) / 2);
                Logger.WarnMarkUp($"set refresh interval to {WAIT_SEC} seconds");
            }

            var progress = AnsiConsole.Progress().AutoClear(true);

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new RecordingDurationColumn(RecordingDurDic), //时长显示
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
                    var task = ctx.AddTask(item.ToShortString(), autoStart: false);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); //速度计算
                    RecordingDurDic[task.Id] = 0;
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                DownloaderConfig.MyOptions.ConcurrentDownload = true;
                DownloaderConfig.MyOptions.MP4RealTimeDecryption = true;
                DownloaderConfig.MyOptions.LiveRecordLimit = DownloaderConfig.MyOptions.LiveRecordLimit ?? TimeSpan.MaxValue;
                var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
                if (limit != TimeSpan.MaxValue)
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
                //并发下载
                await Parallel.ForEachAsync(dic, async (kp, _) =>
                {
                    var task = kp.Value;
                    var list = new BufferBlock<List<MediaSegment>>();
                    var consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id], list);
                    await PlayListProduceAsync(kp.Key, task, list);
                    Results[kp.Key] = await consumerTask;
                });
            });

            var success = Results.Values.All(v => v == true);

            //混流
            if (success && DownloaderConfig.MyOptions.MuxAfterDone)
            {
                Logger.Error("Not supported yet!");
            }

            return success;
        }
    }
}
