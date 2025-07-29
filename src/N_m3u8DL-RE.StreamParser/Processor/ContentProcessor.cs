using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Processor
{
    public abstract class ContentProcessor
    {
        public abstract bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig);
        public abstract string Process(string rawText, ParserConfig parserConfig);
    }
}