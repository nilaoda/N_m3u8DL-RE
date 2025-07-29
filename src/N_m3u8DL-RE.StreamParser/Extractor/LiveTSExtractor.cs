using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Extractor
{
    internal sealed class LiveTSExtractor(ParserConfig parserConfig) : IExtractor
    {
        public ExtractorType ExtractorType => ExtractorType.HTTPLIVE;

        public ParserConfig ParserConfig { get; set; } = parserConfig;

        public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            return Task.FromResult(new List<StreamSpec>
            {
                new()
                {
                    OriginalUrl = ParserConfig.OriginalUrl,
                    Url = ParserConfig.Url,
                    Playlist = new Playlist(),
                    GroupId = ResString.ReLiveTs
                }
            });
        }

        public Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            throw new NotImplementedException();
        }

        public void PreProcessContent()
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