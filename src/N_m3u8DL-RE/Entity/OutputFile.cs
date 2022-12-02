using N_m3u8DL_RE.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Entity
{
    internal class OutputFile
    {
        public MediaType? MediaType { get; set; }
        public required int Index { get; set; }
        public required string FilePath { get; set; }
        public string? LangCode { get; set; }
        public string? Description { get; set; }
        public List<Mediainfo> Mediainfos { get; set; } = new();
    }
}
