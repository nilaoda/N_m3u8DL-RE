using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor
{
    public abstract class UrlProcessor
    {
        public abstract bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig parserConfig);
        public abstract string Process(string oriUrl, ParserConfig parserConfig);
    }
}
