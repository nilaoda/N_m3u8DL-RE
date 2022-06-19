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

        /// <summary>
        /// 组装视频分段的URL时，是否要把原本URL后的参数也加上去
        /// 如 Base URL = "http://xxx.com/playlist.m3u8?hmac=xxx&token=xxx"
        /// 相对路径 = clip_01.ts
        /// 如果 AppendUrlParams=false，得 http://xxx.com/clip_01.ts
        /// 如果 AppendUrlParams=true，得 http://xxx.com/clip_01.ts?hmac=xxx&token=xxx
        /// </summary>
        public bool AppendUrlParams { get; set; } = false;

    }
}
