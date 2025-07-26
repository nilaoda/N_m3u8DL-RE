using System.Collections.Concurrent;
using System.Text;

using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Util;

using Spectre.Console;

namespace N_m3u8DL_RE.DownloadManager
{
    internal class HTTPLiveRecordManager
    {
        private readonly IDownloader Downloader;
        private readonly DownloaderConfig DownloaderConfig;
        private readonly StreamExtractor StreamExtractor;
        private readonly List<StreamSpec> SelectedSteams;
        private readonly List<OutputFile> OutputFiles = [];
        private readonly DateTime NowDateTime;
        private readonly DateTime? PublishDateTime;
        private bool STOP_FLAG = false;
        private bool READ_IFO = false;
        private readonly ConcurrentDictionary<int, int> RecordingDurDic = new(); // 已录制时长
        private readonly ConcurrentDictionary<int, double> RecordingSizeDic = new(); // 已录制大小
        private readonly CancellationTokenSource CancellationTokenSource = new(); // 取消Wait
        private readonly List<byte> InfoBuffer = new(188 * 5000); // 5000个分包中解析信息，没有就算了

        public HTTPLiveRecordManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
        {
            DownloaderConfig = downloaderConfig;
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
            _ = streamSpec.ToShortString();
            string dirName = $"{DownloaderConfig.MyOptions.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{task.Id}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
            string saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;
            string saveName = DownloaderConfig.MyOptions.SaveName != null ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.') : dirName;

            Logger.Debug($"dirName: {dirName}; saveDir: {saveDir}; saveName: {saveName}");

            // 创建文件夹
            if (!Directory.Exists(saveDir))
            {
                Directory.CreateDirectory(saveDir);
            }

            using HttpRequestMessage request = new(HttpMethod.Get, new Uri(streamSpec.Url));
            request.Headers.ConnectionClose = false;
            foreach (KeyValuePair<string, string> item in DownloaderConfig.Headers)
            {
                request.Headers.TryAddWithoutValidation(item.Key, item.Value);
            }
            Logger.Debug(request.Headers.ToString());

            using HttpResponseMessage response = await HTTPUtil.AppHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationTokenSource.Token);
            response.EnsureSuccessStatusCode();

            string output = Path.Combine(saveDir, saveName + ".ts");
            using FileStream stream = new(output, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            using Stream responseStream = await response.Content.ReadAsStreamAsync(CancellationTokenSource.Token);
            byte[] buffer = new byte[16 * 1024];
            int size = 0;

            // 计时器
            _ = TimeCounterAsync();
            // 读取INFO
            _ = ReadInfoAsync();

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
                    await stream.WriteAsync(buffer.AsMemory(0, size));
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
                if (InfoBuffer.Count < 188 * 5000)
                {
                    continue;
                }

                static ushort ConvertToUint16(IEnumerable<byte> bytes)
                {
                    if (BitConverter.IsLittleEndian)
                    {
                        bytes = bytes.Reverse();
                    }

                    return BitConverter.ToUInt16(bytes.ToArray());
                }

                byte[] data = [.. InfoBuffer];
                string programId = "";
                string serviceProvider = "";
                string serviceName = "";
                for (int i = 0; i < data.Length; i++)
                {
                    if (data[i] == 0x47 && (i + 188) < data.Length && data[i + 188] == 0x47)
                    {
                        IEnumerable<byte> tsData = data.Skip(i).Take(188);
                        uint tsHeaderInt = BitConverter.ToUInt32(BitConverter.IsLittleEndian ? [.. tsData.Take(4).Reverse()] : [.. tsData.Take(4)], 0);
                        uint pid = (tsHeaderInt & 0x1fff00) >> 8;
                        IEnumerable<byte> tsPayload = tsData.Skip(4);
                        // PAT
                        if (pid == 0x0000)
                        {
                            programId = ConvertToUint16(tsPayload.Skip(9).Take(2)).ToString();
                        }
                        // SDT, BAT, ST
                        else if (pid == 0x0011)
                        {
                            int tableId = tsPayload.Skip(1).First();
                            // Current TS Info
                            if (tableId == 0x42)
                            {
                                int sectionLength = ConvertToUint16(tsPayload.Skip(2).Take(2)) & 0xfff;
                                IEnumerable<byte> sectionData = tsPayload.Skip(4).Take(sectionLength);
                                IEnumerable<byte> dscripData = sectionData.Skip(8);
                                int descriptorsLoopLength = (ConvertToUint16(dscripData.Skip(3).Take(2))) & 0xfff;
                                IEnumerable<byte> descriptorsData = dscripData.Skip(5).Take(descriptorsLoopLength);
                                int serviceProviderLength = descriptorsData.Skip(3).First();
                                serviceProvider = Encoding.UTF8.GetString([.. descriptorsData.Skip(4).Take(serviceProviderLength)]);
                                int serviceNameLength = descriptorsData.Skip(4 + serviceProviderLength).First();
                                serviceName = Encoding.UTF8.GetString([.. descriptorsData.Skip(5 + serviceProviderLength).Take(serviceNameLength)]);
                            }
                        }
                        if (programId != "" && (serviceName != "" || serviceProvider != ""))
                        {
                            break;
                        }
                    }
                }

                if (!string.IsNullOrEmpty(programId))
                {
                    Logger.InfoMarkUp($"Program Id: [cyan]{programId.EscapeMarkup()}[/]");
                    if (!string.IsNullOrEmpty(serviceName))
                    {
                        Logger.InfoMarkUp($"Service Name: [cyan]{serviceName.EscapeMarkup()}[/]");
                    }

                    if (!string.IsNullOrEmpty(serviceProvider))
                    {
                        Logger.InfoMarkUp($"Service Provider: [cyan]{serviceProvider.EscapeMarkup()}[/]");
                    }

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

                // 检测时长限制
                if (RecordingDurDic.All(d => d.Value >= DownloaderConfig.MyOptions.LiveRecordLimit?.TotalSeconds))
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.LiveLimitReached}[/]");
                    STOP_FLAG = true;
                    CancellationTokenSource.Cancel();
                }
            }
        }

        public async Task<bool> StartRecordAsync()
        {
            ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
            ConcurrentDictionary<StreamSpec, bool?> Results = new();

            Progress progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
            progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

            // 进度条的列定义
            ProgressColumn[] progressColumns =
            [
                new TaskDescriptionColumn() { Alignment = Justify.Left },
                new RecordingDurationColumn(RecordingDurDic), // 时长显示
                new RecordingSizeColumn(RecordingSizeDic), // 大小显示
                new RecordingStatusColumn(),
                new DownloadSpeedColumn(SpeedContainerDic), // 速度计算
                new SpinnerColumn(),
            ];
            if (DownloaderConfig.MyOptions.NoAnsiColor)
            {
                progressColumns = [.. progressColumns.SkipLast(1)];
            }
            progress.Columns(progressColumns);

            await progress.StartAsync(async ctx =>
            {
                // 创建任务
                Dictionary<StreamSpec, ProgressTask> dic = SelectedSteams.Select(item =>
                {
                    ProgressTask task = ctx.AddTask(item.ToShortString(), autoStart: false, maxValue: 0);
                    SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                    RecordingDurDic[task.Id] = 0;
                    RecordingSizeDic[task.Id] = 0;
                    return (item, task);
                }).ToDictionary(item => item.item, item => item.task);

                DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
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
                // 并发下载
                await Parallel.ForEachAsync(dic, options, async (kp, _) =>
                {
                    ProgressTask task = kp.Value;
                    Task<bool> consumerTask = RecordStreamAsync(kp.Key, task, SpeedContainerDic[task.Id]);
                    Results[kp.Key] = await consumerTask;
                });
            });

            bool success = Results.Values.All(v => v == true);

            return success;
        }
    }
}