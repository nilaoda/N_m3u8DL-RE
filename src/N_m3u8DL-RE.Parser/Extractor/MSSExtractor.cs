using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Mp4;
using N_m3u8DL_RE.Parser.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace N_m3u8DL_RE.Parser.Extractor
{
    //Microsoft Smooth Streaming
    //https://test.playready.microsoft.com/smoothstreaming/SSWSS720H264/SuperSpeedway_720.ism/manifest
    //file:///C:/Users/nilaoda/Downloads/[MS-SSTR]-180316.pdf
    internal partial class MSSExtractor : IExtractor
    {
        [GeneratedRegex("00000001\\d7([0-9a-fA-F]{6})")]
        private static partial Regex VCodecsRegex();

        ////////////////////////////////////////
        
        private static EncryptMethod DEFAULT_METHOD = EncryptMethod.CENC;

        public ExtractorType ExtractorType => ExtractorType.MSS;

        private string IsmUrl = string.Empty;
        private string BaseUrl = string.Empty;
        private string IsmContent = string.Empty;
        public ParserConfig ParserConfig { get; set; }

        public MSSExtractor(ParserConfig parserConfig)
        {
            this.ParserConfig = parserConfig;
            SetInitUrl();
        }

        private void SetInitUrl()
        {
            this.IsmUrl = ParserConfig.Url ?? string.Empty;
            if (!string.IsNullOrEmpty(ParserConfig.BaseUrl))
                this.BaseUrl = ParserConfig.BaseUrl;
            else
                this.BaseUrl = this.IsmUrl;
        }

        public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            var streamList = new List<StreamSpec>();
            this.IsmContent = rawText;
            this.PreProcessContent();

            var xmlDocument = XDocument.Parse(IsmContent);

            //选中第一个SmoothStreamingMedia节点
            var ssmElement = xmlDocument.Elements().First(e => e.Name.LocalName == "SmoothStreamingMedia");
            var timeScaleStr = ssmElement.Attribute("TimeScale")?.Value ?? "10000000";
            var durationStr = ssmElement.Attribute("Duration")?.Value;
            var timescale = Convert.ToInt32(timeScaleStr);
            var isLiveStr = ssmElement.Attribute("IsLive")?.Value;
            bool isLive = Convert.ToBoolean(isLiveStr ?? "FALSE");

            var isProtection = false;
            var protectionSystemId = "";
            var protectionData = "";

            //加密检测
            var protectElement = ssmElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Protection");
            if (protectElement != null)
            {
                var protectionHeader = protectElement.Element("ProtectionHeader");
                if (protectionHeader != null)
                {
                    isProtection = true;
                    protectionSystemId = protectionHeader.Attribute("SystemID")?.Value ?? "9A04F079-9840-4286-AB92-E65BE0885F95";
                    protectionData = HexUtil.BytesToHex(Convert.FromBase64String(protectionHeader.Value));
                }
            }

            //所有StreamIndex节点
            var streamIndexElements = ssmElement.Elements().Where(e => e.Name.LocalName == "StreamIndex");

            foreach (var streamIndex in streamIndexElements)
            {
                var type = streamIndex.Attribute("Type")?.Value; //"video" / "audio" / "text"
                var name = streamIndex.Attribute("Name")?.Value;
                var subType = streamIndex.Attribute("Subtype")?.Value; //text track
                //如果有则不从QualityLevel读取
                //Bitrate = "{bitrate}" / "{Bitrate}"
                //StartTimeSubstitution = "{start time}" / "{start_time}"
                var urlPattern = streamIndex.Attribute("Url")?.Value;
                var language = streamIndex.Attribute("Language")?.Value;
                //去除不规范的语言标签
                if (language?.Length != 3) language = null;

                //所有c节点
                var cElements = streamIndex.Elements().Where(e => e.Name.LocalName == "c");

                //所有QualityLevel节点
                var qualityLevelElements = streamIndex.Elements().Where(e => e.Name.LocalName == "QualityLevel");

                foreach (var qualityLevel in qualityLevelElements)
                {
                    urlPattern = (qualityLevel.Attribute("Url")?.Value ?? urlPattern)!
                        .Replace(MSSTags.Bitrate_BK, MSSTags.Bitrate).Replace(MSSTags.StartTime_BK, MSSTags.StartTime);
                    var fourCC = qualityLevel.Attribute("FourCC")!.Value.ToUpper();
                    var samplingRateStr = qualityLevel.Attribute("SamplingRate")?.Value;
                    var bitsPerSampleStr = qualityLevel.Attribute("BitsPerSample")?.Value;
                    var nalUnitLengthFieldStr = qualityLevel.Attribute("NALUnitLengthField")?.Value;
                    var indexStr = qualityLevel.Attribute("Index")?.Value;
                    var codecPrivateData = qualityLevel.Attribute("CodecPrivateData")?.Value ?? "";
                    var audioTag = qualityLevel.Attribute("AudioTag")?.Value;
                    var bitrate = Convert.ToInt32(qualityLevel.Attribute("Bitrate")?.Value ?? "0");
                    var width = Convert.ToInt32(qualityLevel.Attribute("MaxWidth")?.Value ?? "0");
                    var height = Convert.ToInt32(qualityLevel.Attribute("MaxHeight")?.Value ?? "0");
                    var channels = qualityLevel.Attribute("Channels")?.Value;

                    StreamSpec streamSpec = new();
                    streamSpec.PublishTime = DateTime.Now; //发布时间默认现在
                    streamSpec.Extension = "m4s";
                    streamSpec.OriginalUrl = ParserConfig.OriginalUrl;
                    streamSpec.PeriodId = indexStr;
                    streamSpec.Playlist = new Playlist();
                    streamSpec.Playlist.IsLive = isLive;
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
                        streamSpec.Playlist.MediaInit.Index = -1; //便于排序
                        streamSpec.Playlist.MediaInit.Url = $"hex://{codecPrivateData}";
                    }

                    var currentTime = 0L;
                    var segIndex = 0;
                    var varDic = new Dictionary<string, object?>();
                    varDic[MSSTags.Bitrate] = bitrate;

                    foreach (var c in cElements)
                    {
                        //每个C元素包含三个属性:@t(start time)\@r(repeat count)\@d(duration)
                        var _startTimeStr = c.Attribute("t")?.Value;
                        var _durationStr = c.Attribute("d")?.Value;
                        var _repeatCountStr = c.Attribute("r")?.Value;

                        if (_startTimeStr != null) currentTime = Convert.ToInt64(_startTimeStr);
                        var _duration = Convert.ToInt64(_durationStr);
                        var _repeatCount = Convert.ToInt64(_repeatCountStr);
                        if (_repeatCount > 0)
                        {
                            // This value is one-based. (A value of 2 means two fragments in the contiguous series).
                            _repeatCount -= 1;
                        }

                        varDic[MSSTags.StartTime] = currentTime;
                        var oriUrl = ParserUtil.CombineURL(this.BaseUrl, urlPattern!);
                        var mediaUrl = ParserUtil.ReplaceVars(oriUrl, varDic);
                        MediaSegment mediaSegment = new();
                        mediaSegment.Url = mediaUrl;
                        if (oriUrl.Contains(MSSTags.StartTime))
                            mediaSegment.NameFromVar = currentTime.ToString();
                        mediaSegment.Duration = _duration / (double)timescale;
                        mediaSegment.Index = segIndex++;
                        streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                        if (_repeatCount < 0)
                        {
                            //负数表示一直重复 直到period结束 注意减掉已经加入的1个片段
                            _repeatCount = (long)Math.Ceiling(Convert.ToInt64(durationStr) / (double)_duration) - 1;
                        }
                        for (long i = 0; i < _repeatCount; i++)
                        {
                            currentTime += _duration;
                            MediaSegment _mediaSegment = new();
                            varDic[MSSTags.StartTime] = currentTime;
                            var _oriUrl = ParserUtil.CombineURL(this.BaseUrl, urlPattern!);
                            var _mediaUrl = ParserUtil.ReplaceVars(_oriUrl, varDic);
                            _mediaSegment.Url = _mediaUrl;
                            _mediaSegment.Index = segIndex++;
                            _mediaSegment.Duration = _duration / (double)timescale;
                            if (_oriUrl.Contains(MSSTags.StartTime))
                                _mediaSegment.NameFromVar = currentTime.ToString();
                            streamSpec.Playlist.MediaParts[0].MediaSegments.Add(_mediaSegment);
                        }
                        currentTime += _duration;
                    }

                    //生成MOOV数据
                    if (MSSMoovProcessor.CanHandle(fourCC!))
                    {
                        streamSpec.MSSData = new MSSData()
                        {
                            FourCC = fourCC!,
                            CodecPrivateData = codecPrivateData,
                            Type = type!,
                            Timesacle = Convert.ToInt32(timeScaleStr),
                            Duration = Convert.ToInt64(durationStr),
                            SamplingRate = Convert.ToInt32(samplingRateStr ?? "48000"),
                            Channels = Convert.ToInt32(channels ?? "2"),
                            BitsPerSample = Convert.ToInt32(bitsPerSampleStr ?? "16"),
                            NalUnitLengthField = Convert.ToInt32(nalUnitLengthFieldStr ?? "4"),
                            IsProtection = isProtection,
                            ProtectionData = protectionData,
                            ProtectionSystemID = protectionSystemId,
                        };
                        var processor = new MSSMoovProcessor(streamSpec);
                        var header = processor.GenHeader(); //trackId可能不正确
                        streamSpec.Playlist!.MediaInit!.Url = $"base64://{Convert.ToBase64String(header)}";
                        //为音视频写入加密信息
                        if (isProtection && type != "text") 
                        {
                            if (streamSpec.Playlist.MediaInit != null)
                            {
                                streamSpec.Playlist.MediaInit.EncryptInfo.Method = DEFAULT_METHOD;
                            }
                            foreach (var item in streamSpec.Playlist.MediaParts[0].MediaSegments)
                            {
                                item.EncryptInfo.Method = DEFAULT_METHOD;
                            }
                        }
                        streamList.Add(streamSpec);
                    }
                    else
                    {
                        Logger.WarnMarkUp($"[green]{fourCC}[/] not supported! Skiped.");
                        continue;
                    }
                }
            }

            //为视频设置默认轨道
            var aL = streamList.Where(s => s.MediaType == MediaType.AUDIO);
            var sL = streamList.Where(s => s.MediaType == MediaType.SUBTITLES);
            foreach (var item in streamList)
            {
                if (!string.IsNullOrEmpty(item.Resolution))
                {
                    if (aL.Any())
                    {
                        item.AudioId = aL.First().GroupId;
                    }
                    if (sL.Any())
                    {
                        item.SubtitleId = sL.First().GroupId;
                    }
                }
            }

            return streamList;
        }

        /// <summary>
        /// 解析编码
        /// </summary>
        /// <param name="fourCC"></param>
        /// <returns></returns>
        private static string? ParseCodecs(string fourCC, string? privateData)
        {
            if (fourCC == "TTML") return "stpp";
            if (string.IsNullOrEmpty(privateData)) return null;

            return fourCC switch
            {
                //AVC视频
                "H264" or "X264" or "DAVC" or "AVC1" => ParseAVCCodecs(privateData),
                //AAC音频
                "AAC" or "AACL" or "AACH" or "AACP" => ParseAACCodecs(fourCC, privateData),
                //默认返回fourCC本身
                _ => fourCC.ToLower()
            };
        }

        private static string ParseAVCCodecs(string privateData)
        {
            var result = VCodecsRegex().Match(privateData).Groups[1].Value;
            return string.IsNullOrEmpty(result) ? "avc1.4D401E" : $"avc1.{result}";
        }

        private static string ParseAACCodecs(string fourCC, string privateData)
        {
            var mpProfile = 2;
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
            //这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
        }

        private async Task ProcessUrlAsync(List<StreamSpec> streamSpecs)
        {
            for (int i = 0; i < streamSpecs.Count; i++)
            {
                var playlist = streamSpecs[i].Playlist;
                if (playlist != null)
                {
                    if (playlist.MediaInit != null)
                    {
                        playlist.MediaInit!.Url = PreProcessUrl(playlist.MediaInit!.Url);
                    }
                    for (int ii = 0; ii < playlist!.MediaParts.Count; ii++)
                    {
                        var part = playlist.MediaParts[ii];
                        for (int iii = 0; iii < part.MediaSegments.Count; iii++)
                        {
                            part.MediaSegments[iii].Url = PreProcessUrl(part.MediaSegments[iii].Url);
                        }
                    }
                }
            }
        }

        public string PreProcessUrl(string url)
        {
            foreach (var p in ParserConfig.UrlProcessors)
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
            foreach (var p in ParserConfig.ContentProcessors)
            {
                if (p.CanProcess(ExtractorType, IsmContent, ParserConfig))
                {
                    IsmContent = p.Process(IsmContent, ParserConfig);
                }
            }
        }

        public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
        {
            if (streamSpecs.Count == 0) return;

            var (rawText, url) = ("", ParserConfig.Url);
            try
            {
                (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.Url, ParserConfig.Headers);
            }
            catch (HttpRequestException) when (ParserConfig.Url != ParserConfig.OriginalUrl)
            {
                //当URL无法访问时，再请求原始URL
                (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.OriginalUrl, ParserConfig.Headers);
            }

            ParserConfig.Url = url;
            SetInitUrl();

            var newStreams = await ExtractStreamsAsync(rawText);
            foreach (var streamSpec in streamSpecs)
            {
                //有的网站每次请求MPD返回的码率不一致，导致ToShortString()无法匹配 无法更新playlist
                //故增加通过init url来匹配 (如果有的话)
                var match = newStreams.Where(n => n.ToShortString() == streamSpec.ToShortString());
                if (!match.Any())
                    match = newStreams.Where(n => n.Playlist?.MediaInit?.Url == streamSpec.Playlist?.MediaInit?.Url);

                if (match.Any())
                    streamSpec.Playlist!.MediaParts = match.First().Playlist!.MediaParts; //不更新init
            }
            //这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
        }
    }
}
