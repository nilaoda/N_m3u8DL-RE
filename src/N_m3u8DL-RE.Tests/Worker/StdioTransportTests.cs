using System.Text.Json;
using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;
using N_m3u8DL_RE.Worker;

namespace N_m3u8DL_RE.Tests.Worker;

[Collection("Download console")]
public class StdioTransportTests
{
    [Fact]
    public async Task InvocationCancellationStopsAWorkerWaitingForInput()
    {
        using var input = new WaitingReader();
        using var output = new StringWriter();
        using var stop = new CancellationTokenSource();
        var worker = WorkerHost.RunAsync(input, output, stop.Token);
        await Task.Run(() => input.Started.Wait(TimeSpan.FromSeconds(2)));
        stop.Cancel();
        Assert.Equal(1, await worker.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Contains("\"type\":\"ready\"", output.ToString());
    }

    private sealed class WaitingReader : TextReader
    {
        internal ManualResetEventSlim Started { get; } = new();
        private readonly ManualResetEventSlim released = new();
        public override int Read()
        {
            Started.Set();
            released.Wait();
            return -1;
        }
        protected override void Dispose(bool disposing)
        {
            released.Set();
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task BufferedProcessingUpdatesPreserveDownloadCounters()
    {
        using var output = new StringWriter();
        await using var transport = new StdioTransport(new StringReader(""), output);
        var observer = new WorkerTaskObserver(transport);
        observer.Progress(new TrackSnapshot("0", "downloading", DownloadedBytes: 4096, ReceivedBytes: 8192,
            Speed: 100, CompletedSegments: 2, TotalSegments: 2, Fraction: 1));
        observer.Progress(new TrackSnapshot("0", "merging", ProcessedBytes: 1024, Fraction: 0.25));
        transport.Complete();
        await transport.WriteAsync();
        using var document = JsonDocument.Parse(output.ToString());
        var progress = document.RootElement.GetProperty("progress");
        Assert.Equal("merging", progress.GetProperty("phase").GetString());
        Assert.Equal(4096, progress.GetProperty("downloadedBytes").GetInt64());
        Assert.Equal(8192, progress.GetProperty("receivedBytes").GetInt64());
        Assert.Equal(2, progress.GetProperty("completedSegments").GetInt64());
        Assert.Equal(2, progress.GetProperty("totalSegments").GetInt64());
        Assert.False(progress.TryGetProperty("speedBytesPerSecond", out _));
        Assert.Equal(0.25, progress.GetProperty("fraction").GetDouble());
        Assert.Equal(1024, progress.GetProperty("processedBytes").GetInt64());
    }

    [Fact]
    public async Task BufferedPhaseChangeDoesNotReuseProcessingStatistics()
    {
        using var output = new StringWriter();
        await using var transport = new StdioTransport(new StringReader(""), output);
        transport.Publish(new WorkerEvent
        {
            Type = "progress", Progress = new TaskProgress
            {
                TrackId = "0", Phase = "merging", ProcessedBytes = 1024, ProcessedSeconds = 10, Fraction = 0.5
            }
        });
        transport.Publish(new WorkerEvent
        {
            Type = "progress", Progress = new TaskProgress { TrackId = "0", Phase = "finalizing" }
        });
        transport.Complete();
        await transport.WriteAsync();
        using var document = JsonDocument.Parse(output.ToString());
        var progress = document.RootElement.GetProperty("progress");
        Assert.Equal("finalizing", progress.GetProperty("phase").GetString());
        Assert.False(progress.TryGetProperty("processedBytes", out _));
        Assert.False(progress.TryGetProperty("processedSeconds", out _));
        Assert.False(progress.TryGetProperty("fraction", out _));
    }

    [Fact]
    public async Task ContinuousLogsDoNotStarveControlEvents()
    {
        using var output = new ReplenishingWriter();
        await using var transport = new StdioTransport(new StringReader(""), output);
        var logCount = 0;
        const int totalLogs = 300;
        output.Written = line =>
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.GetProperty("type").GetString() != "log")
                return;
            if (++logCount == 1)
                transport.Publish(new WorkerEvent { Type = "accepted", CommandId = "cancel" });
            if (logCount < totalLogs)
                transport.Publish(new WorkerEvent { Type = "log", Message = "next" });
            else
                transport.Complete();
        };
        transport.Publish(new WorkerEvent { Type = "log", Message = "first" });
        await transport.WriteAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        var acceptedIndex = Array.FindIndex(lines, line => line.Contains("\"type\":\"accepted\""));
        Assert.InRange(acceptedIndex, 1, totalLogs - 1);
    }

    private sealed class ReplenishingWriter : StringWriter
    {
        internal Action<string>? Written { get; set; }
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = buffer.ToString();
            WriteLine(line);
            Written?.Invoke(line);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SerializationFailureDisconnectsTheSession()
    {
        using var output = new StringWriter();
        await using var transport = new StdioTransport(new StringReader(""), output);
        transport.Publish(new WorkerEvent
        {
            Type = "progress", Progress = new TaskProgress { TrackId = "0", Phase = "merging", Fraction = double.NaN }
        });
        await transport.WriteAsync().WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(transport.Disconnected.IsCancellationRequested);
    }
}
