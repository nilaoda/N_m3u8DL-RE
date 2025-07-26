using N_m3u8DL_RE.Enumerations;

namespace N_m3u8DL_RE.Entity
{
    internal class MuxOptions
    {
        public bool UseMkvmerge { get; set; }
        public MuxFormat MuxFormat { get; set; } = MuxFormat.MP4;
        public bool KeepFiles { get; set; }
        public bool SkipSubtitle { get; set; }
        public string? BinPath { get; set; }
    }
}