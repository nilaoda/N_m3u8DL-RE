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
            if (paserConfig.AppendUrlParams)
            {
                var uriFromConfig = new Uri(paserConfig.Url);
                var newUri = new Uri(oriUrl);
                var newQuery = (newUri.Query.TrimStart('?') + "&" + uriFromConfig.Query.TrimStart('?')).Trim('&');
                if (!oriUrl.Contains(uriFromConfig.Query))
                {
                    Logger.Debug("Before: " + oriUrl);
                    oriUrl = (newUri.GetLeftPart(UriPartial.Path) + "?" + newQuery).TrimEnd('?');
                    Logger.Debug("After: " + oriUrl);
                }
            }

            return oriUrl;
        }
    }
}
