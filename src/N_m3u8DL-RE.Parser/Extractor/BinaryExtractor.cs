using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Extractor;

/// <summary>二进制直链的占位提取器；下载由主程序直接处理。</summary>
internal sealed class BinaryExtractor(ParserConfig parserConfig) : IExtractor
{
    public ExtractorType ExtractorType => ExtractorType.BINARY;
    public ParserConfig ParserConfig { get; set; } = parserConfig;
    public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText) => Task.FromResult(new List<StreamSpec>());
    public Task FetchPlayListAsync(List<StreamSpec> streamSpecs) => Task.CompletedTask;
    public Task RefreshPlayListAsync(List<StreamSpec> streamSpecs) => Task.CompletedTask;
    public void PreProcessContent() { }
    public string PreProcessUrl(string url) => url;
}
