using N_m3u8DL_RE.Common.Config;
using N_m3u8DL_RE.Common.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Extractor
{
    internal interface IExtractor
    {
        ParserConfig ParserConfig { get; set; }

        Task<List<StreamSpec>> ExtractStreamsAsync(string rawText);

        Task FetchPlayListAsync(List<StreamSpec> streamSpecs);
    }
}
