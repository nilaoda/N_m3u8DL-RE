using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Enum
{
    public enum EncryptMethod
    {
        NONE,
        AES_128,
        AES_128_ECB,
        SAMPLE_AES,
        SAMPLE_AES_CTR,
        CENC,
        CHACHA20,
        UNKNOWN
    }
}
