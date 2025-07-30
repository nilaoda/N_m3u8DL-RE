using System.Collections.Specialized;
using System.Web;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Processor
{
    public class DefaultUrlProcessor : UrlProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig)
        {
            return parserConfig.AppendUrlParams;
        }

        public override string Process(string oriUrl, ParserConfig parserConfig)
        {
            if (!oriUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return oriUrl;
            }

            Uri uriFromConfig = new(parserConfig.Url);
            NameValueCollection uriFromConfigQuery = HttpUtility.ParseQueryString(uriFromConfig.Query);

            Uri oldUri = new(oriUrl);
            NameValueCollection newQuery = HttpUtility.ParseQueryString(oldUri.Query);
            foreach (string? item in uriFromConfigQuery.AllKeys)
            {
                if (newQuery.AllKeys.Contains(item))
                {
                    newQuery.Set(item, uriFromConfigQuery.Get(item));
                }
                else
                {
                    newQuery.Add(item, uriFromConfigQuery.Get(item));
                }
            }

            if (string.IsNullOrEmpty(newQuery.ToString()))
            {
                return oriUrl;
            }

            Logger.Debug("Before: " + oriUrl);
            oriUrl = (oldUri.GetLeftPart(UriPartial.Path) + "?" + newQuery).TrimEnd('?');
            Logger.Debug("After: " + oriUrl);

            return oriUrl;
        }
    }
}