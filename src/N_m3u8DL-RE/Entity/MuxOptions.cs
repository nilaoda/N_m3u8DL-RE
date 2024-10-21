using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Entity;

internal class MuxOptions
{
    public bool UseMkvmerge { get; set; } = false;
    public MuxFormat MuxFormat { get; set; } = MuxFormat.MP4;
    public bool KeepFiles { get; set; } = false;
    public bool SkipSubtitle { get; set; } = false;
    public string? BinPath { get; set; }
}