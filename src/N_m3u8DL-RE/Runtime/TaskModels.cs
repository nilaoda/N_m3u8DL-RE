namespace N_m3u8DL_RE.Runtime;

internal sealed record TrackSnapshot(string TrackId, string Phase, long? DownloadedBytes = null,
    long? ReceivedBytes = null, long? TotalBytes = null, double? Speed = null, long? CompletedSegments = null,
    long? TotalSegments = null, double? RecordedSeconds = null, double? Fraction = null,
    long? ProcessedBytes = null, double? ProcessedSeconds = null);

internal sealed record ExecutionResult(string Status, string[] Outputs, string? ErrorCode = null, string? ErrorMessage = null);

internal sealed record TrackInfo(string Id, string Label, string MediaType, string? Language = null,
    string? Codecs = null, string? Resolution = null, long? Bandwidth = null, double? DurationSeconds = null);

internal interface ITaskObserver
{
    void State(string status, string phase);
    void Tracks(TrackInfo[] tracks);
    void Progress(TrackSnapshot progress);
}

internal sealed class NullTaskObserver : ITaskObserver
{
    public void State(string status, string phase) { }
    public void Tracks(TrackInfo[] tracks) { }
    public void Progress(TrackSnapshot progress) { }
}
