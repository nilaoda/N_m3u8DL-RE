﻿using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.StreamParser.Processor;
using N_m3u8DL_RE.StreamParser.Processor.DASH;
using N_m3u8DL_RE.StreamParser.Processor.HLS;

namespace N_m3u8DL_RE.StreamParser.Config
{
    public class ParserConfig
    {
        public string Url { get; set; } = string.Empty;

        public string OriginalUrl { get; set; } = string.Empty;

        public string BaseUrl { get; set; } = string.Empty;

        public Dictionary<string, string> CustomParserArgs { get; } = [];

        public Dictionary<string, string> Headers { get; init; } = [];

        /// <summary>
        /// 内容前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<ContentProcessor> ContentProcessors { get; } = [new DefaultHLSContentProcessor(), new DefaultDASHContentProcessor()];

        /// <summary>
        /// 添加分片URL前置处理器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<UrlProcessor> UrlProcessors { get; } = [new DefaultUrlProcessor()];

        /// <summary>
        /// KEY解析器. 调用顺序与列表顺序相同
        /// </summary>
        public IList<KeyProcessor> KeyProcessors { get; } = [new DefaultHLSKeyProcessor()];


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
        public bool AppendUrlParams { get; set; }

        /// <summary>
        /// 此参数将会传递给URL Processor中
        /// </summary>
        public string? UrlProcessorArgs { get; set; }

        /// <summary>
        /// KEY重试次数
        /// </summary>
        public int KeyRetryCount { get; set; } = 3;
    }
}