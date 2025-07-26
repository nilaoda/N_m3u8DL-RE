using System.Web;

using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Processor
{
    public class DefaultUrlProcessor : UrlProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig paserConfig) => paserConfig.AppendUrlParams;

        public override string Process(string oriUrl, ParserConfig paserConfig)
        {
            if (!oriUrl.StartsWith("http"))
            {
                return oriUrl;
            }

            Uri uriFromConfig = new Uri(paserConfig.Url);
            System.Collections.Specialized.NameValueCollection uriFromConfigQuery = HttpUtility.ParseQueryString(uriFromConfig.Query);

            Uri oldUri = new Uri(oriUrl);
            System.Collections.Specialized.NameValueCollection newQuery = HttpUtility.ParseQueryString(oldUri.Query);
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