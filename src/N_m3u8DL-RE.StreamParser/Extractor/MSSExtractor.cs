﻿using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.StreamParser.Constants;
using N_m3u8DL_RE.StreamParser.Mp4;
using N_m3u8DL_RE.StreamParser.Util;

namespace N_m3u8DL_RE.StreamParser.Extractor
{
    // Microsoft Smooth Streaming
    // https://test.playready.microsoft.com/smoothstreaming/SSWSS720H264/SuperSpeedway_720.ism/manifest
    // file:///C:/Users/nilaoda/Downloads/[MS-SSTR]-180316.pdf
    internal sealed partial class MSSExtractor : IExtractor
    {
        [GeneratedRegex("00000001\\d7([0-9a-fA-F]{6})")]
        private static partial Regex VCodecsRegex();

        ////////////////////////////////////////

        private static readonly EncryptMethod DEFAULT_METHOD = EncryptMethod.CENC;

        public ExtractorType ExtractorType => ExtractorType.MSS;

        private string IsmUrl = string.Empty;
        private string BaseUrl = string.Empty;
        private string IsmContent = string.Empty;
        public ParserConfig ParserConfig { get; set; }

        public MSSExtractor(ParserConfig parserConfig)
        {
            ParserConfig = parserConfig;
            SetInitUrl();
        }

        private void SetInitUrl()
        {
            IsmUrl = ParserConfig.Url ?? string.Empty;
            BaseUrl = !string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.BaseUrl : IsmUrl;
        }

        public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            List<StreamSpec> streamList = [];
            IsmContent = rawText;
            PreProcessContent();

            XDocument xmlDocument = XDocument.Parse(IsmContent);

            // 选中第一个SmoothStreamingMedia节点
            XElement ssmElement = xmlDocument.Elements().First(e => e.Name.LocalName == "SmoothStreamingMedia");
            string timeScaleStr = ssmElement.Attribute("TimeScale")?.Value ?? "10000000";
            string? durationStr = ssmElement.Attribute("Duration")?.Value;
            int timescale = Convert.ToInt32(timeScaleStr, CultureInfo.InvariantCulture);
            string? isLiveStr = ssmElement.Attribute("IsLive")?.Value;
            bool isLive = Convert.ToBoolean(isLiveStr ?? "FALSE", CultureInfo.InvariantCulture);

            bool isProtection = false;
            string protectionSystemId = "";
            string protectionData = "";

            // 加密检测
            XElement? protectElement = ssmElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Protection");
            if (protectElement != null)
            {
                XElement? protectionHeader = protectElement.Element("ProtectionHeader");
                if (protectionHeader != null)
                {
                    isProtection = true;
                    protectionSystemId = protectionHeader.Attribute("SystemID")?.Value ?? "9A04F079-9840-4286-AB92-E65BE0885F95";
                    protectionData = HexUtil.BytesToHex(Convert.FromBase64String(protectionHeader.Value));
                }
            }

            // 所有StreamIndex节点
            IEnumerable<XElement> streamIndexElements = ssmElement.Elements().Where(e => e.Name.LocalName == "StreamIndex");

            foreach (XElement? streamIndex in streamIndexElements)
            {
                string? type = streamIndex.Attribute("Type")?.Value; // "video" / "audio" / "text"
                string? name = streamIndex.Attribute("Name")?.Value;
                string? subType = streamIndex.Attribute("Subtype")?.Value; // text track
                // 如果有则不从QualityLevel读取
                // Bitrate = "{bitrate}" / "{Bitrate}"
                // StartTimeSubstitution = "{start time}" / "{start_time}"
                string? urlPattern = streamIndex.Attribute("Url")?.Value;
                string? language = streamIndex.Attribute("Language")?.Value;
                // 去除不规范的语言标签
                if (language?.Length != 3)
                {
                    language = null;
                }

                // 所有c节点
                IEnumerable<XElement> cElements = streamIndex.Elements().Where(e => e.Name.LocalName == "c");

                // 所有QualityLevel节点
                IEnumerable<XElement> qualityLevelElements = streamIndex.Elements().Where(e => e.Name.LocalName == "QualityLevel");

                foreach (XElement? qualityLevel in qualityLevelElements)
                {
                    urlPattern = (qualityLevel.Attribute("Url")?.Value ?? urlPattern)!
                        .Replace(MSSTags.Bitrate_BK, MSSTags.Bitrate).Replace(MSSTags.StartTime_BK, MSSTags.StartTime);
                    string fourCC = qualityLevel.Attribute("FourCC")!.Value.ToUpperInvariant();
                    string? samplingRateStr = qualityLevel.Attribute("SamplingRate")?.Value;
                    string? bitsPerSampleStr = qualityLevel.Attribute("BitsPerSample")?.Value;
                    string? nalUnitLengthFieldStr = qualityLevel.Attribute("NALUnitLengthField")?.Value;
                    string? indexStr = qualityLevel.Attribute("Index")?.Value;
                    string codecPrivateData = qualityLevel.Attribute("CodecPrivateData")?.Value ?? "";
                    string? audioTag = qualityLevel.Attribute("AudioTag")?.Value;
                    int bitrate = Convert.ToInt32(qualityLevel.Attribute("Bitrate")?.Value ?? "0", CultureInfo.InvariantCulture);
                    int width = Convert.ToInt32(qualityLevel.Attribute("MaxWidth")?.Value ?? "0", CultureInfo.InvariantCulture);
                    int height = Convert.ToInt32(qualityLevel.Attribute("MaxHeight")?.Value ?? "0", CultureInfo.InvariantCulture);
                    string? channels = qualityLevel.Attribute("Channels")?.Value;

                    StreamSpec streamSpec = new()
                    {
                        PublishTime = DateTime.Now, // 发布时间默认现在
                        Extension = "m4s",
                        OriginalUrl = ParserConfig.OriginalUrl,
                        PeriodId = indexStr,
                        Playlist = new Playlist
                        {
                            IsLive = isLive
                        }
                    };
                    streamSpec.Playlist.MediaParts.Add(new MediaPart());
                    streamSpec.GroupId = name ?? indexStr;
                    streamSpec.Bandwidth = bitrate;
                    streamSpec.Codecs = ParseCodecs(fourCC, codecPrivateData);
                    streamSpec.Language = language;
                    streamSpec.Resolution = width == 0 ? null : $"{width}x{height}";
                    streamSpec.Url = IsmUrl;
                    streamSpec.Channels = channels;
                    streamSpec.MediaType = type switch
                    {
                        "text" => MediaType.SUBTITLES,
                        "audio" => MediaType.AUDIO,
                        _ => null
                    };

                    streamSpec.Playlist.MediaInit = new MediaSegment();
                    if (!string.IsNullOrEmpty(codecPrivateData))
                    {
                        streamSpec.Playlist.MediaInit.Index = -1; // 便于排序
                        streamSpec.Playlist.MediaInit.Url = $"hex://{codecPrivateData}";
                    }

                    long currentTime = 0L;
                    int segIndex = 0;
                    Dictionary<string, object?> varDic = new()
                    {
                        [MSSTags.Bitrate] = bitrate
                    };

                    foreach (XElement? c in cElements)
                    {
                        // 每个C元素包含三个属性:@t(start time)\@r(repeat count)\@d(duration)
                        string? _startTimeStr = c.Attribute("t")?.Value;
                        string? _durationStr = c.Attribute("d")?.Value;
                        string? _repeatCountStr = c.Attribute("r")?.Value;

                        if (_startTimeStr != null)
                        {
                            currentTime = Convert.ToInt64(_startTimeStr, CultureInfo.InvariantCulture);
                        }

                        long _duration = Convert.ToInt64(_durationStr, CultureInfo.InvariantCulture);
                        long _repeatCount = Convert.ToInt64(_repeatCountStr, CultureInfo.InvariantCulture);
                        if (_repeatCount > 0)
                        {
                            // This value is one-based. (A value of 2 means two fragments in the contiguous series).
                            _repeatCount -= 1;
                        }

                        varDic[MSSTags.StartTime] = currentTime;
                        string oriUrl = ParserUtil.CombineURL(BaseUrl, urlPattern!);
                        string mediaUrl = ParserUtil.ReplaceVars(oriUrl, varDic);
                        MediaSegment mediaSegment = new()
                        {
                            Url = mediaUrl
                        };
                        if (oriUrl.Contains(MSSTags.StartTime))
                        {
                            mediaSegment.NameFromVar = currentTime.ToString(CultureInfo.InvariantCulture);
                        }

                        mediaSegment.Duration = _duration / (double)timescale;
                        mediaSegment.Index = segIndex++;
                        streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                        if (_repeatCount < 0)
                        {
                            // 负数表示一直重复 直到period结束 注意减掉已经加入的1个片段
                            _repeatCount = (long)Math.Ceiling(Convert.ToInt64(durationStr, CultureInfo.InvariantCulture) / (double)_duration) - 1;
                        }
                        for (long i = 0; i < _repeatCount; i++)
                        {
                            currentTime += _duration;
                            MediaSegment _mediaSegment = new();
                            varDic[MSSTags.StartTime] = currentTime;
                            string _oriUrl = ParserUtil.CombineURL(BaseUrl, urlPattern!);
                            string _mediaUrl = ParserUtil.ReplaceVars(_oriUrl, varDic);
                            _mediaSegment.Url = _mediaUrl;
                            _mediaSegment.Index = segIndex++;
                            _mediaSegment.Duration = _duration / (double)timescale;
                            if (_oriUrl.Contains(MSSTags.StartTime))
                            {
                                _mediaSegment.NameFromVar = currentTime.ToString(CultureInfo.InvariantCulture);
                            }

                            streamSpec.Playlist.MediaParts[0].MediaSegments.Add(_mediaSegment);
                        }
                        currentTime += _duration;
                    }

                    // 生成MOOV数据
                    if (MSSMoovProcessor.CanHandle(fourCC!))
                    {
                        streamSpec.MSSData = new MSSData()
                        {
                            FourCC = fourCC!,
                            CodecPrivateData = codecPrivateData,
                            Type = type!,
                            Timesacle = Convert.ToInt32(timeScaleStr, CultureInfo.InvariantCulture),
                            Duration = Convert.ToInt64(durationStr, CultureInfo.InvariantCulture),
                            SamplingRate = Convert.ToInt32(samplingRateStr ?? "48000", CultureInfo.InvariantCulture),
                            Channels = Convert.ToInt32(channels ?? "2", CultureInfo.InvariantCulture),
                            BitsPerSample = Convert.ToInt32(bitsPerSampleStr ?? "16", CultureInfo.InvariantCulture),
                            NalUnitLengthField = Convert.ToInt32(nalUnitLengthFieldStr ?? "4", CultureInfo.InvariantCulture),
                            IsProtection = isProtection,
                            ProtectionData = protectionData,
                            ProtectionSystemID = protectionSystemId,
                        };
                        MSSMoovProcessor processor = new(streamSpec);
                        byte[] header = processor.GenHeader(); // trackId可能不正确
                        streamSpec.Playlist!.MediaInit!.Url = $"base64://{Convert.ToBase64String(header)}";
                        // 为音视频写入加密信息
                        if (isProtection && type != "text")
                        {
                            if (streamSpec.Playlist.MediaInit != null)
                            {
                                streamSpec.Playlist.MediaInit.EncryptInfo.Method = DEFAULT_METHOD;
                            }
                            foreach (MediaSegment item in streamSpec.Playlist.MediaParts[0].MediaSegments)
                            {
                                item.EncryptInfo.Method = DEFAULT_METHOD;
                            }
                        }
                        streamList.Add(streamSpec);
                    }
                    else
                    {
                        Logger.WarnMarkUp($"[green]{fourCC}[/] not supported! Skiped.");
                    }
                }
            }

            // 为视频设置默认轨道
            List<StreamSpec> aL = [.. streamList.Where(s => s.MediaType == MediaType.AUDIO)];
            List<StreamSpec> sL = [.. streamList.Where(s => s.MediaType == MediaType.SUBTITLES)];
            foreach (StreamSpec? item in streamList.Where(item => !string.IsNullOrEmpty(item.Resolution)))
            {
                if (aL.Count != 0)
                {
                    item.AudioId = aL.First().GroupId;
                }
                if (sL.Count != 0)
                {
                    item.SubtitleId = sL.First().GroupId;
                }
            }

            return Task.FromResult(streamList);
        }

        /// <summary>
        /// 解析编码
        /// </summary>
        /// <param name="fourCC"></param>
        /// <returns></returns>
        private static string? ParseCodecs(string fourCC, string? privateData)
        {
            return fourCC == "TTML"
                ? "stpp"
                : string.IsNullOrEmpty(privateData)
                ? null
                : fourCC switch
                {
                    // AVC视频
                    "H264" or "X264" or "DAVC" or "AVC1" => ParseAVCCodecs(privateData),
                    // AAC音频
                    "AAC" or "AACL" or "AACH" or "AACP" => ParseAACCodecs(fourCC, privateData),
                    // 默认返回fourCC本身
                    _ => fourCC.ToLowerInvariant()
                };
        }

        private static string ParseAVCCodecs(string privateData)
        {
            string result = VCodecsRegex().Match(privateData).Groups[1].Value;
            return string.IsNullOrEmpty(result) ? "avc1.4D401E" : $"avc1.{result}";
        }

        private static string ParseAACCodecs(string fourCC, string privateData)
        {
            int mpProfile = 2;
            if (fourCC == "AACH")
            {
                mpProfile = 5; // High Efficiency AAC Profile
            }
            else if (!string.IsNullOrEmpty(privateData))
            {
                mpProfile = (Convert.ToByte(privateData[..2], 16) & 0xF8) >> 3;
            }

            return $"mp4a.40.{mpProfile}";
        }

        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            // 这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
        }

        private Task ProcessUrlAsync(List<StreamSpec> streamSpecs)
        {
            foreach (StreamSpec streamSpec in streamSpecs)
            {
                Playlist? playlist = streamSpec.Playlist;
                if (playlist == null)
                {
                    continue;
                }

                if (playlist.MediaInit != null)
                {
                    playlist.MediaInit!.Url = PreProcessUrl(playlist.MediaInit!.Url);
                }
                for (int ii = 0; ii < playlist!.MediaParts.Count; ii++)
                {
                    MediaPart part = playlist.MediaParts[ii];
                    foreach (MediaSegment segment in part.MediaSegments)
                    {
                        segment.Url = PreProcessUrl(segment.Url);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public string PreProcessUrl(string url)
        {
            foreach (Processor.UrlProcessor p in ParserConfig.UrlProcessors)
            {
                if (p.CanProcess(ExtractorType, url, ParserConfig))
                {
                    url = p.Process(url, ParserConfig);
                }
            }

            return url;
        }

        public void PreProcessContent()
        {
            foreach (Processor.ContentProcessor p in ParserConfig.ContentProcessors)
            {
                if (p.CanProcess(ExtractorType, IsmContent, ParserConfig))
                {
                    IsmContent = p.Process(IsmContent, ParserConfig);
                }
            }
        }

        public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
        {
            if (streamSpecs.Count == 0)
            {
                return;
            }

            (string rawText, string url) = ("", ParserConfig.Url);
            try
            {
                (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.Url, ParserConfig.Headers);
            }
            catch (HttpRequestException) when (ParserConfig.Url != ParserConfig.OriginalUrl)
            {
                // 当URL无法访问时，再请求原始URL
                (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.OriginalUrl, ParserConfig.Headers);
            }

            ParserConfig.Url = url;
            SetInitUrl();

            List<StreamSpec> newStreams = await ExtractStreamsAsync(rawText);
            foreach (StreamSpec streamSpec in streamSpecs)
            {
                // 有的网站每次请求MPD返回的码率不一致，导致ToShortString()无法匹配 无法更新playlist
                // 故增加通过init url来匹配 (如果有的话)
                IEnumerable<StreamSpec> match = newStreams.Where(n => n.ToShortString() == streamSpec.ToShortString());
                if (!match.Any())
                {
                    match = newStreams.Where(n => n.Playlist?.MediaInit?.Url == streamSpec.Playlist?.MediaInit?.Url);
                }

                if (match.Any())
                {
                    streamSpec.Playlist!.MediaParts = match.First().Playlist!.MediaParts; // 不更新init
                }
            }
            // 这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
        }
    }
}