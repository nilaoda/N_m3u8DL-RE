using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Processor
{
    public abstract class UrlProcessor
    {
        public abstract bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig);
        public abstract string Process(string oriUrl, ParserConfig parserConfig);
    }
}