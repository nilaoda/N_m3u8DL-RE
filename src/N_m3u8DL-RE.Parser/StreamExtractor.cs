using N_m3u8DL_RE.Common.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Extractor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser
{
    public class StreamExtractor
    {
        private IExtractor extractor;
        private ParserConfig parserConfig = new ParserConfig();
        private string rawText;

        public StreamExtractor(ParserConfig parserConfig)
        {
            this.parserConfig = parserConfig;
        }

        public void LoadSourceFromUrl(string url)
        {
            Logger.Info(ResString.loadingUrl + url);
            if (url.StartsWith("file:"))
            {
                var uri = new Uri(url);
                this.rawText = File.ReadAllText(uri.LocalPath);
                parserConfig.Url = url;
            }
            else if (url.StartsWith("http"))
            {
                this.rawText = HTTPUtil.GetWebSourceAsync(url, parserConfig.Headers).Result;
                parserConfig.Url = url;
            }
            this.rawText = rawText.Trim();
            LoadSourceFromText(this.rawText);
        }

        public void LoadSourceFromText(string rawText)
        {
            rawText = rawText.Trim();
            this.rawText = rawText;
            if (rawText.StartsWith(HLSTags.ext_m3u))
            {
                Logger.InfoMarkUp(ResString.matchHLS);
                extractor = new HLSExtractor(parserConfig);
            }
            else if (rawText.StartsWith(".."))
            {

            }
            else
            {
                throw new Exception(ResString.notSupported);
            }
        }

        public Task<List<StreamSpec>> ExtractStreamsAsync()
        {
            return extractor.ExtractStreamsAsync(rawText);
        }
    }
}
