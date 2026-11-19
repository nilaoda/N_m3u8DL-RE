namespace N_m3u8DL_RE.Protocol;

public sealed record TaskProgress
{
    public required string TrackId { get; init; }
    public required string Phase { get; init; }
    public long? DownloadedBytes { get; init; }
    public long? ReceivedBytes { get; init; }
    public long? TotalBytes { get; init; }
    public double? SpeedBytesPerSecond { get; init; }
    public long? CompletedSegments { get; init; }
    public long? TotalSegments { get; init; }
    public double? RecordedSeconds { get; init; }
    public double? Fraction { get; init; }
    public long? ProcessedBytes { get; init; }
    public double? ProcessedSeconds { get; init; }
}

public sealed record TaskError(string Code, string Message);
public sealed record OutputArtifact(string Path, long? Size = null);
public sealed record TaskResult(string Status, OutputArtifact[] Outputs, TaskError? Error = null);

public sealed record SelectionChoice
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? MediaType { get; init; }
    public string? Language { get; init; }
    public string? Codecs { get; init; }
    public string? Resolution { get; init; }
    public long? Bandwidth { get; init; }
    public double? DurationSeconds { get; init; }
    public long[]? PartIds { get; init; }
}

public sealed record InteractionRequest(string Id, string Kind, SelectionChoice[] Choices, string[] DefaultIds);
