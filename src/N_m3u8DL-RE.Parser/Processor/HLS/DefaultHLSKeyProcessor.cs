using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor.HLS
{
    public class DefaultHLSKeyProcessor : KeyProcessor
    {
        public override bool CanProcess(string method, string uriText, string ivText, ParserConfig paserConfig) => true;

        public override EncryptInfo Process(string method, string uriText, string ivText, int segIndex, ParserConfig parserConfig)
        {
            var encryptInfo = new EncryptInfo();


            if (uriText.ToLower().StartsWith("base64:"))
            {
                encryptInfo.Key = Convert.FromBase64String(uriText.Substring(7));
            }
            else if (uriText.ToLower().StartsWith("data:text/plain;base64,"))
            {
                encryptInfo.Key = Convert.FromBase64String(uriText.Substring(23));
            }
            else
            {
                var segUrl = PreProcessUrl(ParserUtil.CombineURL(parserConfig.BaseUrl, uriText), parserConfig);
                var bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers).Result;
                encryptInfo.Key = bytes;
            }

            //加密方式
            if (Enum.TryParse(method.Replace("-", "_"), out EncryptMethod m))
            {
                encryptInfo.Method = m;
            }
            else
            {
                encryptInfo.Method = EncryptMethod.UNKNOWN;
            }
            //没有读取到IV，自己生成
            if (string.IsNullOrEmpty(ivText))
            {
                encryptInfo.IV = HexUtil.HexToBytes(Convert.ToString(segIndex, 16).PadLeft(32, '0'));
            }
            else
            {
                encryptInfo.IV = HexUtil.HexToBytes(ivText);
            }

            return encryptInfo;
        }

        /// <summary>
        /// 预处理URL
        /// </summary>
        private string PreProcessUrl(string url, ParserConfig parserConfig)
        {
            foreach (var p in parserConfig.HLSUrlProcessors)
            {
                if (p.CanProcess(url, parserConfig))
                {
                    url = p.Process(url, parserConfig);
                }
            }

            return url;
        }
    }
}
