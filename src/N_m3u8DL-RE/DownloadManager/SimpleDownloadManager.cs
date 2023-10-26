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

namespace N_m3u8DL_RE.DownloadManager
{
    internal class SimpleDownloadManager
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        StreamExtractor StreamExtractor;
        List<StreamSpec> SelectedSteams;
        List<OutputFile> OutputFiles = new();

        public SimpleDownloadManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor) 
        { 
            this.DownloaderConfig = downloaderConfig;
            this.SelectedSteams = selectedSteams;
            this.StreamExtractor = streamExtractor;
            Downloader = new SimpleDownloader(DownloaderConfig);
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


        private async Task<bool> DownloadStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer)
        {
            speedContainer.ResetVars();
            bool useAACFilter = false; //ffmpeg合并flag
            List<Mediainfo> mediaInfos = new();
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();

            var segments = streamSpec.Playlist?.MediaParts.SelectMany(m => m.MediaSegments);
            if (segments == null || !segments.Any()) return false;
            //单分段尝试切片并行下载
            if (segments.Count() == 1)
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
                else speedContainer.SingleSegment = true;
            }

            var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
            var dirName = $"{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            var tmpDir = Path.Combine(DownloaderConfig.DirPrefix, dirName);
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;
            var headers = DownloaderConfig.Headers;

            //mp4decrypt
            var mp4decrypt = DownloaderConfig.MyOptions.DecryptionBinaryPath!;
            var mp4InitFile = "";
            var currentKID = "";
            var readInfo = false; //是否读取过

            //用户自定义范围导致被跳过的时长 计算字幕偏移使用
            var skippedDur = streamSpec.SkippedDuration ?? 0d;

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}");

            //创建文件夹
            if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            var totalCount = segments.Count();
            if (streamSpec.Playlist?.MediaInit != null)
            {
                totalCount++;
            }

            task.MaxValue = totalCount;
            task.StartTask();

            //开始下载
            Logger.InfoMarkUp(ResString.startDownloading + streamSpec.ToShortString());

            //对于CENC，全部自动开启二进制合并
            if (!DownloaderConfig.MyOptions.BinaryMerge && totalCount >= 1 && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method == Common.Enum.EncryptMethod.CENC)
            {
                DownloaderConfig.MyOptions.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge4}[/]");
            }

            //下载init
            if (streamSpec.Playlist?.MediaInit != null)
            {
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
                        ChangeSpecInfo(streamSpec, mediaInfos, ref useAACFilter);
                        readInfo = true;
                    }
                }
            }

            //计算填零个数
            var pad = "0".PadLeft(segments.Count().ToString().Length, '0');

            //下载第一个分片
            if (!readInfo || StreamExtractor.ExtractorType == ExtractorType.MSS)
            {
                var seg = segments.First();
                segments = segments.Skip(1);

                var index = seg.Index;
                var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
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
                var index = seg.Index;
                var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
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

            //修改输出后缀
            var outputExt = "." + streamSpec.Extension;
            if (streamSpec.Extension == null) outputExt = ".ts";
            else if (streamSpec.MediaType == MediaType.AUDIO && (streamSpec.Extension == "m4s" || streamSpec.Extension == "mp4")) outputExt = ".m4a";
            else if (streamSpec.MediaType != MediaType.SUBTITLES && (streamSpec.Extension == "m4s" || streamSpec.Extension == "mp4")) outputExt = ".mp4";

            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == MediaType.SUBTITLES)
            {
                if (DownloaderConfig.MyOptions.SubtitleFormat == Enum.SubtitleFormat.SRT) outputExt = ".srt";
                else outputExt = ".vtt";
            }
            var output = Path.Combine(saveDir, saveName + outputExt);

            //检测目标文件是否存在
            while (File.Exists(output))
            {
                Logger.WarnMarkUp($"{Path.GetFileName(output)} => {Path.GetFileName(output = Path.ChangeExtension(output, $"copy" + Path.GetExtension(output)))}");
            }

            if (!string.IsNullOrEmpty(currentKID) && DownloaderConfig.MyOptions.MP4RealTimeDecryption && DownloaderConfig.MyOptions.Keys != null && DownloaderConfig.MyOptions.Keys.Length > 0 && mp4InitFile != "")
            {
                File.Delete(mp4InitFile);
                //shaka实时解密不需要init文件用于合并
                if (DownloaderConfig.MyOptions.UseShakaPackager)
                {
                    FileDic!.Remove(streamSpec.Playlist!.MediaInit, out _);
                }
            }

            //校验分片数量
            if (DownloaderConfig.MyOptions.CheckSegmentsCount && FileDic.Values.Any(s => s == null))
            {
                Logger.ErrorMarkUp(ResString.segmentCountCheckNotPass, totalCount, FileDic.Values.Where(s => s != null).Count());
                return false;
            }

            //移除无效片段
            var badKeys = FileDic.Where(i => i.Value == null).Select(i => i.Key);
            foreach (var badKey in badKeys)
            {
                FileDic!.Remove(badKey, out _);
            }

            //校验完整性
            if (DownloaderConfig.CheckContentLength && FileDic.Values.Any(a => a!.Success == false)) 
            {
                return false;
            }

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
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else finalVtt.AddCuesFromOne(vtt);
                }
                //写出字幕
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                //设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                var subContentFixed = finalVtt.ToVtt();
                //转换字幕格式
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
                    var mp4s = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".m4s")).ToArray();
                    var finalVtt = MP4VttUtil.ExtractSub(mp4s, timescale);
                    //写出字幕
                    var firstKey = FileDic.Keys.First();
                    var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                    foreach (var item in files) File.Delete(item);
                    FileDic.Clear();
                    var index = 0;
                    var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                    //设置字幕偏移
                    finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                    var subContentFixed = finalVtt.ToVtt();
                    //转换字幕格式
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

            //自动修复TTML raw字幕
            if (DownloaderConfig.MyOptions.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                && streamSpec.Extension != null && streamSpec.Extension.Contains("ttml"))
            {
                Logger.WarnMarkUp(ResString.fixingTTML);
                var first = true;
                var finalVtt = new WebVttSub();
                var keys = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Key);
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromTTML(FileDic[seg]!.ActualFilePath, 0);
                    //手动计算MPEGTS
                    if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else finalVtt.AddCuesFromOne(vtt);
                }
                //写出字幕
                var firstKey = FileDic.Keys.First();
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();

                //处理图形字幕
                await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                //设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                var subContentFixed = finalVtt.ToVtt();
                //转换字幕格式
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
                var first = true;
                var finalVtt = new WebVttSub();
                var keys = FileDic.OrderBy(s => s.Key.Index).Where(v => v.Value!.ActualFilePath.EndsWith(".m4s")).Select(s => s.Key);
                foreach (var seg in keys)
                {
                    var vtt = MP4TtmlUtil.ExtractFromMp4(FileDic[seg]!.ActualFilePath, 0);
                    //手动计算MPEGTS
                    if (finalVtt.MpegtsTimestamp == 0 && vtt.MpegtsTimestamp == 0)
                    {
                        vtt.MpegtsTimestamp = 90000 * (long)(skippedDur + keys.Where(s => s.Index < seg.Index).Sum(s => s.Duration));
                    }
                    if (first) { finalVtt = vtt; first = false; }
                    else finalVtt.AddCuesFromOne(vtt);
                }

                //写出字幕
                var firstKey = FileDic.Keys.First();
                var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();

                //处理图形字幕
                await SubtitleUtil.TryWriteImagePngsAsync(finalVtt, tmpDir);

                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                //设置字幕偏移
                finalVtt.LeftShiftTime(TimeSpan.FromSeconds(skippedDur));
                var subContentFixed = finalVtt.ToVtt();
                //转换字幕格式
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
            //合并
            if (!DownloaderConfig.MyOptions.SkipMerge)
            {
                //字幕也使用二进制合并
                if (DownloaderConfig.MyOptions.BinaryMerge || streamSpec.MediaType == MediaType.SUBTITLES)
                {
                    Logger.InfoMarkUp(ResString.binaryMerge);
                    var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                    MergeUtil.CombineMultipleFilesIntoSingleFile(files, output);
                    mergeSuccess = true;
                }
                else
                {
                    //ffmpeg合并
                    var files = FileDic.OrderBy(s => s.Key.Index).Select(s => s.Value).Select(v => v!.ActualFilePath).ToArray();
                    Logger.InfoMarkUp(ResString.ffmpegMerge);
                    var ext = streamSpec.MediaType == MediaType.AUDIO ? "m4a" : "mp4";
                    var ffOut = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + $".{ext}");
                    //检测目标文件是否存在
                    while (File.Exists(ffOut))
                    {
                        Logger.WarnMarkUp($"{Path.GetFileName(ffOut)} => {Path.GetFileName(ffOut = Path.ChangeExtension(ffOut, $"copy" + Path.GetExtension(ffOut)))}");
                    }
                    //大于1800分片，需要分步骤合并
                    if (files.Length >= 1800)
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
                    mergeSuccess = MergeUtil.MergeByFFmpeg(DownloaderConfig.MyOptions.FFmpegBinaryPath!, files, Path.ChangeExtension(ffOut, null), ext, useAACFilter, writeDate: !DownloaderConfig.MyOptions.NoDateInfo, useConcatDemuxer: DownloaderConfig.MyOptions.UseFFmpegConcatDemuxer);
                    if (mergeSuccess) output = ffOut;
                }
            }

            //删除临时文件夹
            if (!DownloaderConfig.MyOptions.SkipMerge && DownloaderConfig.MyOptions.DelAfterDone && mergeSuccess)
            {
                var files = FileDic.Values.Select(v => v!.ActualFilePath);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                OtherUtil.SafeDeleteDir(tmpDir);
            }

            //重新读取init信息
            if (mergeSuccess && totalCount >= 1 && string.IsNullOrEmpty(currentKID) && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method != Common.Enum.EncryptMethod.NONE)
            {
                currentKID = MP4DecryptUtil.ReadInit(output);
                //从文件读取KEY
                await SearchKeyAsync(currentKID);
            }

            //调用mp4decrypt解密
            if (mergeSuccess && File.Exists(output) && !string.IsNullOrEmpty(currentKID) && !DownloaderConfig.MyOptions.MP4RealTimeDecryption && DownloaderConfig.MyOptions.Keys != null && DownloaderConfig.MyOptions.Keys.Length > 0)
            {
                var enc = output;
                var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                Logger.InfoMarkUp($"[grey]Decrypting...[/]");
                var result = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.MyOptions.UseShakaPackager, mp4decrypt, DownloaderConfig.MyOptions.Keys, enc, dec, currentKID);
                if (result)
                {
                    File.Delete(enc);
                    File.Move(dec, enc);
                }
            }

            //记录所有文件信息
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
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); //速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            var progress = AnsiConsole.Progress().AutoClear(true);
            progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new ProgressBarColumn(){ Width = 30 },
                new MyPercentageColumn(),
                new DownloadStatusColumn(SpeedContainerDic),
                new DownloadSpeedColumn(SpeedContainerDic), //速度计算
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            });

            if (DownloaderConfig.MyOptions.MP4RealTimeDecryption && !DownloaderConfig.MyOptions.UseShakaPackager
                && DownloaderConfig.MyOptions.Keys != null && DownloaderConfig.MyOptions.Keys.Length > 0)
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.realTimeDecMessage}[/]");

            await progress.StartAsync(async ctx =>
            {
                //创建任务
                var dic = SelectedSteams.Select(item =>
                {
                    var description = item.ToShortShortString();
                    var task = ctx.AddTask(description, autoStart: false);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); //速度计算
                    //限速设置
                    if (DownloaderConfig.MyOptions.MaxSpeed != null)
                    {
                        SpeedContainerDic[task.Id].SpeedLimit = DownloaderConfig.MyOptions.MaxSpeed.Value;
                    }
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                if (!DownloaderConfig.MyOptions.ConcurrentDownload)
                {
                    //遍历，顺序下载
                    foreach (var kp in dic)
                    {
                        var task = kp.Value;
                        var result = await DownloadStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                        Results[kp.Key] = result;
                        //失败不再下载后续
                        if (!result) break;
                    }
                }
                else
                {
                    //并发下载
                    await Parallel.ForEachAsync(dic, async (kp, _) =>
                    {
                        var task = kp.Value;
                        var result = await DownloadStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                        Results[kp.Key] = result;
                    });
                }
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
