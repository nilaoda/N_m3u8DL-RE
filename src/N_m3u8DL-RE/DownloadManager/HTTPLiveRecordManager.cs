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
using System.Collections.Concurrent;
using System.Diagnostics;

namespace N_m3u8DL_RE.DownloadManager;

internal class HTTPLiveRecordManager
{
    private const int InfoBufferLimit = 10 * 1024 * 1024;

    IDownloader Downloader;
    DownloaderConfig DownloaderConfig;
    StreamExtractor StreamExtractor;
    List<StreamSpec> SelectedSteams;
    List<OutputFile> OutputFiles = [];
    DateTime NowDateTime;
    DateTime? PublishDateTime;
    // 独立频道不能共用停止标志、响应流或媒体信息样本。
    private sealed class RecordingState : IDisposable
    {
        internal volatile bool Stopped;
        internal volatile bool ReadInfo;
        internal long WrittenSize;
        internal CancellationTokenSource Cancellation { get; } = new();
        // 最多采样开头 10 MiB，首次收到数据时分配，解析后释放。
        internal object InfoLock { get; } = new();
        internal byte[]? InfoBuffer;
        internal int InfoBufferLength;
        public void Dispose()
        {
            Cancellation.Dispose();
            InfoBuffer = null;
        }
    }
    private readonly HashSet<string> ReservedPaths = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    ConcurrentDictionary<int, TimeSpan> RecordingDurDic = new(); // 已录制时长
    ConcurrentDictionary<int, long> RecordingSizeDic = new(); // 已写入文件的大小，每秒刷新显示

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

        using var state = new RecordingState();
        var isChannel = StreamExtractor.ExtractorType == ExtractorType.IPTV_M3U;
        var dirName = $"{DownloaderConfig.MyOptions.SaveName ?? NowDateTime.ToString("yyyy-MM-dd_HH-mm-ss")}_{taskId}_{OtherUtil.GetValidFileName(streamSpec.GroupId ?? "", "-")}_{streamSpec.Codecs}_{streamSpec.Bandwidth}_{streamSpec.Language}";
        var saveDir = DownloaderConfig.MyOptions.SaveDir ?? Environment.CurrentDirectory;

        // 命名模板优先；频道默认使用名称和时间，直接输入沿用原保存名称。
        var saveName = isChannel
            ? $"{streamSpec.Name}_{NowDateTime:yyyy-MM-dd_HH-mm-ss}" : dirName;
        if (!string.IsNullOrWhiteSpace(DownloaderConfig.MyOptions.SavePattern))
        {
            saveName = OtherUtil.FormatSavePattern(DownloaderConfig.MyOptions.SavePattern, streamSpec, DownloaderConfig.MyOptions.SaveName, taskId);
        }
        else if (DownloaderConfig.MyOptions.SaveName != null)
        {
            saveName = isChannel
                ? SelectedSteams.Count > 1 ? $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Name}" : DownloaderConfig.MyOptions.SaveName
                : $"{DownloaderConfig.MyOptions.SaveName}.{streamSpec.Language}".TrimEnd('.');
        }

        Logger.Debug($"dirName: {dirName}; saveDir: {saveDir}; saveName: {saveName}");

        Directory.CreateDirectory(saveDir);
        var output = Path.Combine(saveDir, OtherUtil.GetSafeFileName(OtherUtil.GetValidFileName(saveName), ".ts"));
        if (isChannel)
            output = OtherUtil.HandleFileCollision(output, streamSpec, ReservedPaths);
        if (File.Exists(output))
        {
            Logger.Warn($"File already exists, skipping recording: {output}");
            return true;
        }
        // 列表解析和选择不建立直播连接；每个选中频道仅请求一次并读取自己的响应。
        using var channelSource = isChannel ? await StreamExtractor.LoadLiveSourceAsync(streamSpec, state.Cancellation.Token) : null;
        var source = channelSource ?? StreamExtractor.DirectSource ??
                     throw new InvalidDataException("HTTP live TS requires the original response stream.");
        var responseStream = source.Stream ??
                             throw new InvalidDataException("HTTP live TS response has no stream.");
        Logger.Info(ResString.saveName + output);
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        var buffer = new byte[16 * 1024];

        var counterTask = TimeCounterAsync(state, streamSpec, taskId, task == null);
        var infoTask = ReadInfoAsync(state, streamSpec);
        try
        {
            async Task WriteChunkAsync(ReadOnlyMemory<byte> data)
            {
                if (!state.ReadInfo)
                {
                    lock (state.InfoLock)
                    {
                        // 解析线程可能已经取走样本，进入锁后再次确认，避免重新分配缓冲。
                        if (!state.ReadInfo)
                        {
                            var length = Math.Min(data.Length, InfoBufferLimit - state.InfoBufferLength);
                            if (length > 0)
                            {
                                state.InfoBuffer ??= new byte[InfoBufferLimit];
                                data.Span[..length].CopyTo(state.InfoBuffer.AsSpan(state.InfoBufferLength));
                                state.InfoBufferLength += length;
                            }
                        }
                    }
                }
                await stream.WriteAsync(data, state.Cancellation.Token);
                speedContainer.Add(data.Length);
                Interlocked.Add(ref state.WrittenSize, data.Length);
            }

            await WriteChunkAsync(source.Prefix);
            int size;
            while ((size = await responseStream.ReadAsync(buffer, state.Cancellation.Token)) > 0)
            {
                await WriteChunkAsync(buffer.AsMemory(0, size));
            }
        }
        catch (OperationCanceledException) when (state.Cancellation.IsCancellationRequested)
        {
            // 到达直播录制时长限制。
        }
        finally
        {
            state.Stopped = true;
            await counterTask;
            RecordingSizeDic[taskId] = Interlocked.Read(ref state.WrittenSize);
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

    private async Task ReadInfoAsync(RecordingState state, StreamSpec streamSpec)
    {
        while (!state.ReadInfo)
        {
            await Task.Delay(200);
            byte[]? data;
            int length;
            lock (state.InfoLock)
            {
                if (state.InfoBufferLength < InfoBufferLimit && !state.Stopped) continue;
                // 固定样本只解析一次；停止采样后移交缓冲，避免复制整份样本。
                state.ReadInfo = true;
                data = state.InfoBuffer;
                length = state.InfoBufferLength;
                state.InfoBuffer = null;
                state.InfoBufferLength = 0;
            }
            TSInfoUtil.ServiceInfo[] services;
            try
            {
                services = TSInfoUtil.ReadServices(data.AsSpan(0, length));
            }
            finally
            {
                // 无论是否找到服务信息或解析失败，都释放采样缓冲的引用。
                data = null;
            }

            foreach (var service in services)
            {
                if (SelectedSteams.Count > 1)
                    Logger.InfoMarkUp($"[turquoise4]{streamSpec.Name?.EscapeMarkup()}[/]");
                Logger.InfoMarkUp($"Program Id: [turquoise4]{service.ProgramId}[/]");
                if (!string.IsNullOrEmpty(service.ServiceName))
                    Logger.InfoMarkUp($"Service Name: [turquoise4]{service.ServiceName.EscapeMarkup()}[/]");
                if (!string.IsNullOrEmpty(service.ServiceProvider))
                    Logger.InfoMarkUp($"Service Provider: [turquoise4]{service.ServiceProvider.EscapeMarkup()}[/]");
            }
        }
    }

    private async Task TimeCounterAsync(RecordingState state, StreamSpec streamSpec, int taskId, bool logProgress = false)
    {
        long previousSize = 0;
        // 单调时钟不会受系统校时、跨天或每次 Delay 的调度误差影响。
        var timer = Stopwatch.StartNew();
        while (!state.Stopped)
        {
            await Task.Delay(1000);
            if (state.Stopped)
            {
                break;
            }
            RecordingDurDic[taskId] = timer.Elapsed;
            var currentSize = Interlocked.Read(ref state.WrittenSize);
            RecordingSizeDic[taskId] = currentSize;
            if (logProgress)
            {
                var name = SelectedSteams.Count > 1 ? $"{streamSpec.Name ?? streamSpec.GroupId} " : "";
                Logger.Info($"{name}{GlobalUtil.FormatTime(RecordingDurDic[taskId])} " +
                            $"{GlobalUtil.FormatFileSize(currentSize)} " +
                            $"{GlobalUtil.FormatFileSize(currentSize - previousSize)}ps");
                previousSize = currentSize;
            }

            // 检测时长限制
            if (DownloaderConfig.MyOptions.LiveRecordLimit is { } limit && timer.Elapsed >= limit)
            {
                var name = SelectedSteams.Count > 1 ? $"{streamSpec.Name ?? streamSpec.GroupId}: " : "";
                Logger.WarnMarkUp($"[darkorange3_1]{name.EscapeMarkup()}{ResString.liveLimitReached}[/]");
                state.Stopped = true;
                state.Cancellation.Cancel();
            }
        }
    }

    private async Task<bool> TryRecordStreamAsync(StreamSpec streamSpec, int taskId, SpeedContainer speedContainer, ProgressTask? task = null)
    {
        try
        {
            return await RecordStreamAsync(streamSpec, taskId, speedContainer, task);
        }
        catch (Exception ex)
        {
            // 一个频道连接失败或断流，不中止其他独立频道的录制。
            Logger.Error($"{streamSpec.Name ?? streamSpec.Url}: {ex.Message}");
            return false;
        }
        finally
        {
            task?.StopTask();
        }
    }

    public async Task<bool> StartRecordAsync()
    {
        DownloaderConfig.MyOptions.LiveRecordLimit ??= TimeSpan.MaxValue;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected || DownloaderConfig.MyOptions.NoAnsiColor)
        {
            var results = await Task.WhenAll(SelectedSteams.Select((stream, index) =>
            {
                RecordingDurDic[index] = TimeSpan.Zero;
                RecordingSizeDic[index] = 0;
                return TryRecordStreamAsync(stream, index, new SpeedContainer());
            }));
            return results.All(result => result);
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
                var consumerTask = TryRecordStreamAsync(kp.Key, task.Id, SpeedContainerDic[task.Id], task);
                Results[kp.Key] = await consumerTask;
            });
        });

        var success = Results.Values.All(v => v == true);

        return success;
    }
}
