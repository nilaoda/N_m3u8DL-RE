using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Entity
{
    internal class MuxOptions
    {
        public bool UseMkvmerge { get; set; } = false;
        public bool MuxToMp4 { get; set; } = false;
        public bool KeepFiles { get; set; } = false;
        public bool SkipSubtitle { get; set; } = false;
        public string? BinPath { get; set; }
    }
}
