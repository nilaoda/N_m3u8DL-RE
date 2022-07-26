using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor;
using N_m3u8DL_RE.Parser.Processor.HLS;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Processor
{
    internal class DemoProcessor2 : KeyProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig)
        {
            return extractorType == ExtractorType.HLS  && parserConfig.Url.Contains("playertest.longtailvideo.com");
        }

        public override EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig)
        {
            Logger.InfoMarkUp($"[white on green]My Key Processor => {keyLine}[/]");
            var info = new DefaultHLSKeyProcessor().Process(keyLine, m3u8Url, m3u8Content, parserConfig);
            Logger.InfoMarkUp("[red]" + HexUtil.BytesToHex(info.Key!, " ") + "[/]");
            return info;
        }
    }
}
