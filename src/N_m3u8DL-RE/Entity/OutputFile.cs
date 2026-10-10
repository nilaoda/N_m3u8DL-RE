using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.Entity;

internal class OutputFile
{
    public MediaType? MediaType { get; set; }
    public required int Index { get; set; }
    public required string FilePath { get; set; }
    public double? Duration { get; set; }
    public string? LangCode { get; set; }
    public string? Description { get; set; }
    public List<Mediainfo> Mediainfos { get; set; } = [];
    // 多 Period 输出已经按共同时间轴对齐，混流时不能逐轨道重新归零。
    public bool PreserveTimestamp { get; set; }
}