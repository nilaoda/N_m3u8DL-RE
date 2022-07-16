using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor
{
    public partial class DefaultUrlProcessor : UrlProcessor
    {
        [RegexGenerator("\\?.*")]
        private static partial Regex ParaRegex();

        public override bool CanProcess(ExtractorType extractorType, string oriUrl, ParserConfig paserConfig) => true;

        public override string Process(string oriUrl, ParserConfig paserConfig)
        {
            if (paserConfig.AppendUrlParams)
            {
                Logger.Debug("Before: " + oriUrl);
                oriUrl += ParaRegex().Match(paserConfig.Url).Value;
                Logger.Debug("After: " + oriUrl);
            }

            return oriUrl;
        }
    }
}
