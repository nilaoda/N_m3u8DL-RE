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

        public EncryptInfo() { }

        /// <summary>
        /// 创建EncryptInfo并尝试自动解析Method
        /// </summary>
        /// <param name="method"></param>
        public EncryptInfo(string method)
        {
            Method = ParseMethod(method);
        }

        public static EncryptMethod ParseMethod(string? method)
        {
            if (method != null && System.Enum.TryParse(method.Replace("-", "_"), out EncryptMethod m))
            {
                return m;
            }
            else
            {
                return EncryptMethod.UNKNOWN;
            }
        }
    }
}
