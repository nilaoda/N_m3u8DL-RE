using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using System.Web;

namespace N_m3u8DL_RE.Parser.Processor;

public class DefaultUrlProcessor : UrlProcessor
{
    public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig paserConfig) => paserConfig.AppendUrlParams;

    public override string Process(string oriUrl, ParserConfig paserConfig)
    {
        if (!oriUrl.StartsWith("http")) return oriUrl;
        
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

        if (string.IsNullOrEmpty(newQuery.ToString())) return oriUrl;
        
        Logger.Debug("Before: " + oriUrl);
        oriUrl = (oldUri.GetLeftPart(UriPartial.Path) + "?" + newQuery).TrimEnd('?');
        Logger.Debug("After: " + oriUrl);

        return oriUrl;
    }
}