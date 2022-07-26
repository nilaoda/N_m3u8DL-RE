using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor
{
    public abstract class KeyProcessor
    {
        public abstract bool CanProcess(ExtractorType extractorType, string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
        public abstract EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
    }
}
