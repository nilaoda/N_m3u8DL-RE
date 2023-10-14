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
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Xml.Linq;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class HTTPLiveRecordManager
    {
        IDownloader Downloader;
        DownloaderConfig DownloaderConfig;
        StreamExtractor StreamExtractor;
        List<StreamSpec> SelectedSteams;
        List<OutputFile> OutputFiles = new();
        DateTime NowDateTime;
        DateTime? PublishDateTime;
        bool STOP_FLAG = false;
        bool READ_IFO = false;
        ConcurrentDictionary<int, int> RecordingDurDic = new(); //已录制时长
        ConcurrentDictionary<int, double> RecordingSizeDic = new(); //已录制大小
        CancellationTokenSource CancellationTokenSource = new(); //取消Wait
        List<byte> InfoBuffer = new List<byte>(188 * 5000); //5000个分包中解析信息，没有就算了

        public HTTPLiveRecordManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            this.DownloaderConfig = downloaderConfig;
            Downloader = new SimpleDownloader(DownloaderConfig);
            NowDateTime = DateTime.Now;
            PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
            StreamExtractor = streamExtractor;
            SelectedSteams = selectedSteams;
        }

        private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, ProgressTask task, SpeedContainer speedContainer)
        {
            task.MaxValue = 1;
            task.StartTask();

            var name = streamSpec.ToShortString();
            var dirName = $"{DownloaderConfig.MyOptions.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            var saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;

            Logger.Debug($"dirName: {dirName}; saveDir: {saveDir}; saveName: {saveName}");

            //创建文件夹
            if (!Directory.Exists(saveDir)) Directory.CreateDirectory(saveDir);

            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(streamSpec.Url));
            request.Headers.ConnectionClose = false;
            foreach (var item in DownloaderConfig.Headers)
            {
                request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
            Logger.Debug(request.Headers.ToString());

            using var response = await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationTokenSource.Token);
            response.EnsureSuccessStatusCode();

            var output = Path.Combine(saveDir, saveName + ".ts");
            using var stream = new FileStream(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            using var responseStream = await response.Content.ReadAsStreamAsync(CancellationTokenSource.Token);
            var buffer = new byte[16 * 1024];
            var size = 0;

            //计时器
            TimeCounterAsync();
            //读取INFO
            ReadInfoAsync();

            try
            {
                while ((size = await responseStream.ReadAsync(buffer, CancellationTokenSource.Token)) > 0)
                {
                    if (!READ_IFO && InfoBuffer.Count < 188 * 5000)
                    {
                        InfoBuffer.AddRange(buffer);
                    }
                    speedContainer.Add(size);
                    RecordingSizeDic[task.Id] += size;
                    await stream.WriteAsync(buffer, 0, size);
                }
            }
            catch (OperationCanceledException oce) when (oce.CancellationToken == CancellationTokenSource.Token)
            {
                ;
            }

            Logger.InfoMarkUp("File Size: " + GlobalUtil.FormatFileSize(RecordingSizeDic[task.Id]));

            return true;
        }

        public async Task ReadInfoAsync()
        {
            while (!STOP_FLAG && !READ_IFO)
            {
                await Task.Delay(200);
                if (InfoBuffer.Count < 188 * 5000) continue;

                UInt16 ConvertToUint16(IEnumerable<byte> bytes)
                {
                    if (BitConverter.IsLittleEndian)
                        bytes = bytes.Reverse();
                    return BitConverter.ToUInt16(bytes.ToArray());
                }

                var data = InfoBuffer.ToArray();
                var programId = "";
                var serviceProvider = "";
                var serviceName = "";
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == 0x47 && (i + 188) < data.Length && data[i + 188] == 0x47)
                    {
                        var tsData = data.Skip(i).Take(188);
                        var tsHeaderInt = BitConverter.ToUInt32(BitConverter.IsLittleEndian ? tsData.Take(4).Reverse().ToArray() : tsData.Take(4).ToArray(), 0);
                        var pid = (tsHeaderInt & 0x1fff00) >> 8;
                        var tsPayload = tsData.Skip(4);
                        //PAT
                        if (pid == 0x0000)
                        {
                            programId = ConvertToUint16(tsPayload.Skip(9).Take(2)).ToString();
                        }
                        //SDT, BAT, ST
                        else if (pid == 0x0011)
                        {
                            var tableId = (int)tsPayload.Skip(1).First();
                            //Current TS Info
                            if (tableId == 0x42)
                            {
                                var sectionLength = ConvertToUint16(tsPayload.Skip(2).Take(2)) & 0xfff;
                                var sectionData = tsPayload.Skip(4).Take(sectionLength);
                                var dscripData = sectionData.Skip(8);
                                var descriptorsLoopLength = (ConvertToUint16(dscripData.Skip(3).Take(2))) & 0xfff;
                                var descriptorsData = dscripData.Skip(5).Take(descriptorsLoopLength);
                                var serviceProviderLength = (int)descriptorsData.Skip(3).First();
                                serviceProvider = Encoding.UTF8.GetString(descriptorsData.Skip(4).Take(serviceProviderLength).ToArray());
                                var serviceNameLength = (int)descriptorsData.Skip(4 + serviceProviderLength).First();
                                serviceName = Encoding.UTF8.GetString(descriptorsData.Skip(5 + serviceProviderLength).Take(serviceNameLength).ToArray());
                            }
                        }
                        if (programId != "" && (serviceName != "" || serviceProvider != ""))
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(programId))
                {
                    Logger.InfoMarkUp($"Program Id: [cyan]{programId.EscapeMarkup()}[/]");
                    if (!string.IsNullOrEmpty(serviceName)) Logger.InfoMarkUp($"Service Name: [cyan]{serviceName.EscapeMarkup()}[/]");
                    if (!string.IsNullOrEmpty(serviceProvider)) Logger.InfoMarkUp($"Service Provider: [cyan]{serviceProvider.EscapeMarkup()}[/]");
                    READ_IFO = true;
                }
            }
        }

        public async Task TimeCounterAsync()
        {
            while (!STOP_FLAG)
            {
                await Task.Delay(1000);
                RecordingDurDic[0]++;

                //检测时长限制
                if (RecordingDurDic.All(d => d.Value >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds))
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                    STOP_FLAG = true;
                    CancellationTokenSource.Cancel();
                }
            }
        }

        public async Task<bool> StartRecordAsync()
        {
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); //速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            var progress = AnsiConsole.Progress().AutoClear(true);
            progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

            //进度条的列定义
            progress.Columns(new ProgressColumn[]
            {
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new RecordingDurationColumn(RecordingDurDic), //时长显示
                new RecordingSizeColumn(RecordingSizeDic), //大小显示
                new RecordingStatusColumn(),
                new DownloadSpeedColumn(SpeedContainerDic), //速度计算
                new SpinnerColumn(),
            });

            await progress.StartAsync(async ctx =>
            {
                //创建任务
                var dic = SelectedSteams.Select(item =>
                {
                    var task = ctx.AddTask(item.ToShortString(), autoStart: false, maxValue: 0);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); //速度计算
                    RecordingDurDic[task.Id] = 0;
                    RecordingSizeDic[task.Id] = 0;
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                DownloaderConfig.MyOptions.LiveRecordLimit = DownloaderConfig.MyOptions.LiveRecordLimit ?? TimeSpan.MaxValue;
                var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
                if (limit != TimeSpan.MaxValue)
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime((int)limit.Value.TotalSeconds)}[/]");
                //录制直播时，用户选了几个流就并发录几个
                var options = new ParallelOptions()
                {
                    MaxDegreeOfParallelism = SelectedSteams.Count
                };
                //并发下载
                await Parallel.ForEachAsync(dic, options, async (kp, _) =>
                {
                    var task = kp.Value;
                    var consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                    Results[kp.Key] = await consumerTask;
                });
            });

            var success = Results.Values.All(v => v == true);

            return success;
        }
    }
}
