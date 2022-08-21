using Mp4SubtitleParser;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using System.Text;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class SimpleDownloadManager
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        DateTime NowDateTime;
        List<OutputFile> OutputFiles = new();

        public SimpleDownloadManager(DownloaderConfig downloaderConfig) 
        { 
            this.DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            NowDateTime = DateTime.Now;
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

        //若该文件夹为空，删除，同时判断其父文件夹，直到遇到根目录或不为空的目录
        private void SafeDeleteDir(string dirPath)
        {
            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
                return;

            var parent = Path.GetDirectoryName(dirPath)!;
            if (!Directory.EnumerateFileSystemEntries(dirPath).Any())
            {
                Directory.Delete(dirPath);
            }
            else
            {
                return;
            }
            SafeDeleteDir(parent);
        }

        //从文件读取KEY
        private async Task SearchKeyAsync(string? currentKID)
        {
            var _key = await MP4DecryptUtil.SearchKeyFromFile(DownloaderConfig.KeyTextFile, currentKID);
            if (_key != null)
            {
                if (DownloaderConfig.Keys == null)
                    DownloaderConfig.Keys = new string[] { _key };
                else
                    DownloaderConfig.Keys = DownloaderConfig.Keys.Concat(new string[] { _key }).ToArray();
            }
        }

        private void ChangeSpecInfo(StreamSpec streamSpec, List<Mediainfo> mediainfos, ref bool useAACFilter)
        {
            if (!DownloaderConfig.BinaryMerge && mediainfos.Any(m => m.DolbyVison == true))
            {
                DownloaderConfig.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge2}[/]");
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
            bool useAACFilter = false; //ffmpeg合并flag
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();

            var segments = streamSpec.Playlist?.MediaParts.SelectMany(m => m.MediaSegments);
            if (segments == null) return false;

            var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
            var dirName = $"{DownloaderConfig.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{streamSpec.GroupId}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            //去除非法字符
            dirName = ConvertUtil.GetValidFileName(dirName, filterSlash: true);
            var tmpDir = Path.Combine(DownloaderConfig.TmpDir ?? Environment.CurrentDirectory, dirName);
            var saveDir = DownloaderConfig.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.SaveName != null ? $"{DownloaderConfig.SaveName}.{type}.{streamSpec.Language}".TrimEnd('.') : dirName;
            var headers = DownloaderConfig.Headers;

            //mp4decrypt
            var mp4decrypt = DownloaderConfig.DecryptionBinaryPath!;
            var mp4InitFile = "";
            var currentKID = "";
            var readInfo = false; //是否读取过

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
            if (!DownloaderConfig.BinaryMerge && totalCount >= 1 && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method == Common.Enum.EncryptMethod.CENC)
            {
                DownloaderConfig.BinaryMerge = true;
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge4}[/]");
            }

            //下载init
            if (streamSpec.Playlist?.MediaInit != null)
            {
                //对于fMP4，自动开启二进制合并
                if (!DownloaderConfig.BinaryMerge && streamSpec.MediaType != MediaType.SUBTITLES)
                {
                    DownloaderConfig.BinaryMerge = true;
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge}[/]");
                }

                var path = Path.Combine(tmpDir, "_init.mp4.tmp");
                var result = await Downloader.DownloadSegmentAsync(streamSpec.Playlist.MediaInit, path, speedContainer, headers);
                FileDic[streamSpec.Playlist.MediaInit] = result;
                if (result == null)
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
                    if (DownloaderConfig.MP4RealTimeDecryption && streamSpec.Playlist.MediaInit.EncryptInfo.Method == Common.Enum.EncryptMethod.CENC)
                    {
                        var enc = result.ActualFilePath;
                        var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                        var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.UseShakaPackager, mp4decrypt, DownloaderConfig.Keys, enc, dec, currentKID);
                        if (dResult)
                        {
                            FileDic[streamSpec.Playlist.MediaInit]!.ActualFilePath = dec;
                        }
                    }
                    //ffmpeg读取信息
                    if (!readInfo)
                    {
                        Logger.WarnMarkUp(ResString.readingInfo);
                        var mediainfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.FFmpegBinaryPath!, result.ActualFilePath);
                        mediainfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                        ChangeSpecInfo(streamSpec, mediainfos, ref useAACFilter);
                        readInfo = true;
                    }
                }
            }

            //计算填零个数
            var pad = "0".PadLeft(segments.Count().ToString().Length, '0');

            //下载第一个分片
            if (!readInfo)
            {
                var seg = segments.First();
                segments = segments.Skip(1);

                var index = seg.Index;
                var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                task.Increment(1);
                //实时解密
                if (DownloaderConfig.MP4RealTimeDecryption && seg.EncryptInfo.Method == Common.Enum.EncryptMethod.CENC && result != null)
                {
                    //读取init信息
                    if (string.IsNullOrEmpty(currentKID))
                    {
                        currentKID = ReadInit(result.ActualFilePath);
                    }
                    //从文件读取KEY
                    await SearchKeyAsync(currentKID);
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.UseShakaPackager, mp4decrypt, DownloaderConfig.Keys, enc, dec, currentKID, mp4InitFile);
                    if (dResult)
                    {
                        File.Delete(enc);
                        result.ActualFilePath = dec;
                    }
                }
                //ffmpeg读取信息
                Logger.WarnMarkUp(ResString.readingInfo);
                var mediainfos = await MediainfoUtil.ReadInfoAsync(DownloaderConfig.FFmpegBinaryPath!, result!.ActualFilePath);
                mediainfos.ForEach(info => Logger.InfoMarkUp(info.ToStringMarkUp()));
                ChangeSpecInfo(streamSpec, mediainfos, ref useAACFilter);
                readInfo = true;
            }

            //开始下载
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = DownloaderConfig.ThreadCount
            };
            await Parallel.ForEachAsync(segments, options, async (seg, _) =>
            {
                var index = seg.Index;
                var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, speedContainer, headers);
                FileDic[seg] = result;
                task.Increment(1);
                //实时解密
                if (DownloaderConfig.MP4RealTimeDecryption && seg.EncryptInfo.Method == Common.Enum.EncryptMethod.CENC && result != null) 
                {
                    var enc = result.ActualFilePath;
                    var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                    var dResult = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.UseShakaPackager, mp4decrypt, DownloaderConfig.Keys, enc, dec, currentKID, mp4InitFile);
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
            else if (streamSpec.MediaType == MediaType.AUDIO && streamSpec.Extension == "m4s") outputExt = ".m4a";
            else if (streamSpec.MediaType != MediaType.SUBTITLES && streamSpec.Extension == "m4s") outputExt = ".mp4";

            var output = Path.Combine(saveDir, saveName + outputExt);

            //检测目标文件是否存在
            while (File.Exists(output))
            {
                Logger.WarnMarkUp($"{output} => {output = Path.ChangeExtension(output, $"copy" + Path.GetExtension(output))}");
            }

            if (DownloaderConfig.MP4RealTimeDecryption && mp4InitFile != "")
            {
                File.Delete(mp4InitFile);
                //shaka实时解密不需要init文件用于合并
                if (DownloaderConfig.UseShakaPackager)
                {
                    FileDic!.Remove(streamSpec.Playlist!.MediaInit, out _);
                }
            }

            //校验分片数量
            if (DownloaderConfig.CheckSegmentsCount && FileDic.Values.Any(s => s == null))
            {
                Logger.WarnMarkUp(ResString.segmentCountCheckNotPass, totalCount, FileDic.Values.Where(s => s != null).Count());
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
            if (DownloaderConfig.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES 
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
                var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                var subContentFixed = finalVtt.ToStringWithHeader();
                //转换字幕格式
                if (DownloaderConfig.SubtitleFormat != Enum.SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = ConvertUtil.WebVtt2Other(finalVtt, DownloaderConfig.SubtitleFormat);
                    output = Path.ChangeExtension(output, ".srt");
                }
                await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                FileDic[keys.First()] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
            }

            //自动修复VTT mp4字幕
            if (DownloaderConfig.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
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
                    var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                    foreach (var item in files) File.Delete(item);
                    FileDic.Clear();
                    var index = 0;
                    var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                    var subContentFixed = finalVtt.ToStringWithHeader();
                    //转换字幕格式
                    if (DownloaderConfig.SubtitleFormat != Enum.SubtitleFormat.VTT)
                    {
                        path = Path.ChangeExtension(path, ".srt");
                        subContentFixed = ConvertUtil.WebVtt2Other(finalVtt, DownloaderConfig.SubtitleFormat);
                        output = Path.ChangeExtension(output, ".srt");
                    }
                    await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                    FileDic[firstKey] = new DownloadResult()
                    {
                        ActualContentLength = subContentFixed.Length,
                        ActualFilePath = path
                    };
                    //修改输出后缀
                    output = Path.ChangeExtension(output, Path.GetExtension(path));
                }
            }

            //自动修复TTML raw字幕
            if (DownloaderConfig.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
                && streamSpec.Extension != null && streamSpec.Extension.Contains("ttml"))
            {
                Logger.WarnMarkUp(ResString.fixingTTML);
                var mp4s = FileDic.Values.Select(v => v!.ActualFilePath).Where(p => p.EndsWith(".ttml")).OrderBy(s => s).ToArray();
                var finalVtt = MP4TtmlUtil.ExtractFromTTMLs(mp4s, 0);
                //写出字幕
                var firstKey = FileDic.Keys.First();
                var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                var subContentFixed = finalVtt.ToStringWithHeader();
                //转换字幕格式
                if (DownloaderConfig.SubtitleFormat != Enum.SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = ConvertUtil.WebVtt2Other(finalVtt, DownloaderConfig.SubtitleFormat);
                    output = Path.ChangeExtension(output, ".srt");
                }
                await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                FileDic[firstKey] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
                //修改输出后缀
                output = Path.ChangeExtension(output, Path.GetExtension(path));
            }

            //自动修复TTML mp4字幕
            if (DownloaderConfig.AutoSubtitleFix && streamSpec.MediaType == Common.Enum.MediaType.SUBTITLES
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
                var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                foreach (var item in files) File.Delete(item);
                FileDic.Clear();
                var index = 0;
                var path = Path.Combine(tmpDir, index.ToString(pad) + ".fix.vtt");
                var subContentFixed = finalVtt.ToStringWithHeader();
                //转换字幕格式
                if (DownloaderConfig.SubtitleFormat != Enum.SubtitleFormat.VTT)
                {
                    path = Path.ChangeExtension(path, ".srt");
                    subContentFixed = ConvertUtil.WebVtt2Other(finalVtt, DownloaderConfig.SubtitleFormat);
                    output = Path.ChangeExtension(output, ".srt");
                }
                await File.WriteAllTextAsync(path, subContentFixed, new UTF8Encoding(false));
                FileDic[firstKey] = new DownloadResult()
                {
                    ActualContentLength = subContentFixed.Length,
                    ActualFilePath = path
                };
                //修改输出后缀
                output = Path.ChangeExtension(output, Path.GetExtension(path));
            }

            bool mergeSuccess = false;
            //合并
            if (!DownloaderConfig.SkipMerge)
            {
                //字幕也使用二进制合并
                if (DownloaderConfig.BinaryMerge || streamSpec.MediaType == MediaType.SUBTITLES)
                {
                    Logger.InfoMarkUp(ResString.binaryMerge);
                    var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                    MergeUtil.CombineMultipleFilesIntoSingleFile(files, output);
                    mergeSuccess = true;
                }
                else
                {
                    //ffmpeg合并
                    var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                    Logger.InfoMarkUp(ResString.ffmpegMerge);
                    var ext = streamSpec.MediaType == MediaType.AUDIO ? "m4a" : "mp4";
                    var ffOut = Path.Combine(Path.GetDirectoryName(output)!, Path.GetFileNameWithoutExtension(output) + $".{ext}");
                    //检测目标文件是否存在
                    while (File.Exists(ffOut))
                    {
                        Logger.WarnMarkUp($"{ffOut} => {ffOut = Path.ChangeExtension(ffOut, $"copy" + Path.GetExtension(ffOut))}");
                    }
                    mergeSuccess = MergeUtil.MergeByFFmpeg(DownloaderConfig.FFmpegBinaryPath!, files, Path.ChangeExtension(ffOut, null), ext, useAACFilter);
                    if (mergeSuccess) output = ffOut;
                }
            }

            //删除临时文件夹
            if (!DownloaderConfig.SkipMerge && DownloaderConfig.DelAfterDone && mergeSuccess)
            {
                var files = FileDic.Values.Select(v => v!.ActualFilePath);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                SafeDeleteDir(tmpDir);
            }

            //重新读取init信息
            if (mergeSuccess && totalCount >= 1 && string.IsNullOrEmpty(currentKID) && streamSpec.Playlist!.MediaParts.First().MediaSegments.First().EncryptInfo.Method == Common.Enum.EncryptMethod.CENC)
            {
                currentKID = ReadInit(output);
                //从文件读取KEY
                await SearchKeyAsync(currentKID);
            }

            //调用mp4decrypt解密
            if (mergeSuccess && File.Exists(output) && !string.IsNullOrEmpty(currentKID) && !DownloaderConfig.MP4RealTimeDecryption && DownloaderConfig.Keys != null && DownloaderConfig.Keys.Length > 0)
            {
                var enc = output;
                var dec = Path.Combine(Path.GetDirectoryName(enc)!, Path.GetFileNameWithoutExtension(enc) + "_dec" + Path.GetExtension(enc));
                Logger.InfoMarkUp($"[grey]Decrypting...[/]");
                var result = await MP4DecryptUtil.DecryptAsync(DownloaderConfig.UseShakaPackager, mp4decrypt, DownloaderConfig.Keys, enc, dec, currentKID);
                if (result)
                {
                    File.Delete(enc);
                    File.Move(dec, enc);
                }
            }

            //记录所有文件信息
            if (File.Exists(output))
                OutputFiles.Add(new OutputFile() { FilePath = output, LangCode = streamSpec.Language, Description = streamSpec.Name });

            return true;
        }

        public async Task<bool> StartDownloadAsync(IEnumerable<StreamSpec> streamSpecs)
        {
            SpeedContainer speedContainer = new SpeedContainer(); //速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            var progress = AnsiConsole.Progress().AutoClear(true);

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
                new DownloadSpeedColumn(speedContainer), //速度计算
                new RemainingTimeColumn(),
                new SpinnerColumn(),
            });

            await progress.StartAsync(async ctx =>
            {
                //创建任务
                var dic = streamSpecs.Select(item =>
                {
                    var task = ctx.AddTask(item.ToShortString(), autoStart: false);
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);
                //遍历，顺序下载
                foreach (var kp in dic)
                {
                    var task = kp.Value;
                    var result = await DownloadStreamAsync(kp.Key, task, speedContainer);
                    Results[kp.Key] = result;
                }
            });

            var success = Results.Values.All(v => v == true);

            //混流
            if (success && DownloaderConfig.MuxAfterDone && OutputFiles.Count > 0) 
            {
                var saveDir = DownloaderConfig.SaveDir ?? Environment.CurrentDirectory;
                var outName = $"{DownloaderConfig.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}";
                var outPath = Path.Combine(saveDir, outName);
                Logger.WarnMarkUp($"Muxing to [grey]{outName.EscapeMarkup()}.mkv[/]");
                var result = false;
                if (DownloaderConfig.UseMkvmerge) result = MergeUtil.MuxInputsByMkvmerge(DownloaderConfig.MkvmergeBinaryPath!, OutputFiles.ToArray(), outPath);
                else result = MergeUtil.MuxInputsByFFmpeg(DownloaderConfig.FFmpegBinaryPath!, OutputFiles.ToArray(), outPath);
                //完成后删除各轨道文件
                if (result)
                {
                    OutputFiles.ForEach(f => File.Delete(f.FilePath));
                    var tmpDir = DownloaderConfig.TmpDir ?? Environment.CurrentDirectory;
                    SafeDeleteDir(tmpDir);  
                }
                else Logger.ErrorMarkUp($"Mux failed");
            }

            return success;
        }
    }
}
