using N_m3u8DL_RE.Common.Entity;
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
        public abstract bool CanProcess(string method, string uriText, ParserConfig parserConfig);
        public abstract byte[] Process(string method, string uriText, ParserConfig parserConfig);
    }
}
