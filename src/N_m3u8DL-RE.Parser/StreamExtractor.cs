using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Parser
{
    public class StreamExtractor
    {
        public IExtractor Extractor { get; private set; }
        private ParserConfig parserConfig = new ParserConfig();
        private string rawText;
        private static SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);

        public StreamExtractor()
        {

        }

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
                (this.rawText, url) = HTTPUtil.GetWebSourceAndNewUrlAsync(url, parserConfig.Headers).Result;
                parserConfig.Url = url;
            }
            else if (File.Exists(url))
            {
                url = Path.GetFullPath(url);
                this.rawText = File.ReadAllText(url);
                parserConfig.Url = new Uri(url).AbsoluteUri;
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
                Extractor = new HLSExtractor(parserConfig);
            }
            else if (rawText.Contains("</MPD>") && rawText.Contains("<MPD"))
            {
                Logger.InfoMarkUp(ResString.matchDASH);
                //extractor = new DASHExtractor(parserConfig);
                Extractor = new DASHExtractor2(parserConfig);
            }
            else
            {
                throw new NotSupportedException(ResString.notSupported);
            }
        }

        /// <summary>
        /// 开始解析流媒体信息
        /// </summary>
        /// <returns></returns>
        public async Task<List<StreamSpec>> ExtractStreamsAsync()
        {
            try
            {
                await semaphore.WaitAsync();
                Logger.Info(ResString.parsingStream);
                return await Extractor.ExtractStreamsAsync(rawText);
            }
            finally
            {
                semaphore.Release();
            }
        }

        /// <summary>
        /// 根据规格说明填充媒体播放列表信息
        /// </summary>
        /// <param name="streamSpecs"></param>
        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            try
            {
                await semaphore.WaitAsync();
                Logger.Info(ResString.parsingStream);
                await Extractor.FetchPlayListAsync(streamSpecs);
            }
            finally
            {
                semaphore.Release();
            }
        }
    }
}
