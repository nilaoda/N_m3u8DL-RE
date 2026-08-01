using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Web.Services;

public sealed class BrowserParserService
{
    public async Task<BrowserParseResult> ParseAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Please provide a playlist URL or local .m3u8 file URL.");
        }

        var parserConfig = new ParserConfig
        {
            AppendUrlParams = false,
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["user-agent"] = "Mozilla/5.0 (compatible; N_m3u8DL-RE-Web/1.0)"
            }
        };

        var extractor = new StreamExtractor(parserConfig);
        await extractor.LoadSourceFromUrlAsync(input);
        var streams = await extractor.ExtractStreamsAsync();

        var streamItems = streams
            .OrderBy(x => x.MediaType)
            .ThenByDescending(x => x.Bandwidth)
            .Select(x => new BrowserStreamItem(
                Guid.NewGuid().ToString("N"),
                x.MediaType?.ToString() ?? "UNKNOWN",
                x.Language ?? "und",
                x.Codecs ?? "",
                x.Resolution ?? "",
                x.Bandwidth ?? 0,
                x.Url ?? string.Empty,
                x.GroupId ?? string.Empty,
                x.Name ?? string.Empty))
            .ToList();

        return new BrowserParseResult(
            extractor.ExtractorType.ToString(),
            streamItems,
            parserConfig.OriginalUrl);
    }
}

public sealed record BrowserParseResult(string ExtractorType, IReadOnlyList<BrowserStreamItem> Streams, string OriginalUrl);

public sealed record BrowserStreamItem(
    string Id,
    string MediaType,
    string Language,
    string Codecs,
    string Resolution,
    long? Bandwidth,
    string Url,
    string GroupId,
    string Name);
