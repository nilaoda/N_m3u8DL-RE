using N_m3u8DL_RE.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class EncryptInfo
    {
        /// <summary>
        /// 加密方式，默认无加密
        /// </summary>
        public EncryptMethod Method { get; set; } = EncryptMethod.NONE;

        public byte[]? Key { get; set; }
        public byte[]? IV { get; set; }
    }
}
