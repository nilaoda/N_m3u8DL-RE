using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.StreamParser.Processor;

namespace N_m3u8DL_RE.Processor
{
    internal sealed class DemoProcessor : ContentProcessor
    {

        public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig)
        {
            return extractorType == ExtractorType.MPEGDASH && parserConfig.Url.Contains("bitmovin");
        }

        public override string Process(string rawText, ParserConfig parserConfig)
        {
            Logger.InfoMarkUp("[red]Match bitmovin![/]");
            return rawText;
        }
    }
}