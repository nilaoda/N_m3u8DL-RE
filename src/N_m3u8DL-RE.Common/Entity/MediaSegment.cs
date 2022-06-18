using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class MediaSegment
    {
        public int Index { get; set; }

        public int TargetDuration { get; set; }
        public double Duration { get; set; }
        public string? Title { get; set; }

        public long StartRange { get; set; } = 0L;
        public long StopRange { get => StartRange + ExpectLength - 1; }
        public long ExpectLength { get; set; } = -1L;

        public EncryptInfo EncryptInfo { get; set; } = new EncryptInfo();

        public string Url { get; set; }
    }
}
