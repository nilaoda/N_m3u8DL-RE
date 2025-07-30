using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Processor
{
    public abstract class KeyProcessor
    {
        public abstract bool CanProcess(ExtractorType extractorType, string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
        public abstract EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
    }
}