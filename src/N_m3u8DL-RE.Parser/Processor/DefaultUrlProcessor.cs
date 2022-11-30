using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace N_m3u8DL_RE.Parser.Processor
{
    public class DefaultUrlProcessor : UrlProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig paserConfig) => paserConfig.AppendUrlParams;

        public override string Process(string oriUrl, ParserConfig paserConfig)
        {
            if (oriUrl.StartsWith("http")) 
            {
                var uriFromConfig = new Uri(paserConfig.Url);
                var uriFromConfigQuery = HttpUtility.ParseQueryString(uriFromConfig.Query);

                var oldUri = new Uri(oriUrl);
                var newQuery = HttpUtility.ParseQueryString(oldUri.Query);
                foreach (var item in uriFromConfigQuery.AllKeys)
                {
                    if (newQuery.AllKeys.Contains(item))
                        newQuery.Set(item, uriFromConfigQuery.Get(item));
                    else
                        newQuery.Add(item, uriFromConfigQuery.Get(item));
                }

                if (!string.IsNullOrEmpty(newQuery.ToString()))
                {
                    Logger.Debug("Before: " + oriUrl);
                    oriUrl = (oldUri.GetLeftPart(UriPartial.Path) + "?" + newQuery.ToString()).TrimEnd('?');
                    Logger.Debug("After: " + oriUrl);
                }
            }

            return oriUrl;
        }
    }
}
