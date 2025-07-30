﻿using N_m3u8DL_RE.Common.CommonEnumerations;

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
            return method != null && Enum.TryParse(method.Replace("-", "_"), out EncryptMethod m) ? m : EncryptMethod.UNKNOWN;
        }
    }
}