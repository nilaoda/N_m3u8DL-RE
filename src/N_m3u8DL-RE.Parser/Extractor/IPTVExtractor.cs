using System.Text.RegularExpressions;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Util;

namespace N_m3u8DL_RE.Parser.Extractor;

internal sealed partial class IPTVExtractor(ParserConfig parserConfig) : IExtractor
{
    public ExtractorType ExtractorType => ExtractorType.IPTV_M3U;
    public ParserConfig ParserConfig { get; set; } = parserConfig;

    [GeneratedRegex("(?:^|\\s)([\\w-]+)=(?:\"([^\"]*)\"|'([^']*)'|([^\\s,]+))")]
    private static partial Regex AttributeRegex();

    internal static bool IsPlaylist(string text)
    {
        var lines = text.Split('\n').Select(line => line.Trim()).ToArray();
        // 标准 HLS 标签优先，不能将异常负时长的媒体分片误判为独立频道。
        if (lines.Any(line => line.StartsWith("#EXT-X-", StringComparison.Ordinal)))
            return false;
        return lines.Any(line => line.StartsWith("#EXTGRP:", StringComparison.Ordinal) ||
            line.StartsWith("#EXTINF:", StringComparison.Ordinal) &&
            (line[8..].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() == "-1" ||
             AttributeRegex().Matches(line).Any(match => match.Groups[1].Value.StartsWith("tvg-", StringComparison.OrdinalIgnoreCase) ||
                                                       match.Groups[1].Value.Equals("group-title", StringComparison.OrdinalIgnoreCase))));
    }

    public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
    {
        List<StreamSpec> streams = [];
        StreamSpec? pending = null;
        using var reader = new StringReader(rawText);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length == 0)
                continue;
            if (line.StartsWith("#EXTINF:", StringComparison.Ordinal))
            {
                if (pending != null)
                    throw new FormatException(ResString.iptvEntryInvalid);
                var comma = FindTitleSeparator(line);
                if (comma < 0)
                    throw new FormatException(ResString.iptvEntryInvalid);
                var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in AttributeRegex().Matches(line[..comma]))
                    attributes[match.Groups[1].Value] = match.Groups[2].Success ? match.Groups[2].Value :
                        match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value;
                pending = new StreamSpec
                {
                    OriginalUrl = ParserConfig.OriginalUrl,
                    Name = line[(comma + 1)..].Trim(),
                    GroupId = attributes.GetValueOrDefault("tvg-id"),
                    GroupTitle = attributes.GetValueOrDefault("group-title"),
                    Language = attributes.GetValueOrDefault("tvg-language")
                };
                if (string.IsNullOrEmpty(pending.Name))
                    pending.Name = attributes.GetValueOrDefault("tvg-name");
            }
            else if (line.StartsWith("#EXTGRP:", StringComparison.Ordinal) && pending != null)
            {
                pending.GroupTitle ??= line[8..].Trim();
            }
            else if (!line.StartsWith('#') && pending != null)
            {
                pending.Url = PreProcessUrl(ParserUtil.CombineURL(
                    string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.Url : ParserConfig.BaseUrl, line));
                pending.Name = string.IsNullOrEmpty(pending.Name) ? pending.GroupId ?? line : pending.Name;
                streams.Add(pending);
                pending = null;
            }
        }
        if (pending != null)
            throw new FormatException(ResString.iptvEntryInvalid);
        return Task.FromResult(streams);
    }

    private static int FindTitleSeparator(string line)
    {
        var quote = '\0';
        for (var i = 8; i < line.Length; i++)
        {
            if (quote != '\0')
            {
                if (line[i] == quote)
                    quote = '\0';
            }
            else if (line[i] is '\'' or '"')
                quote = line[i];
            else if (line[i] == ',')
                return i;
        }
        return -1;
    }

    public string PreProcessUrl(string url)
    {
        foreach (var processor in ParserConfig.UrlProcessors)
            if (processor.CanProcess(ExtractorType, url, ParserConfig))
                url = processor.Process(url, ParserConfig);
        return url;
    }

    // 频道是相互独立的直播源，不在列表解析时连接或刷新全部频道。
    public Task FetchPlayListAsync(List<StreamSpec> streamSpecs) => Task.CompletedTask;
    public Task RefreshPlayListAsync(List<StreamSpec> streamSpecs, CancellationToken cancellationToken = default, TimeSpan? requestTimeout = null) => Task.CompletedTask;
    public void PreProcessContent() { }
}
