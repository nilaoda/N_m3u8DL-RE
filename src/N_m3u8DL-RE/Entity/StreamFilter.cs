using N_m3u8DL_RE.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Entity
{
    public class StreamFilter
    {
        public Regex? GroupIdReg { get; set; }
        public Regex? LanguageReg { get; set; }
        public Regex? NameReg { get; set; }
        public Regex? CodecsReg { get; set; }
        public Regex? ResolutionReg { get; set; }
        public Regex? FrameRateReg { get; set; }
        public Regex? ChannelsReg { get; set; }
        public Regex? VideoRangeReg { get; set; }
        public Regex? UrlReg { get; set; }
        public long? SegmentsMinCount { get; set; }
        public long? SegmentsMaxCount { get; set; }

        public string For { get; set; } = "best";
    }
}
