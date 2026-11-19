using N_m3u8DL_RE.Protocol;
using N_m3u8DL_RE.Runtime;

namespace N_m3u8DL_RE.Worker;

internal sealed class WorkerTaskObserver(StdioTransport transport) : ITaskObserver
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> lastProgress = new();
    private string? lastStatus;
    private string? lastPhase;
    public void State(string status, string phase)
    {
        lock (gate)
        {
            if (lastStatus is "succeeded" or "failed" or "cancelled")
                return;
            if (lastStatus == "stopping" && status is "running" or "waitingForInput")
                status = "stopping";
            if (lastStatus == status && lastPhase == phase)
                return;
            lastStatus = status;
            lastPhase = phase;
            transport.Publish(new WorkerEvent { Type = "state", Status = status, Phase = phase });
        }
    }
    public void Tracks(TrackInfo[] tracks) => transport.Publish(new WorkerEvent
    {
        Type = "tracks", Tracks = tracks.Select(t => new SelectionChoice
        {
            Id = t.Id, Label = t.Label, MediaType = t.MediaType, Language = t.Language,
            Codecs = t.Codecs, Resolution = t.Resolution, Bandwidth = t.Bandwidth, DurationSeconds = t.DurationSeconds
        }).ToArray()
    });

    public void Progress(TrackSnapshot value)
    {
        lock (gate)
        {
            if (lastStatus is "succeeded" or "failed" or "cancelled")
                return;
            var now = Environment.TickCount64;
            var key = value.TrackId + ":" + value.Phase;
            if (lastProgress.TryGetValue(key, out var previous) && now - previous < 250 && value.Fraction != 1)
                return;
            lastProgress[key] = now;
        }
        transport.Publish(new WorkerEvent
        {
            Type = "progress", Progress = new TaskProgress
            {
                TrackId = value.TrackId, Phase = value.Phase, DownloadedBytes = value.DownloadedBytes,
                ReceivedBytes = value.ReceivedBytes, TotalBytes = value.TotalBytes,
                SpeedBytesPerSecond = value.Speed, CompletedSegments = value.CompletedSegments,
                TotalSegments = value.TotalSegments, RecordedSeconds = value.RecordedSeconds,
                Fraction = value.Fraction, ProcessedBytes = value.ProcessedBytes, ProcessedSeconds = value.ProcessedSeconds
            }
        });
    }
}
