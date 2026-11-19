namespace N_m3u8DL_RE.Protocol;

/// <summary>下载与录制配置；未指定的字段使用 CLI 默认值。</summary>
public sealed record TaskRequest
{
    public required string Input { get; init; }
    public string? SaveDir { get; init; }
    public string? TmpDir { get; init; }
    public string? SaveName { get; init; }
    public string? SavePattern { get; init; }
    public string? UrlProcessorArgs { get; init; }
    public Dictionary<string, string>? Headers { get; init; }
    public string? Cookies { get; init; }
    public bool? AutoSelect { get; init; }
    public bool? SubOnly { get; init; }
    public string? SubFormat { get; init; }
    public string? SelectVideo { get; init; }
    public string? SelectAudio { get; init; }
    public string? SelectSubtitle { get; init; }
    public string? DropVideo { get; init; }
    public string? DropAudio { get; init; }
    public string? DropSubtitle { get; init; }
    public bool? VodSelectParts { get; init; }
    public bool? VodListParts { get; init; }
    public string? VodDropParts { get; init; }
    public int? ThreadCount { get; init; }
    public int? DownloadRetryCount { get; init; }
    public double? HttpRequestTimeout { get; init; }
    public string? HttpVersion { get; init; }
    public long? MaxSpeed { get; init; }
    public bool? ConcurrentDownload { get; init; }
    public bool? SkipMerge { get; init; }
    public bool? SkipDownload { get; init; }
    public bool? BinaryMerge { get; init; }
    public string? FfmpegConcatMode { get; init; }
    public bool? AutoSubtitleFix { get; init; }
    public bool? CheckSegmentsCount { get; init; }
    public bool? NoDateInfo { get; init; }
    public bool? WriteMetaJson { get; init; }
    public bool? DelAfterDone { get; init; }
    public string? FfmpegBinaryPath { get; init; }
    public string? DecryptionBinaryPath { get; init; }
    public string? DecryptionEngine { get; init; }
    public bool? Mp4RealTimeDecryption { get; init; }
    public string? CustomHlsMethod { get; init; }
    public string? CustomHlsKey { get; init; }
    public string? CustomHlsIv { get; init; }
    public string? CustomHlsScope { get; init; }
    public bool? AllowHlsMultiExtMap { get; init; }
    public string[]? Keys { get; init; }
    public string? KeyTextFile { get; init; }
    public string? MuxAfterDone { get; init; }
    public string[]? MuxImports { get; init; }
    public string? CustomProxy { get; init; }
    public bool? UseSystemProxy { get; init; }
    public string? NetworkInterface { get; init; }
    public string? BaseUrl { get; init; }
    public bool? AppendUrlParams { get; init; }
    public string? CustomRange { get; init; }
    public string[]? AdKeywords { get; init; }
    public bool? LivePerformAsVod { get; init; }
    public bool? LiveRealTimeMerge { get; init; }
    public bool? LivePipeMux { get; init; }
    public bool? LiveFixVttByAudio { get; init; }
    public bool? LiveKeepSegments { get; init; }
    public string? LiveRecordLimit { get; init; }
    public int? LiveWaitTime { get; init; }
    public int? LiveIdleTimeout { get; init; }
    public int? LiveTakeCount { get; init; }
    public string? LiveCatchup { get; init; }
    public string? TaskStartAt { get; init; }
    public string? LogFilePath { get; init; }
    public string? LogLevel { get; init; }
    public bool? NoLog { get; init; }
}
