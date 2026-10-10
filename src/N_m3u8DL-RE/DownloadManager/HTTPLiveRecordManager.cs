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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace N_m3u8DL_RE.DownloadManager;

internal class HTTPLiveRecordManager
{
    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    List<OutputFile> OutputFiles = [];
    DateTime NowDateTime;
    DateTime? PublishDateTime;
    bool STOP_FLAG = false;
    bool READ_IFO = false;
    ConcurrentDictionary<int, TimeSpan> RecordingDurDic = new(); // 已录制时长
    ConcurrentDictionary<int, long> RecordingSizeDic = new(); // 已写入文件的大小
    CancellationTokenSource CancellationTokenSource = new(); // 取消Wait
    List<byte> InfoBuffer = new List<byte>(188 * 5000); // 5000个分包中解析信息，没有就算了

    public HTTPLiveRecordManager(DownloaderConfig downloaderConfig, List<StreamSpec> selectedSteams, StreamExtractor streamExtractor)
    {
        this.DownloaderConfig = downloaderConfig;
        Downloader = new SimpleDownloader(DownloaderConfig);
        NowDateTime = DateTime.Now;
        PublishDateTime = selectedSteams.FirstOrDefault()?.PublishTime;
        StreamExtractor = streamExtractor;
        SelectedSteams = selectedSteams;
    }

    private async Task<bool> RecordStreamAsync(StreamSpec streamSpec, int taskId, SpeedContainer speedContainer,
        ProgressTask? task = null)
    {
        if (task != null)
        {
            task.MaxValue = 1;
            task.StartTask();
        }

        var name = streamSpec.ToShortString();
        var dirName = $"{DownloaderConfig.MyOptions.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{taskId}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
        var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;

        // Use SavePattern if provided, otherwise use SaveName or dirName
        var saveName = dirName;
        if (!string.IsNullOrWhiteSpace(DownloaderConfig.MyOptions.SavePattern))
        {
            saveName = OtherUtil.FormatSavePattern(DownloaderConfig.MyOptions.SavePattern, streamSpec, DownloaderConfig.MyOptions.SaveName, taskId);
        }
        else if (DownloaderConfig.MyOptions.SaveName != null)
        {
            saveName = $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.');
        }

        Logger.Debug($"dirName: {dirName}; saveDir: {saveDir}; saveName: {saveName}");

        Directory.CreateDirectory(saveDir);
        var source = StreamExtractor.DirectSource ??
                     throw new InvalidDataException("HTTP live TS requires the original response stream.");
        var responseStream = source.Stream ??
                             throw new InvalidDataException("HTTP live TS response has no stream.");
        var output = Path.Combine(saveDir, OtherUtil.GetSafeFileName(saveName, ".ts"));
        if (File.Exists(output))
        {
            Logger.Warn($"File already exists, skipping recording: {output}");
            return true;
        }
        Logger.Info(ResString.saveName + output);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var buffer = new byte[16 * 1024];

        var counterTask = TimeCounterAsync(task == null);
        var infoTask = ReadInfoAsync();
        try
        {
            async Task WriteChunkAsync(ReadOnlyMemory<byte> data)
            {
                if (!READ_IFO && InfoBuffer.Count < 188 * 5000)
                {
                    InfoBuffer.AddRange(data.ToArray());
                }
                await stream.WriteAsync(data, CancellationTokenSource.Token);
                speedContainer.Add(data.Length);
                RecordingSizeDic[taskId] += data.Length;
            }

            await WriteChunkAsync(source.Prefix);
            int size;
            while ((size = await responseStream.ReadAsync(buffer, CancellationTokenSource.Token)) > 0)
            {
                await WriteChunkAsync(buffer.AsMemory(0, size));
            }
        }
        catch (OperationCanceledException) when (CancellationTokenSource.IsCancellationRequested)
        {
            // 到达直播录制时长限制。
        }
        finally
        {
            STOP_FLAG = true;
            await counterTask;
            try
            {
                await infoTask;
            }
            catch (Exception ex)
            {
                Logger.Debug($"Could not read TS service information: {ex.Message}");
            }
        }

        Logger.InfoMarkUp("File Size: " + GlobalUtil.FormatFileSize(RecordingSizeDic[taskId]));
        return true;
    }

    public async Task ReadInfoAsync()
    {
        while (!STOP_FLAG && !READ_IFO)
        {
            await Task.Delay(200);
            if (InfoBuffer.Count < 188 * 5000) continue;

            ushort ConvertToUint16(IEnumerable<byte> bytes)
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
                    // PAT
                    if (pid == 0x0000)
                    {
                        programId = ConvertToUint16(tsPayload.Skip(9).Take(2)).ToString();
                    }
                    // SDT, BAT, ST
                    else if (pid == 0x0011)
                    {
                        var tableId = (int)tsPayload.Skip(1).First();
                        // Current TS Info
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
                Logger.InfoMarkUp($"Program Id: [turquoise4]{programId.EscapeMarkup()}[/]");
                if (!string.IsNullOrEmpty(serviceName)) Logger.InfoMarkUp($"Service Name: [turquoise4]{serviceName.EscapeMarkup()}[/]");
                if (!string.IsNullOrEmpty(serviceProvider)) Logger.InfoMarkUp($"Service Provider: [turquoise4]{serviceProvider.EscapeMarkup()}[/]");
                READ_IFO = true;
            }
        }
    }

    public async Task TimeCounterAsync(bool logProgress = false)
    {
        long previousSize = 0;
        // 单调时钟不会受系统校时、跨天或每次 Delay 的调度误差影响。
        var timer = Stopwatch.StartNew();
        while (!STOP_FLAG)
        {
            await Task.Delay(1000);
            if (STOP_FLAG)
            {
                break;
            }
            RecordingDurDic[0] = timer.Elapsed;
            if (logProgress)
            {
                var currentSize = RecordingSizeDic[0];
                Logger.Info($"{GlobalUtil.FormatTime(RecordingDurDic[0])} " +
                            $"{GlobalUtil.FormatFileSize(currentSize)} " +
                            $"{GlobalUtil.FormatFileSize(currentSize - previousSize)}ps");
                previousSize = currentSize;
            }

            // 检测时长限制
            if (DownloaderConfig.MyOptions.LiveRecordLimit is { } limit && RecordingDurDic.All(d => d.Value >= limit))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimitReached}[/]");
                STOP_FLAG = true;
                CancellationTokenSource.Cancel();
            }
        }
    }

    public async Task<bool> StartRecordAsync()
    {
        DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            var stream = SelectedSteams.Single();
            RecordingDurDic[0] = TimeSpan.Zero;
            RecordingSizeDic[0] = 0;
            return await RecordStreamAsync(stream, 0, new SpeedContainer());
        }

        ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic = new(); // 速度计算
        ConcurrentDictionary<StreamSpec, bool?> Results = new();

        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = DownloaderConfig.MyOptions.LogLevel != LogLevel.OFF;

        // 进度条的列定义
        var progressColumns = new ProgressColumn[]
        {
            new TaskDescriptionColumn() { Alignment = Justify.Left },
            new RecordingDurationColumn(RecordingDurDic), // 时长显示
            new RecordingSizeColumn(RecordingSizeDic), // 大小显示
            new RecordingStatusColumn(),
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
                var task = ctx.AddTask(item.ToShortString(), autoStart: false, maxValue: 0);
                SpeedContainerDic[task.Id] = new SpeedContainer(); // 速度计算
                RecordingDurDic[task.Id] = TimeSpan.Zero;
                RecordingSizeDic[task.Id] = 0;
                return (item, task);
            }).ToDictionary(item => item.item, item => item.task);

            var limit = DownloaderConfig.MyOptions.LiveRecordLimit;
            if (limit != TimeSpan.MaxValue)
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.liveLimit}{GlobalUtil.FormatTime(limit.Value)}[/]");
            // 录制直播时，用户选了几个流就并发录几个
            var options = new ParallelOptions()
            {
                MaxDegreeOfParallelism = SelectedSteams.Count
            };
            // 并发下载
            await Parallel.ForEachAsync(dic, options, async (kp, _) =>
            {
                var task = kp.Value;
                var consumerTask = RecordStreamAsync(kp.Key, task.Id, SpeedContainerDic[task.Id], task);
                Results[kp.Key] = await consumerTask;
            });
        });

        var success = Results.Values.All(v => v == true);

        return success;
    }
}
