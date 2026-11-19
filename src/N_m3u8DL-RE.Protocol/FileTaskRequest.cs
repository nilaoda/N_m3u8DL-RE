namespace N_m3u8DL_RE.Protocol;

/// <summary>独立文件处理任务；输入既可以是文件，也可以是录制目录。</summary>
public sealed record FileTaskRequest
{
    public required FileTaskInput[] Inputs { get; init; }
    public required string Output { get; init; }
    public string? Pattern { get; init; }
    public bool Overwrite { get; init; }
    public bool DryRun { get; init; }
    public string? FfmpegBinaryPath { get; init; }
    public string? MkvmergeBinaryPath { get; init; }
    public string? Muxer { get; init; }
    public string? ConcatMode { get; init; }
    public bool? AutoSubtitleFix { get; init; }
    public string? Title { get; init; }
}

public sealed record FileTaskInput
{
    public required string Path { get; init; }
    public string? Language { get; init; }
    public string? Name { get; init; }
}
