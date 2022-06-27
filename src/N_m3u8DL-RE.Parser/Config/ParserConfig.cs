using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Processor;
using N_m3u8DL_RE.Parser.Processor.DASH;
using N_m3u8DL_RE.Parser.Processor.HLS;

namespace N_m3u8DL_RE.Parser.Config
{
    public class ParserConfig
    {
        public string Url { get; set; }

        public string BaseUrl { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>()
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
        };

        /// <summary>
        /// HLS内容前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<ContentProcessor> HLSContentProcessors { get; } = new List<ContentProcessor>() { new DefaultHLSContentProcessor() };

        /// <summary>
        /// DASH内容前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<ContentProcessor> DASHContentProcessors { get; } = new List<ContentProcessor>() { new DefaultDASHContentProcessor() };

        /// <summary>
        /// 添加分片URL前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<UrlProcessor> HLSUrlProcessors { get; } = new List<UrlProcessor>() { new DefaultUrlProcessor() };

        /// <summary>
        /// DASH内容前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<UrlProcessor> DASHUrlProcessors { get; } = new List<UrlProcessor>() { new DefaultUrlProcessor() };

        /// <summary>
        /// HLS-KEY解析器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<KeyProcessor> HLSKeyProcessors { get; } = new List<KeyProcessor>() { new DefaultHLSKeyProcessor() };


        /// <summary>
        /// 自定义的加密方式
        /// </summary>
        public EncryptMethod? CustomMethod { get; set; }

        /// <summary>
        /// 自定义的解密KEY
        /// </summary>
        public byte[]? CustomeKey { get; set; }

        /// <summary>
        /// 自定义的解密IV
        /// </summary>
        public byte[]? CustomeIV { get; set; }

        /// <summary>
        /// 组装视频分段的URL时，是否要把原本URL后的参数也加上去
        /// 如 Base URL = "http://xxx.com/playlist.m3u8?hmac=xxx&token=xxx"
        /// 相对路径 = clip_01.ts
        /// 如果 AppendUrlParams=false，得 http://xxx.com/clip_01.ts
        /// 如果 AppendUrlParams=true，得 http://xxx.com/clip_01.ts?hmac=xxx&token=xxx
        /// </summary>
        public bool AppendUrlParams { get; set; } = false;
    }
}
