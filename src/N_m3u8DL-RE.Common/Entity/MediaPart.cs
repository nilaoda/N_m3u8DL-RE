namespace N_m3u8DL_RE.Common.Entity;

// 主要处理 EXT-X-DISCONTINUITY，也承载 DASH Period 与 HLS MAP 的初始化边界。
public class MediaPart
{
    // INIT信息：本段媒体对应的初始化信息，不能用其它 Period/MAP 的 init 替代。
    public MediaSegment? MediaInit { get; set; }
    // DASH Period 身份使用清单中的顺序，id 可以缺失或重复。
    public int? PeriodIndex { get; set; }
    // HLS 不连续序号用于跨音视频轨道对齐，MAP 变化本身不增加此序号。
    public long? DiscontinuitySequence { get; set; }
    public string? PeriodId { get; set; }
    public double? PeriodStart { get; set; }
    public double? PeriodDuration { get; set; }
    // 媒体时间轴与 Period 时间轴之间的偏移，单位为秒。
    public double? PresentationTimeOffset { get; set; }
    public string? RepresentationId { get; set; }
    public string? Codecs { get; set; }
    // 过滤后的输出时间轴：移除整个广告 Period 后，后续媒体向前衔接。
    public double? OutputStart { get; set; }
    public double? OutputDuration { get; set; }
    // 输出所采用的源时间原点；同一 Period 各轨道共用裁剪量，保留帧边界差异。
    public double? OutputInpoint { get; set; }
    public List<MediaSegment> MediaSegments { get; set; } = [];

    public MediaPart WithSegments(List<MediaSegment> segments)
    {
        var copy = (MediaPart)MemberwiseClone();
        copy.MediaSegments = segments;
        return copy;
    }
}