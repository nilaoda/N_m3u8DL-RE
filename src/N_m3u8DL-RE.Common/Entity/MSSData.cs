using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class MSSData
    {
        public string FourCC { get; set; } = "";
        public string CodecPrivateData { get; set; } = "";
        public string Type { get; set; } = "";
        public int Timesacle { get; set; }
        public int SamplingRate { get; set; }
        public int Channels { get; set; }
        public int BitsPerSample { get; set; }
        public int NalUnitLengthField { get; set; }
        public long Duration { get; set; }

        public bool IsProtection { get; set; } = false;
        public string ProtectionSystemID { get; set; } = "";
        public string ProtectionData { get; set; } = "";
    }
}
