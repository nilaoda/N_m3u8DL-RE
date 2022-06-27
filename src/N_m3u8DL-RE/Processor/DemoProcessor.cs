using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Processor
{
    internal class DemoProcessor : ContentProcessor
    {
        public override bool CanProcess(string rawText, ParserConfig parserConfig)
        {
            return parserConfig.Url.Contains("bitmovin");
        }

        public override string Process(string rawText, ParserConfig parserConfig)
        {
            Logger.InfoMarkUp("[red]Match bitmovin![/]");
            return rawText;
        }
    }
}
