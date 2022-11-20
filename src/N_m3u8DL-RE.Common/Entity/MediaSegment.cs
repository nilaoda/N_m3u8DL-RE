using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class MediaSegment
    {
        public long Index { get; set; }
        public double Duration { get; set; }
        public string? Title { get; set; }
        public DateTime? DateTime { get; set; }

        public long? StartRange { get; set; }
        public long? StopRange { get => (StartRange != null && ExpectLength != null) ? StartRange + ExpectLength - 1 : null; }
        public long? ExpectLength { get; set; }

        public EncryptInfo EncryptInfo { get; set; } = new EncryptInfo();

        public string Url { get; set; }

        public string? NameFromVar { get; set; } //MPD分段文件名

        public override bool Equals(object? obj)
        {
            return obj is MediaSegment segment &&
                   Index == segment.Index &&
                   Duration == segment.Duration &&
                   Title == segment.Title &&
                   StartRange == segment.StartRange &&
                   StopRange == segment.StopRange &&
                   ExpectLength == segment.ExpectLength &&
                   Url == segment.Url;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Index, Duration, Title, StartRange, StopRange, ExpectLength, Url);
        }
    }
}
