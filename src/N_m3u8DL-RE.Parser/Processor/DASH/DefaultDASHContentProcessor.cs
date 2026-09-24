using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Processor.DASH;

/// <summary>
/// MPD自动补充Namespace
/// </summary>
public class DefaultDASHContentProcessor : ContentProcessor
{
    private static readonly Dictionary<string, string> NamespaceMap = new()
    {
        ["cenc"] = "urn:mpeg:cenc:2013",
        ["mspr"] = "urn:microsoft:playready",
        ["mas"] = "urn:marlin:mas:1-0:services:schemas:mpd",
    };
    
    public override bool CanProcess(ExtractorType extractorType, string mpdContent, ParserConfig parserConfig)
    {
        if (extractorType != ExtractorType.MPEG_DASH) return false;

        return NamespaceMap.Keys.Any(x => IsMissingNs(mpdContent, x));
    }

    public override string Process(string mpdContent, ParserConfig parserConfig)
    {
        Logger.InfoMarkUp("[gray]Namespace missing, try fix...[/]");
        var missingNamespaceKeys = NamespaceMap.Keys.Where(x => IsMissingNs(mpdContent, x)).ToList();
        if (missingNamespaceKeys.Count == 0)
            return mpdContent;
        
        var missingNamespaceDfns = missingNamespaceKeys.Select(key => $"xmlns:{key}=\"{NamespaceMap[key]}\"");
        var declarations = string.Join(" ", missingNamespaceDfns);
        return ReplaceFirst(mpdContent, "<MPD ", $"<MPD {declarations} ");
    }
    
    private static bool IsMissingNs(string rawText, string tag)
    {
        return !rawText.Contains($"xmlns:{tag}") && rawText.Contains($"<{tag}:");
    }

    // 替换字符串中第一次出现的指定子字符串。
    private static string ReplaceFirst(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? source :
            source.Remove(index, oldValue.Length).Insert(index, newValue);
    }
}