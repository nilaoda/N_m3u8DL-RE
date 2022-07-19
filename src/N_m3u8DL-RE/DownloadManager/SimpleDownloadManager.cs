using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class SimpleDownloadManager
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        DateTime NowDateTime;

        public SimpleDownloadManager(DownloaderConfig downloaderConfig) 
        { 
            this.DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            NowDateTime = DateTime.Now;
        }

        private async Task<bool> DownloadStreamAsync(StreamSpec streamSpec, ProgressTask task)
        {
            ConcurrentDictionary<MediaSegment, DownloadResult?> FileDic = new();

            var segments = streamSpec.Playlist?.MediaParts.SelectMany(m => m.MediaSegments);
            if (segments == null) return false;

            var type = streamSpec.MediaType ?? Common.Enum.MediaType.VIDEO;
            var dirName = $"{NowDateTime:yyyy-MM-dd_HH-mm-ss}_{streamSpec.GroupId}_{streamSpec.Codecs}_{streamSpec.Language}";
            var tmpDir = Path.Combine(DownloaderConfig.TmpDir ?? Environment.CurrentDirectory, dirName);
            var saveDir = DownloaderConfig.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.SaveName != null ? $"{DownloaderConfig.SaveName}.{type}.{streamSpec.Language}" : dirName;
            var headers = DownloaderConfig.Headers;
            var output = Path.Combine(saveDir, saveName + $".{streamSpec.Extension ?? "ts"}");

            Logger.Debug($"dirName: {dirName}; tmpDir: {tmpDir}; saveDir: {saveDir}; saveName: {saveName}; output: {output}");

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

            //下载init
            if (streamSpec.Playlist?.MediaInit != null)
            {
                totalCount++;
                var path = Path.Combine(tmpDir, "_init.mp4.tmp");
                var result = await Downloader.DownloadSegmentAsync(streamSpec.Playlist.MediaInit, path, headers);
                FileDic[streamSpec.Playlist.MediaInit] = result;
                task.Increment(1);
                //修改输出后缀
                if (streamSpec.MediaType == Common.Enum.MediaType.AUDIO)
                    output = Path.ChangeExtension(output, ".m4a");
                else
                    output = Path.ChangeExtension(output, ".mp4");
                if (result != null && result.Success) 
                {
                    var data = File.ReadAllBytes(result.ActualFilePath);
                    var pssh = MP4InitUtil.ReadWVPssh(data);
                    var kid = MP4InitUtil.ReadWVKid(data);
                    if (pssh != null) Logger.WarnMarkUp($"[grey]PSSH(WV): {pssh}[/]");
                    if (kid != null) Logger.WarnMarkUp($"[grey]KID: {kid}[/]");
                }
            }

            //开始下载
            var pad = "0".PadLeft(segments.Count().ToString().Length, '0');
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = DownloaderConfig.ThreadCount
            };
            await Parallel.ForEachAsync(segments, options, async (seg, _) =>
            {
                var index = seg.Index;
                var path = Path.Combine(tmpDir, index.ToString(pad) + $".{streamSpec.Extension ?? "clip"}.tmp");
                var result = await Downloader.DownloadSegmentAsync(seg, path, headers);
                FileDic[seg] = result;
                task.Increment(1);
            });

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

            //合并
            if (!DownloaderConfig.SkipMerge)
            {
                if (DownloaderConfig.BinaryMerge)
                {
                    Logger.InfoMarkUp(ResString.binaryMerge);
                    var files = FileDic.Values.Select(v => v!.ActualFilePath).OrderBy(s => s).ToArray();
                    DownloadUtil.CombineMultipleFilesIntoSingleFile(files, output);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            //删除临时文件夹
            if (DownloaderConfig.DelAfterDone)
            {
                var files = FileDic.Values.Select(v => v!.ActualFilePath);
                foreach (var file in files)
                {
                    File.Delete(file);
                }
                if (!Directory.EnumerateFiles(tmpDir).Any())
                {
                    Directory.Delete(tmpDir);
                }
            }

            return true;
        }

        public async Task<bool> StartDownloadAsync(IEnumerable<StreamSpec> streamSpecs)
        {
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            var progress = AnsiConsole.Progress().AutoClear(true);

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new ProgressBarColumn(),
                new PercentageColumn(),
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
                    var result = await DownloadStreamAsync(kp.Key, task);
                    Results[kp.Key] = result;
                }
            });

            return Results.Values.All(v => v == true);
        }
    }
}
