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
        public override bool CanProcess(ExtractorType extractorType, string method, string keyUriText, string m3u8Content, ParserConfig paserConfig) => extractorType == ExtractorType.HLS;


        public override EncryptInfo Process(string method, string keyUriText, string m3u8Content, ParserConfig parserConfig)
        {
            var encryptInfo = new EncryptInfo(method);

            if (keyUriText.ToLower().StartsWith("base64:"))
            {
                encryptInfo.Key = Convert.FromBase64String(keyUriText[7..]);
            }
            else if (keyUriText.ToLower().StartsWith("data:text/plain;base64,"))
            {
                encryptInfo.Key = Convert.FromBase64String(keyUriText[23..]);
            }
            else if (!string.IsNullOrEmpty(keyUriText)) 
            {
                var segUrl = PreProcessUrl(ParserUtil.CombineURL(parserConfig.BaseUrl, keyUriText), parserConfig);
                var bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers).Result;
                encryptInfo.Key = bytes;
            }

            return encryptInfo;
        }

        /// <summary>
        /// 预处理URL
        /// </summary>
        private string PreProcessUrl(string url, ParserConfig parserConfig)
        {
            foreach (var p in parserConfig.UrlProcessors)
            {
                if (p.CanProcess(ExtractorType.HLS, url, parserConfig))
                {
                    url = p.Process(url, parserConfig);
                }
            }

            return url;
        }
    }
}
