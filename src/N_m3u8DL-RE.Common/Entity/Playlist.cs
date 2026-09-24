namespace N_m3u8DL_RE.Common.Entity;

public class Playlist
{
    // 对应Url信息
    public string Url { get; set; } = string.Empty;
    // 是否直播
    public bool IsLive { get; set; } = false;
    // 直播刷新间隔毫秒（默认15秒）
    public double RefreshIntervalMs { get; set; } = 15000;
    // 所有分片时长总和
    public double TotalDuration => MediaParts.Sum(x => x.MediaSegments.Sum(m => m.Duration));

    // 所有分片中最长时长
    public double? TargetDuration { get; set; }
    // INIT信息
    public MediaSegment? MediaInit { get; set; }
    // 分片信息
    public List<MediaPart> MediaParts { get; set; } = [];
}