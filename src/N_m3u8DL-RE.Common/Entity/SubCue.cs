using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class SubCue
    {
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public required string Payload { get; set; }
        public required string Settings { get; set; }

        public override bool Equals(object? obj)
        {
            return obj is SubCue cue &&
                   StartTime.Equals(cue.StartTime) &&
                   EndTime.Equals(cue.EndTime) &&
                   Payload == cue.Payload &&
                   Settings == cue.Settings;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StartTime, EndTime, Payload, Settings);
        }
    }
}
