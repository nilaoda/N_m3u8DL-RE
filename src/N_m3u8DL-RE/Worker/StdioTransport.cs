using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using N_m3u8DL_RE.Protocol;

namespace N_m3u8DL_RE.Worker;

internal sealed class StdioTransport(TextReader input, TextWriter output) : IAsyncDisposable
{
    private const int LogCapacity = 128;
    private readonly Channel<WorkerEvent> events = Channel.CreateBounded<WorkerEvent>(256);
    private readonly Channel<WorkerEvent> logs = Channel.CreateBounded<WorkerEvent>(new BoundedChannelOptions(LogCapacity)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });
    private readonly ConcurrentDictionary<string, WorkerEvent> progress = new();
    private readonly SemaphoreSlim signal = new(0, 1);
    private readonly CancellationTokenSource disconnected = new();
    private readonly object publishGate = new();
    private bool terminal;
    private volatile bool complete;
    private long sequence;
    public CancellationToken Disconnected => disconnected.Token;

    private void Wake()
    {
        try
        {
            signal.Release();
        }
        catch (SemaphoreFullException) { }
    }

    internal void Publish(WorkerEvent value)
    {
        var overflow = false;
        lock (publishGate)
        {
            if (complete || terminal || disconnected.IsCancellationRequested)
                return;
            if (value.Type == "progress")
            {
                if (progress.Count < 1024 || progress.ContainsKey(value.Progress!.TrackId))
                {
                    var trackId = value.Progress!.TrackId;
                    if (progress.TryGetValue(trackId, out var previous))
                        value = value with { Progress = MergeProgress(previous.Progress!, value.Progress) };
                    progress[trackId] = value;
                }
            }
            else if (value.Type == "log")
                logs.Writer.TryWrite(value);
            else if (!events.Writer.TryWrite(value))
            {
                overflow = true;
            }
            if (value.Type == "result")
                terminal = true;
            Wake();
        }
        // 控制事件不丢弃；队列溢出则终止会话。取消回调放在锁外执行，避免交叉等待。
        if (overflow)
            disconnected.Cancel();
    }

    internal Task<WorkerCommand?> ReadAsync(CancellationToken token) =>
        Task.Run(ReadCommand).WaitAsync(token);

    private WorkerCommand? ReadCommand()
    {
        // 部分平台的控制台管道读取不能被 CancellationToken 中断；在后台读取，
        // 会话结束时停止等待，使客户端保持 stdin 打开也不会妨碍 Worker 退出。
        var line = new StringBuilder();
        while (true)
        {
            var value = input.Read();
            if (value < 0)
            {
                if (line.Length == 0)
                    return null;
                throw new InvalidDataException("Command must end with a newline.");
            }
            if (value == '\n')
                break;
            if (line.Length >= WorkerProtocol.MaxMessageCharacters)
                throw new InvalidDataException("Command exceeds the maximum message size.");
            line.Append((char)value);
        }
        return JsonSerializer.Deserialize(line.ToString(), WorkerJsonContext.Default.WorkerCommand)
            ?? throw new JsonException("Command must be an object.");
    }

    internal async Task WriteAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(disconnected.Token);
                while (events.Reader.TryRead(out var message))
                {
                    if (message.Type == "result")
                        await FlushTelemetryAsync();
                    await WriteOneAsync(message);
                }
                await FlushTelemetryAsync();
                if (complete && events.Reader.Count == 0 && progress.IsEmpty && logs.Reader.Count == 0)
                    return;
            }
        }
        catch (Exception)
        {
            // 序列化失败同样意味着协议通道不可用，必须唤醒会话及正在等待交互的任务。
            disconnected.Cancel();
        }
    }

    private async Task FlushTelemetryAsync()
    {
        foreach (var key in progress.Keys)
        {
            WorkerEvent? snapshot;
            // 取出快照与发布时的合并互斥，避免已发送的旧字段再次进入队列。
            lock (publishGate)
                progress.TryRemove(key, out snapshot);
            if (snapshot != null)
                await WriteOneAsync(snapshot);
        }
        // 每批日志有上限，让持续产生日志的任务也能及时发送确认和状态事件。
        for (var i = 0; i < LogCapacity && logs.Reader.TryRead(out var message); i++)
            await WriteOneAsync(message);
    }

    private static TaskProgress MergeProgress(TaskProgress previous, TaskProgress current)
    {
        var samePhase = previous.Phase == current.Phase;
        // 后处理消息只更新部分字段，合并待发送快照时保留已知的下载统计。
        // 处理量和速度只在同一阶段内沿用，百分比为空则表示当前阶段进度未知。
        return current with
        {
            DownloadedBytes = current.DownloadedBytes ?? previous.DownloadedBytes,
            ReceivedBytes = current.ReceivedBytes ?? previous.ReceivedBytes,
            TotalBytes = current.TotalBytes ?? previous.TotalBytes,
            CompletedSegments = current.CompletedSegments ?? previous.CompletedSegments,
            TotalSegments = current.TotalSegments ?? previous.TotalSegments,
            RecordedSeconds = current.RecordedSeconds ?? previous.RecordedSeconds,
            SpeedBytesPerSecond = current.SpeedBytesPerSecond ?? (samePhase ? previous.SpeedBytesPerSecond : null),
            ProcessedBytes = current.ProcessedBytes ?? (samePhase ? previous.ProcessedBytes : null),
            ProcessedSeconds = current.ProcessedSeconds ?? (samePhase ? previous.ProcessedSeconds : null)
        };
    }

    private async Task WriteOneAsync(WorkerEvent message)
    {
        var json = JsonSerializer.Serialize(message with { Sequence = ++sequence }, WorkerJsonContext.Default.WorkerEvent);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(disconnected.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        // 标准管道的写入也可能是同步系统调用；超时后结束会话，不等待被父进程堵住的写线程。
        await Task.Run(async () =>
        {
            await output.WriteLineAsync(json.AsMemory(), timeout.Token);
            await output.FlushAsync(timeout.Token);
        }).WaitAsync(timeout.Token);
    }

    internal void Complete()
    {
        lock (publishGate)
        {
            complete = true;
            Wake();
        }
    }
    public ValueTask DisposeAsync()
    {
        disconnected.Dispose();
        signal.Dispose();
        return ValueTask.CompletedTask;
    }
}
