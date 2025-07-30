using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.StreamParser.Constants;
using N_m3u8DL_RE.StreamParser.Util;

namespace N_m3u8DL_RE.StreamParser.Extractor
{
    // https://blog.csdn.net/leek5533/article/details/117750191
    internal sealed partial class DASHExtractor2 : IExtractor
    {
        private static readonly EncryptMethod DEFAULT_METHOD = EncryptMethod.CENC;

        public ExtractorType ExtractorType => ExtractorType.MPEGDASH;

        private string MpdUrl = string.Empty;
        private string BaseUrl = string.Empty;
        private string MpdContent = string.Empty;
        public ParserConfig ParserConfig { get; set; }

        public DASHExtractor2(ParserConfig parserConfig)
        {
            ParserConfig = parserConfig;
            SetInitUrl();
        }


        private void SetInitUrl()
        {
            MpdUrl = ParserConfig.Url ?? string.Empty;
            BaseUrl = !string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.BaseUrl : MpdUrl;
        }

        private static string ExtendBaseUrl(XElement element, string oriBaseUrl)
        {
            XElement? target = element.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL");
            if (target != null)
            {
                oriBaseUrl = ParserUtil.CombineURL(oriBaseUrl, target.Value);
            }

            return oriBaseUrl;
        }

        private static double? GetFrameRate(XElement element)
        {
            string? frameRate = element.Attribute("frameRate")?.Value;
            if (frameRate == null || !frameRate.Contains('/'))
            {
                return null;
            }

            double d = Convert.ToDouble(frameRate.Split('/')[0], CultureInfo.InvariantCulture) / Convert.ToDouble(frameRate.Split('/')[1], CultureInfo.InvariantCulture);
            frameRate = d.ToString("0.000", CultureInfo.InvariantCulture);
            return Convert.ToDouble(frameRate, CultureInfo.InvariantCulture);
        }

        public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            List<StreamSpec> streamList = [];

            MpdContent = rawText;
            PreProcessContent();


            XDocument xmlDocument = XDocument.Parse(MpdContent);

            // 选中第一个MPD节点
            XElement mpdElement = xmlDocument.Elements().First(e => e.Name.LocalName == "MPD");

            // 类型 static点播, dynamic直播
            string? type = mpdElement.Attribute("type")?.Value;
            bool isLive = type == "dynamic";

            // 分片最大时长
            string? maxSegmentDuration = mpdElement.Attribute("maxSegmentDuration")?.Value;
            // 分片从该时间起可用
            string? availabilityStartTime = mpdElement.Attribute("availabilityStartTime")?.Value;
            // 在availabilityStartTime的前XX段时间，分片有效
            string? timeShiftBufferDepth = mpdElement.Attribute("timeShiftBufferDepth")?.Value;
            if (string.IsNullOrEmpty(timeShiftBufferDepth))
            {
                // 如果没有 默认一分钟有效
                timeShiftBufferDepth = "PT1M";
            }
            // MPD发布时间
            string? publishTime = mpdElement.Attribute("publishTime")?.Value;
            // MPD总时长
            string? mediaPresentationDuration = mpdElement.Attribute("mediaPresentationDuration")?.Value;

            // 读取在MPD开头定义的<BaseURL>，并替换本身的URL
            XElement? baseUrlElement = mpdElement.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL");
            if (baseUrlElement != null)
            {
                string baseUrl = baseUrlElement.Value;
                if (baseUrl.Contains("kkbox.com.tw/"))
                {
                    baseUrl = baseUrl.Replace("//https:%2F%2F", "//");
                }

                BaseUrl = ParserUtil.CombineURL(MpdUrl, baseUrl);
            }

            // 全部Period
            IEnumerable<XElement> periods = mpdElement.Elements().Where(e => e.Name.LocalName == "Period");
            foreach (XElement? period in periods)
            {
                // 本Period时长
                string? periodDuration = period.Attribute("duration")?.Value;

                // 本Period ID
                string? periodId = period.Attribute("id")?.Value;

                // 最终分片会使用的baseurl
                string segBaseUrl = BaseUrl;

                // 处理baseurl嵌套
                segBaseUrl = ExtendBaseUrl(period, segBaseUrl);

                string adaptationSetsBaseUrl = segBaseUrl;

                // 本Period中的全部AdaptationSet
                IEnumerable<XElement> adaptationSets = period.Elements().Where(e => e.Name.LocalName == "AdaptationSet");
                foreach (XElement? adaptationSet in adaptationSets)
                {
                    // 处理baseurl嵌套
                    segBaseUrl = ExtendBaseUrl(adaptationSet, segBaseUrl);

                    string representationsBaseUrl = segBaseUrl;

                    string? mimeType = adaptationSet.Attribute("contentType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                    double? frameRate = GetFrameRate(adaptationSet);
                    // 本AdaptationSet中的全部Representation
                    IEnumerable<XElement> representations = adaptationSet.Elements().Where(e => e.Name.LocalName == "Representation");
                    foreach (XElement? representation in representations)
                    {
                        // 处理baseurl嵌套
                        segBaseUrl = ExtendBaseUrl(representation, segBaseUrl);

                        mimeType ??= representation.Attribute("contentType")?.Value ?? representation.Attribute("mimeType")?.Value ?? "";
                        XAttribute? bandwidth = representation.Attribute("bandwidth");
                        StreamSpec streamSpec = new()
                        {
                            OriginalUrl = ParserConfig.OriginalUrl,
                            PeriodId = periodId,
                            Playlist = new Playlist()
                        };
                        streamSpec.Playlist.MediaParts.Add(new MediaPart());
                        streamSpec.GroupId = representation.Attribute("id")?.Value;
                        streamSpec.Bandwidth = Convert.ToInt32(bandwidth?.Value ?? "0", CultureInfo.InvariantCulture);
                        streamSpec.Codecs = representation.Attribute("codecs")?.Value ?? adaptationSet.Attribute("codecs")?.Value;
                        streamSpec.Language = FilterLanguage(representation.Attribute("lang")?.Value ?? adaptationSet.Attribute("lang")?.Value);
                        streamSpec.FrameRate = frameRate ?? GetFrameRate(representation);
                        streamSpec.Resolution = representation.Attribute("width")?.Value != null ? $"{representation.Attribute("width")?.Value}x{representation.Attribute("height")?.Value}" : null;
                        streamSpec.Url = MpdUrl;
                        streamSpec.MediaType = mimeType.Split('/')[0] switch
                        {
                            "text" => MediaType.SUBTITLES,
                            "audio" => MediaType.AUDIO,
                            _ => null
                        };
                        // 特殊处理
                        if (representation.Attribute("volumeAdjust") != null)
                        {
                            streamSpec.GroupId += "-" + representation.Attribute("volumeAdjust")?.Value;
                        }
                        // 推测后缀名
                        string? mType = representation.Attribute("mimeType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                        if (mType != null)
                        {
                            string[] mTypeSplit = mType.Split('/');
                            streamSpec.Extension = mTypeSplit.Length == 2 ? mTypeSplit[1] : null;
                        }
                        // 优化字幕场景识别
                        if (streamSpec.Codecs is "stpp" or "wvtt")
                        {
                            streamSpec.MediaType = MediaType.SUBTITLES;
                        }
                        // 优化字幕场景识别
                        XElement? role = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "Role") ?? adaptationSet.Elements().FirstOrDefault(e => e.Name.LocalName == "Role");
                        if (role != null)
                        {
                            string? roleValue = role.Attribute("value")?.Value;
                            if (Enum.TryParse(roleValue, true, out RoleType roleType))
                            {
                                streamSpec.Role = roleType;

                                if (roleType == RoleType.Subtitle)
                                {
                                    streamSpec.MediaType = MediaType.SUBTITLES;
                                    if (mType != null && mType.Contains("ttml"))
                                    {
                                        streamSpec.Extension = "ttml";
                                    }
                                }
                            }
                            else if (roleValue != null && roleValue.Contains('-'))
                            {
                                roleValue = roleValue.Replace("-", "");
                                if (Enum.TryParse(roleValue, true, out RoleType roleType_))
                                {
                                    streamSpec.Role = roleType_;

                                    if (roleType_ == RoleType.ForcedSubtitle)
                                    {
                                        streamSpec.MediaType = MediaType.SUBTITLES; // or maybe MediaType.CLOSED_CAPTIONS?
                                        if (mType != null && mType.Contains("ttml"))
                                        {
                                            streamSpec.Extension = "ttml";
                                        }
                                    }
                                }
                            }
                        }
                        streamSpec.Playlist.IsLive = isLive;
                        // 设置刷新间隔 timeShiftBufferDepth / 2
                        if (timeShiftBufferDepth != null)
                        {
                            streamSpec.Playlist.RefreshIntervalMs = XmlConvert.ToTimeSpan(timeShiftBufferDepth).TotalMilliseconds / 2;
                        }

                        // 读取声道数量
                        XElement? audioChannelConfiguration = adaptationSet.Elements().Concat(representation.Elements()).FirstOrDefault(e => e.Name.LocalName == "AudioChannelConfiguration");
                        if (audioChannelConfiguration != null)
                        {
                            streamSpec.Channels = audioChannelConfiguration.Attribute("value")?.Value;
                        }

                        // 发布时间
                        if (!string.IsNullOrEmpty(publishTime))
                        {
                            streamSpec.PublishTime = DateTime.Parse(publishTime, CultureInfo.InvariantCulture);
                        }


                        // 第一种形式 SegmentBase
                        XElement? segmentBaseElement = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentBase");
                        if (segmentBaseElement != null)
                        {
                            // 处理init url
                            XElement? initialization = segmentBaseElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Initialization");
                            if (initialization != null)
                            {
                                string? sourceURL = initialization.Attribute("sourceURL")?.Value;
                                if (sourceURL == null)
                                {
                                    streamSpec.Playlist.MediaParts[0].MediaSegments.Add
                                    (
                                        new MediaSegment()
                                        {
                                            Index = 0,
                                            Url = segBaseUrl,
                                            Duration = XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds
                                        }
                                    );
                                }
                                else
                                {
                                    string initUrl = ParserUtil.CombineURL(segBaseUrl, initialization.Attribute("sourceURL")?.Value!);
                                    string? initRange = initialization.Attribute("range")?.Value;
                                    streamSpec.Playlist.MediaInit = new MediaSegment
                                    {
                                        Index = -1, // 便于排序
                                        Url = initUrl
                                    };
                                    if (initRange != null)
                                    {
                                        (long start, long expect) = ParserUtil.ParseRange(initRange);
                                        streamSpec.Playlist.MediaInit.StartRange = start;
                                        streamSpec.Playlist.MediaInit.ExpectLength = expect;
                                    }
                                }
                            }
                        }

                        // 第二种形式 SegmentList.SegmentList
                        XElement? segmentList = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentList");
                        if (segmentList != null)
                        {
                            string? durationStr = segmentList.Attribute("duration")?.Value;
                            // 处理init url
                            XElement? initialization = segmentList.Elements().FirstOrDefault(e => e.Name.LocalName == "Initialization");
                            if (initialization != null)
                            {
                                string initUrl = ParserUtil.CombineURL(segBaseUrl, initialization.Attribute("sourceURL")?.Value!);
                                string? initRange = initialization.Attribute("range")?.Value;
                                streamSpec.Playlist.MediaInit = new MediaSegment
                                {
                                    Index = -1, // 便于排序
                                    Url = initUrl
                                };
                                if (initRange != null)
                                {
                                    (long start, long expect) = ParserUtil.ParseRange(initRange);
                                    streamSpec.Playlist.MediaInit.StartRange = start;
                                    streamSpec.Playlist.MediaInit.ExpectLength = expect;
                                }
                            }
                            // 处理分片
                            List<XElement> segmentURLs = [.. segmentList.Elements().Where(e => e.Name.LocalName == "SegmentURL")];
                            string timescaleStr = segmentList.Attribute("timescale")?.Value ?? "1";
                            for (int segmentIndex = 0; segmentIndex < segmentURLs.Count; segmentIndex++)
                            {
                                XElement segmentURL = segmentURLs.ElementAt(segmentIndex);
                                string mediaUrl = ParserUtil.CombineURL(segBaseUrl, segmentURL.Attribute("media")?.Value!);
                                string? mediaRange = segmentURL.Attribute("mediaRange")?.Value;
                                int timesacle = Convert.ToInt32(timescaleStr, CultureInfo.InvariantCulture);
                                long duration = Convert.ToInt64(durationStr, CultureInfo.InvariantCulture);
                                MediaSegment mediaSegment = new()
                                {
                                    Duration = duration / (double)timesacle,
                                    Url = mediaUrl,
                                    Index = segmentIndex
                                };
                                if (mediaRange != null)
                                {
                                    (long start, long expect) = ParserUtil.ParseRange(mediaRange);
                                    mediaSegment.StartRange = start;
                                    mediaSegment.ExpectLength = expect;
                                }
                                streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                            }
                        }

                        // 第三种形式 SegmentTemplate+SegmentTimeline
                        // 通配符有$RepresentationID$ $Bandwidth$ $Number$ $Time$

                        // adaptationSets中的segmentTemplate
                        IEnumerable<XElement> segmentTemplateElementsOuter = adaptationSet.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                        // representation中的segmentTemplate
                        IEnumerable<XElement> segmentTemplateElements = representation.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                        if (segmentTemplateElements.Any() || segmentTemplateElementsOuter.Any())
                        {
                            // 优先使用最近的元素
                            XElement segmentTemplate = (segmentTemplateElements.FirstOrDefault() ?? segmentTemplateElementsOuter.FirstOrDefault())!;
                            XElement segmentTemplateOuter = (segmentTemplateElementsOuter.FirstOrDefault() ?? segmentTemplateElements.FirstOrDefault())!;
                            Dictionary<string, object?> varDic = new()
                            {
                                [DASHTags.TemplateRepresentationID] = streamSpec.GroupId,
                                [DASHTags.TemplateBandwidth] = bandwidth?.Value
                            };
                            // presentationTimeOffset
                            string presentationTimeOffsetStr = segmentTemplate.Attribute("presentationTimeOffset")?.Value ?? segmentTemplateOuter.Attribute("presentationTimeOffset")?.Value ?? "0";
                            // timesacle
                            string timescaleStr = segmentTemplate.Attribute("timescale")?.Value ?? segmentTemplateOuter.Attribute("timescale")?.Value ?? "1";
                            string? durationStr = segmentTemplate.Attribute("duration")?.Value ?? segmentTemplateOuter.Attribute("duration")?.Value;
                            string startNumberStr = segmentTemplate.Attribute("startNumber")?.Value ?? segmentTemplateOuter.Attribute("startNumber")?.Value ?? "1";
                            // 处理init url
                            string? initialization = segmentTemplate.Attribute("initialization")?.Value ?? segmentTemplateOuter.Attribute("initialization")?.Value;
                            if (initialization != null)
                            {
                                string _init = ParserUtil.ReplaceVars(initialization, varDic);
                                string initUrl = ParserUtil.CombineURL(segBaseUrl, _init);
                                streamSpec.Playlist.MediaInit = new MediaSegment
                                {
                                    Index = -1, // 便于排序
                                    Url = initUrl
                                };
                            }
                            // 处理分片
                            string? mediaTemplate = segmentTemplate.Attribute("media")?.Value ?? segmentTemplateOuter.Attribute("media")?.Value;
                            XElement? segmentTimeline = segmentTemplate.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentTimeline");
                            if (segmentTimeline != null)
                            {
                                // 使用了SegmentTimeline 结果精确
                                long segNumber = Convert.ToInt64(startNumberStr, CultureInfo.InvariantCulture);
                                IEnumerable<XElement> Ss = segmentTimeline.Elements().Where(e => e.Name.LocalName == "S");
                                long currentTime = 0L;
                                int segIndex = 0;
                                foreach (XElement? S in Ss)
                                {
                                    // 每个S元素包含三个属性:@t(start time)\@r(repeat count)\@d(duration)
                                    string? _startTimeStr = S.Attribute("t")?.Value;
                                    string? _durationStr = S.Attribute("d")?.Value;
                                    string? _repeatCountStr = S.Attribute("r")?.Value;

                                    if (_startTimeStr != null)
                                    {
                                        currentTime = Convert.ToInt64(_startTimeStr, CultureInfo.InvariantCulture);
                                    }

                                    long _duration = Convert.ToInt64(_durationStr, CultureInfo.InvariantCulture);
                                    int timescale = Convert.ToInt32(timescaleStr, CultureInfo.InvariantCulture);
                                    long _repeatCount = Convert.ToInt64(_repeatCountStr, CultureInfo.InvariantCulture);
                                    varDic[DASHTags.TemplateTime] = currentTime;
                                    varDic[DASHTags.TemplateNumber] = segNumber++;
                                    bool hasTime = mediaTemplate!.Contains(DASHTags.TemplateTime);
                                    string media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                    string mediaUrl = ParserUtil.CombineURL(segBaseUrl, media!);
                                    MediaSegment mediaSegment = new()
                                    {
                                        Url = mediaUrl
                                    };
                                    if (hasTime)
                                    {
                                        mediaSegment.NameFromVar = currentTime.ToString(CultureInfo.InvariantCulture);
                                    }

                                    mediaSegment.Duration = _duration / (double)timescale;
                                    mediaSegment.Index = segIndex++;
                                    streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                                    if (_repeatCount < 0)
                                    {
                                        // 负数表示一直重复 直到period结束 注意减掉已经加入的1个片段
                                        _repeatCount = (long)Math.Ceiling(XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale / _duration) - 1;
                                    }
                                    for (long i = 0; i < _repeatCount; i++)
                                    {
                                        currentTime += _duration;
                                        MediaSegment _mediaSegment = new();
                                        varDic[DASHTags.TemplateTime] = currentTime;
                                        varDic[DASHTags.TemplateNumber] = segNumber++;
                                        bool _hashTime = mediaTemplate!.Contains(DASHTags.TemplateTime);
                                        string _media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                        string _mediaUrl = ParserUtil.CombineURL(segBaseUrl, _media);
                                        _mediaSegment.Url = _mediaUrl;
                                        _mediaSegment.Index = segIndex++;
                                        _mediaSegment.Duration = _duration / (double)timescale;
                                        if (_hashTime)
                                        {
                                            _mediaSegment.NameFromVar = currentTime.ToString(CultureInfo.InvariantCulture);
                                        }

                                        streamSpec.Playlist.MediaParts[0].MediaSegments.Add(_mediaSegment);
                                    }
                                    currentTime += _duration;
                                }
                            }
                            else
                            {
                                // 没用SegmentTimeline 需要计算总分片数量 不精确
                                int timescale = Convert.ToInt32(timescaleStr, CultureInfo.InvariantCulture);
                                long startNumber = Convert.ToInt64(startNumberStr, CultureInfo.InvariantCulture);
                                int duration = Convert.ToInt32(durationStr, CultureInfo.InvariantCulture);
                                long totalNumber = (long)Math.Ceiling(XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale / duration);
                                // 直播的情况，需要自己计算totalNumber
                                if (totalNumber == 0 && isLive)
                                {
                                    DateTime now = DateTime.Now;
                                    DateTime availableTime = DateTime.Parse(availabilityStartTime!, CultureInfo.InvariantCulture);
                                    // 可用时间+偏移量
                                    TimeSpan offsetMs = TimeSpan.FromMilliseconds(Convert.ToInt64(presentationTimeOffsetStr, CultureInfo.InvariantCulture) / 1000);
                                    availableTime = availableTime.Add(offsetMs);
                                    TimeSpan ts = now - availableTime;
                                    TimeSpan updateTs = XmlConvert.ToTimeSpan(timeShiftBufferDepth!);
                                    // (当前时间到发布时间的时间差 - 最小刷新间隔) / 分片时长
                                    startNumber += (long)((ts.TotalSeconds - updateTs.TotalSeconds) * timescale / duration);
                                    totalNumber = (long)(updateTs.TotalSeconds * timescale / duration);
                                }
                                for (long index = startNumber, segIndex = 0; index < startNumber + totalNumber; index++, segIndex++)
                                {
                                    varDic[DASHTags.TemplateNumber] = index;
                                    bool hasNumber = mediaTemplate!.Contains(DASHTags.TemplateNumber);
                                    string media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                    string mediaUrl = ParserUtil.CombineURL(segBaseUrl, media!);
                                    MediaSegment mediaSegment = new()
                                    {
                                        Url = mediaUrl
                                    };
                                    if (hasNumber)
                                    {
                                        mediaSegment.NameFromVar = index.ToString(CultureInfo.InvariantCulture);
                                    }

                                    mediaSegment.Index = isLive ? index : segIndex; // 直播直接用startNumber
                                    mediaSegment.Duration = duration / (double)timescale;
                                    streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                                }
                            }
                        }

                        // 如果依旧没被添加分片，直接把BaseUrl塞进去就好
                        if (streamSpec.Playlist.MediaParts[0].MediaSegments.Count == 0)
                        {
                            streamSpec.Playlist.MediaParts[0].MediaSegments.Add
                            (
                                new MediaSegment()
                                {
                                    Index = 0,
                                    Url = segBaseUrl,
                                    Duration = XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds
                                }
                            );
                        }

                        // 判断加密情况
                        if (adaptationSet.Elements().Concat(representation.Elements()).Any(e => e.Name.LocalName == "ContentProtection"))
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

                        // 处理同一ID分散在不同Period的情况
                        int _index = streamList.FindIndex(_f => _f.PeriodId != streamSpec.PeriodId && _f.GroupId == streamSpec.GroupId && _f.Resolution == streamSpec.Resolution && _f.MediaType == streamSpec.MediaType);
                        if (_index > -1)
                        {
                            if (isLive)
                            {
                                // 直播，这种情况直接略过新的
                            }
                            else
                            {
                                // 点播，这种情况如果URL不同则作为新的part出现，否则仅把时间加起来
                                string url1 = streamList[_index].Playlist!.MediaParts.Last().MediaSegments.Last().Url;
                                string? url2 = streamSpec.Playlist.MediaParts[0].MediaSegments.LastOrDefault()?.Url;
                                if (url1 != url2)
                                {
                                    long startIndex = streamList[_index].Playlist!.MediaParts.Last().MediaSegments.Last().Index + 1;
                                    List<MediaSegment>.Enumerator enumerator = streamSpec.Playlist.MediaParts[0].MediaSegments.GetEnumerator();
                                    while (enumerator.MoveNext())
                                    {
                                        enumerator.Current.Index += startIndex;
                                    }
                                    streamList[_index].Playlist!.MediaParts.Add(new MediaPart()
                                    {
                                        MediaSegments = streamSpec.Playlist.MediaParts[0].MediaSegments
                                    });
                                }
                                else
                                {
                                    streamList[_index].Playlist!.MediaParts.Last().MediaSegments.Last().Duration += streamSpec.Playlist.MediaParts[0].MediaSegments.Sum(x => x.Duration);
                                }
                            }
                        }
                        else
                        {
                            // 修复mp4类型字幕
                            if (streamSpec is { MediaType: MediaType.SUBTITLES, Extension: "mp4" })
                            {
                                streamSpec.Extension = "m4s";
                            }
                            // 分片默认后缀m4s
                            if (streamSpec.MediaType != MediaType.SUBTITLES && (streamSpec.Extension == null || streamSpec.Playlist.MediaParts.Sum(x => x.MediaSegments.Count) > 1))
                            {
                                streamSpec.Extension = "m4s";
                            }
                            streamList.Add(streamSpec);
                        }
                        // 恢复BaseURL相对位置
                        segBaseUrl = representationsBaseUrl;
                    }
                    // 恢复BaseURL相对位置
                    segBaseUrl = adaptationSetsBaseUrl;
                }
            }

            // 为视频设置默认轨道
            List<StreamSpec> aL = [.. streamList.Where(s => s.MediaType == MediaType.AUDIO)];
            List<StreamSpec> sL = [.. streamList.Where(s => s.MediaType == MediaType.SUBTITLES)];
            foreach (StreamSpec? item in streamList.Where(item => !string.IsNullOrEmpty(item.Resolution)))
            {
                if (aL.Count != 0)
                {
                    item.AudioId = aL.OrderByDescending(x => x.Bandwidth).First().GroupId;
                }
                if (sL.Count != 0)
                {
                    item.SubtitleId = sL.OrderByDescending(x => x.Bandwidth).First().GroupId;
                }
            }

            return Task.FromResult(streamList);
        }

        /// <summary>
        /// 如果有非法字符 返回und
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        private static string? FilterLanguage(string? v)
        {
            return v == null ? null : LangCodeRegex().IsMatch(v) ? v : "und";
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
                    foreach (MediaSegment mediaSegment in part.MediaSegments)
                    {
                        mediaSegment.Url = PreProcessUrl(mediaSegment.Url);
                    }
                }
            }

            return Task.CompletedTask;
        }

        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            // 这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
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
                if (p.CanProcess(ExtractorType, MpdContent, ParserConfig))
                {
                    MpdContent = p.Process(MpdContent, ParserConfig);
                }
            }
        }

        [GeneratedRegex(@"^[\w_\-\d]+$")]
        private static partial Regex LangCodeRegex();
    }
}