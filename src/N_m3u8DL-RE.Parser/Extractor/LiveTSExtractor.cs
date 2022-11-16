using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Extractor
{
    internal class LiveTSExtractor : IExtractor
    {
        public ExtractorType ExtractorType => ExtractorType.HTTP_LIVE;

        public ParserConfig ParserConfig {get; set;}

        public LiveTSExtractor(ParserConfig parserConfig)
        {
            this.ParserConfig = parserConfig;
        }

        public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            return new List<StreamSpec>()
            {
                new StreamSpec()
                {
                    OriginalUrl = ParserConfig.OriginalUrl,
                    Url = ParserConfig.Url,
                    Playlist = new Playlist(),
                    GroupId = ResString.ReLiveTs
                }
            };
        }

        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            throw new NotImplementedException();
        }

        public async void PreProcessContent()
        {
            throw new NotImplementedException();
        }

        public string PreProcessUrl(string url)
        {
            throw new NotImplementedException();
        }

        public Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
        {
            throw new NotImplementedException();
        }
    }
}
