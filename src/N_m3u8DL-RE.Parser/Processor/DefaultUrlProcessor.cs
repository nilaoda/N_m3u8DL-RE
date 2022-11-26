using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor
{
    public class DefaultUrlProcessor : UrlProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig paserConfig) => true;

        public override string Process(string oriUrl, ParserConfig paserConfig)
        {
            if (paserConfig.AppendUrlParams && oriUrl.StartsWith("http")) 
            {
                var uriFromConfig = new Uri(paserConfig.Url);
                var oldUri = new Uri(oriUrl);
                var newQuery = (oldUri.Query.TrimStart('?') + "&" + uriFromConfig.Query.TrimStart('?')).Trim('&');
                var sameLeft = oldUri.GetLeftPart(UriPartial.Path) == uriFromConfig.GetLeftPart(UriPartial.Path);
                if (sameLeft && !oriUrl.Contains(uriFromConfig.Query))
                {
                    Logger.Debug("Before: " + oriUrl);
                    oriUrl = (oldUri.GetLeftPart(UriPartial.Path) + "?" + newQuery).TrimEnd('?');
                    Logger.Debug("After: " + oriUrl);
                }
            }

            return oriUrl;
        }
    }
}
