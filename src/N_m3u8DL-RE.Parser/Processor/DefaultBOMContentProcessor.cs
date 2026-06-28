using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Processor;

/// <summary>
/// 去除字符串开头的UTF-8 BOM (U+FEFF)，兼容DASH/HLS/MSS
/// </summary>
public class DefaultBOMContentProcessor : ContentProcessor
{
    public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig)
    {
        return rawText.Length > 0 && rawText[0] == '\uFEFF';
    }

    public override string Process(string rawText, ParserConfig parserConfig)
    {
        return rawText[1..];
    }
}
