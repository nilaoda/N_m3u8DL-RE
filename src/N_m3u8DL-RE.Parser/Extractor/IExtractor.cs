using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.Parser.Extractor
{
    public interface IExtractor
    {
        ExtractorType ExtractorType { get; }

        ParserConfig ParserConfig { get; set; }

        Task<List<StreamSpec>> ExtractStreamsAsync(string rawText);

        Task FetchPlayListAsync(List<StreamSpec> streamSpecs);
        Task RefreshPlayListAsync(List<StreamSpec> streamSpecs);

        string PreProcessUrl(string url);

        void PreProcessContent();
    }
}
