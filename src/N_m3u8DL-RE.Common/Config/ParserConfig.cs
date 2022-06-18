using N_m3u8DL_RE.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Config
{
    public class ParserConfig
    {
        public string Url { get; set; }
        public string BaseUrl { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>()
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
        };

        /// <summary>
        /// 自定义的加密方式 默认AES_128_CBC
        /// </summary>
        public EncryptMethod CustomMethod { get; set; } = EncryptMethod.AES_128;

        /// <summary>
        /// 自定义的解密KEY
        /// </summary>
        public byte[]? CustomeKey { get; set; }

        /// <summary>
        /// 自定义的解密IV
        /// </summary>
        public byte[]? CustomeIV { get; set; }

    }
}
