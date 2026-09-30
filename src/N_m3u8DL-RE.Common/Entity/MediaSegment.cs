using N_m3u8DL_RE.Common.Enum;
using System.Text.Json.Serialization;

namespace N_m3u8DL_RE.Common.Entity;

public class MediaSegment
{
    public long Index { get; set; }
    // 直播录制中的顺序；Index 始终保留源播放列表里的序号。
    [JsonIgnore]
    public long? RecordingIndex { get; set; }
    public double Duration { get; set; }
    // DASH 源媒体时间（秒），独立于全局下载序号，供范围下载和 PTO 裁剪使用。
    public double? PresentationTime { get; set; }
    // HLS 点播在过滤前记录的不连续段内时间，删除广告后仍能定位字幕的源时间。
    public double? HlsTime { get; set; }
    // 点播过滤前的源播放时间；与媒体 PTS、删除后的输出时间分开保存。
    public double? SourceTime { get; set; }
    public string? Title { get; set; }
    public DateTime? DateTime { get; set; }

    public long? StartRange { get; set; }
    public long? StopRange => (StartRange != null && ExpectLength != null) ? StartRange + ExpectLength - 1 : null;
    public long? ExpectLength { get; set; }

    public EncryptInfo EncryptInfo { get; set; } = new();
    
    public bool IsEncrypted => EncryptInfo.Method != EncryptMethod.NONE;

    public string Url { get; set; } = string.Empty;

    public string? NameFromVar { get; set; } // MPD分段文件名

    public MediaSegment WithIndex(long index)
    {
        var copy = (MediaSegment)MemberwiseClone();
        copy.Index = index;
        return copy;
    }

    public override bool Equals(object? obj)
    {
        return obj is MediaSegment segment &&
               Index == segment.Index &&
               RecordingIndex == segment.RecordingIndex &&
               Math.Abs(Duration - segment.Duration) < 0.001 &&
               Title == segment.Title &&
               StartRange == segment.StartRange &&
               StopRange == segment.StopRange &&
               ExpectLength == segment.ExpectLength &&
               Url == segment.Url;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Index, RecordingIndex, Duration, Title, StartRange, StopRange, ExpectLength, Url);
    }
}
