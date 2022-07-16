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
        public override bool CanProcess(ExtractorType extractorType, string method, string uriText, ParserConfig paserConfig) => extractorType == ExtractorType.HLS;


        public override byte[]? Process(string method, string uriText, ParserConfig parserConfig)
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
            else if (!string.IsNullOrEmpty(uriText)) 
            {
                var segUrl = PreProcessUrl(ParserUtil.CombineURL(parserConfig.BaseUrl, uriText), parserConfig);
                var bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers).Result;
                encryptInfo.Key = bytes;
            }

            return encryptInfo.Key;
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
