namespace N_m3u8DL_RE.Common.Entity;

// 主要处理 EXT-X-DISCONTINUITY
public class MediaPart
{
    public List<MediaSegment> MediaSegments { get; set; } = [];
}