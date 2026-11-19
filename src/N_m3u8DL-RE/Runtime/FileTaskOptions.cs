using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Runtime;

internal sealed record FileTaskOptions
{
    internal const string DefaultMuxer = "ffmpeg";
    internal const bool DefaultAutoSubtitleFix = true;
    public required string Operation { get; init; }
    public required OutputFile[] Inputs { get; init; }
    public required string Output { get; init; }
    public string? Pattern { get; init; }
    public bool Overwrite { get; init; }
    public bool DryRun { get; init; }
    public string? FFmpegBinaryPath { get; init; }
    public string? MkvmergeBinaryPath { get; init; }
    public string Muxer { get; init; } = DefaultMuxer;
    public FFmpegConcatMode? ConcatMode { get; init; }
    public bool AutoSubtitleFix { get; init; } = DefaultAutoSubtitleFix;
    public string? Title { get; init; }
}

internal sealed record FileTaskResult(bool Success, string Output, string? ErrorMessage = null);
