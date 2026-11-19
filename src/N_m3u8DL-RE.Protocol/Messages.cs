namespace N_m3u8DL_RE.Protocol;

public static class WorkerProtocol
{
    public const int Version = 1;
    public const int MaxMessageCharacters = 1024 * 1024;
}

/// <summary>客户端命令；每个 Worker 只接受一次 start。</summary>
public sealed record WorkerCommand
{
    public required string Type { get; init; }
    public required string Id { get; init; }
    public int? ProtocolVersion { get; init; }
    // 省略 operation 时按下载请求解析。
    public string? Operation { get; init; }
    public TaskRequest? Request { get; init; }
    public FileTaskRequest? FileRequest { get; init; }
    public string? InteractionId { get; init; }
    public string[]? SelectedIds { get; init; }
}

/// <summary>输出时只保留当前消息类型需要的字段。</summary>
public sealed record WorkerEvent
{
    public required string Type { get; init; }
    public long Sequence { get; init; }
    public string? CommandId { get; init; }
    public int? ProtocolVersion { get; init; }
    public string? ProgramVersion { get; init; }
    public string[]? Capabilities { get; init; }
    public string? Status { get; init; }
    public string? Phase { get; init; }
    public SelectionChoice[]? Tracks { get; init; }
    public TaskProgress? Progress { get; init; }
    public InteractionRequest? Interaction { get; init; }
    public TaskResult? Result { get; init; }
    public string? Level { get; init; }
    public string? Message { get; init; }
    public TaskError? Error { get; init; }
}
