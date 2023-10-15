using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace N_m3u8DL_RE.Parser.Extractor
{
    //https://blog.csdn.net/leek5533/article/details/117750191
    internal class DASHExtractor2 : IExtractor
    {
        private static EncryptMethod DEFAULT_METHOD = EncryptMethod.CENC;

        public ExtractorType ExtractorType => ExtractorType.MPEG_DASH;

        private string MpdUrl = string.Empty;
        private string BaseUrl = string.Empty;
        private string MpdContent = string.Empty;
        public ParserConfig ParserConfig { get; set; }

        public DASHExtractor2(ParserConfig parserConfig)
        {
            this.ParserConfig = parserConfig;
            SetInitUrl();
        }


        private void SetInitUrl()
        {
            this.MpdUrl = ParserConfig.Url ?? string.Empty;
            if (!string.IsNullOrEmpty(ParserConfig.BaseUrl))
                this.BaseUrl = ParserConfig.BaseUrl;
            else
                this.BaseUrl = this.MpdUrl;
        }

        private string ExtendBaseUrl(XElement element, string oriBaseUrl)
        {
            var target = element.Elements().Where(e => e.Name.LocalName == "BaseURL").FirstOrDefault();
            if (target != null)
            {
                oriBaseUrl = ParserUtil.CombineURL(oriBaseUrl, target.Value);
            }

            return oriBaseUrl;
        }

        private double? GetFrameRate(XElement element)
        {
            var frameRate = element.Attribute("frameRate")?.Value;
            if (frameRate != null && frameRate.Contains("/"))
            {
                var d = Convert.ToDouble(frameRate.Split('/')[0]) / Convert.ToDouble(frameRate.Split('/')[1]);
                frameRate = d.ToString("0.000");
                return Convert.ToDouble(frameRate);
            }
            return null;
        }

        public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            var streamList = new List<StreamSpec>();

            this.MpdContent = rawText;
            this.PreProcessContent();


            var xmlDocument = XDocument.Parse(MpdContent);

            //选中第一个MPD节点
            var mpdElement = xmlDocument.Elements().First(e => e.Name.LocalName == "MPD");

            //类型 static点播, dynamic直播
            var type = mpdElement.Attribute("type")?.Value;
            bool isLive = type == "dynamic";

            //分片最大时长
            var maxSegmentDuration = mpdElement.Attribute("maxSegmentDuration")?.Value;
            //分片从该时间起可用
            var availabilityStartTime = mpdElement.Attribute("availabilityStartTime")?.Value;
            //在availabilityStartTime的前XX段时间，分片有效
            var timeShiftBufferDepth = mpdElement.Attribute("timeShiftBufferDepth")?.Value;
            if (string.IsNullOrEmpty(timeShiftBufferDepth))
            {
                //如果没有 默认一分钟有效
                timeShiftBufferDepth = "PT1M";
            }
            //MPD发布时间
            var publishTime = mpdElement.Attribute("publishTime")?.Value;
            //MPD总时长
            var mediaPresentationDuration = mpdElement.Attribute("mediaPresentationDuration")?.Value;

            //读取在MPD开头定义的<BaseURL>，并替换本身的URL
            var baseUrlElement = mpdElement.Elements().Where(e => e.Name.LocalName == "BaseURL").FirstOrDefault();
            if (baseUrlElement != null)
            {
                var baseUrl = baseUrlElement.Value;
                if (baseUrl.Contains("kkbox.com.tw/")) baseUrl = baseUrl.Replace("//https:%2F%2F", "//");
                this.BaseUrl = ParserUtil.CombineURL(this.MpdUrl, baseUrl);
            }

            //全部Period
            var periods = mpdElement.Elements().Where(e => e.Name.LocalName == "Period");
            foreach (var period in periods)
            {
                //本Period时长
                var periodDuration = period.Attribute("duration")?.Value;

                //本Period ID
                var periodId = period.Attribute("id")?.Value;

                //最终分片会使用的baseurl
                var segBaseUrl = this.BaseUrl;

                //处理baseurl嵌套
                segBaseUrl = ExtendBaseUrl(period, segBaseUrl);

                var adaptationSetsBaseUrl = segBaseUrl;

                //本Period中的全部AdaptationSet
                var adaptationSets = period.Elements().Where(e => e.Name.LocalName == "AdaptationSet");
                foreach (var adaptationSet in adaptationSets)
                {
                    //处理baseurl嵌套
                    segBaseUrl = ExtendBaseUrl(adaptationSet, segBaseUrl);

                    var representationsBaseUrl = segBaseUrl;

                    var mimeType = adaptationSet.Attribute("contentType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                    var frameRate = GetFrameRate(adaptationSet);
                    //本AdaptationSet中的全部Representation
                    var representations = adaptationSet.Elements().Where(e => e.Name.LocalName == "Representation");
                    foreach (var representation in representations)
                    {
                        //处理baseurl嵌套
                        segBaseUrl = ExtendBaseUrl(representation, segBaseUrl);

                        if (mimeType == null)
                        {
                            mimeType = representation.Attribute("contentType")?.Value ?? representation.Attribute("mimeType")?.Value ?? "";
                        }
                        var bandwidth = representation.Attribute("bandwidth");
                        StreamSpec streamSpec = new();
                        streamSpec.OriginalUrl = ParserConfig.OriginalUrl;
                        streamSpec.PeriodId = periodId;
                        streamSpec.Playlist = new Playlist();
                        streamSpec.Playlist.MediaParts.Add(new MediaPart());
                        streamSpec.GroupId = representation.Attribute("id")?.Value;
                        streamSpec.Bandwidth = Convert.ToInt32(bandwidth?.Value ?? "0");
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
                        //特殊处理
                        if (representation.Attribute("volumeAdjust") != null)
                        {
                            streamSpec.GroupId += "-" + representation.Attribute("volumeAdjust")?.Value;
                        }
                        //推测后缀名
                        var mType = representation.Attribute("mimeType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                        if (mType != null)
                        {
                            var mTypeSplit = mType.Split('/');
                            streamSpec.Extension = mTypeSplit.Length == 2 ? mTypeSplit[1] : null;
                        }
                        //优化字幕场景识别
                        if (streamSpec.Codecs == "stpp" || streamSpec.Codecs == "wvtt")
                        {
                            streamSpec.MediaType = MediaType.SUBTITLES;
                        }
                        //优化字幕场景识别
                        var role = representation.Elements().Where(e => e.Name.LocalName == "Role").FirstOrDefault() ?? adaptationSet.Elements().Where(e => e.Name.LocalName == "Role").FirstOrDefault();
                        if (role != null)
                        {
                            var v = role.Attribute("value")?.Value;
                            if (Enum.TryParse(v, true, out RoleType roleType))
                            {
                                streamSpec.Role = roleType;

                                if (roleType == RoleType.Subtitle)
                                {
                                    streamSpec.MediaType = MediaType.SUBTITLES;
                                    if (mType != null && mType.Contains("ttml"))
                                        streamSpec.Extension = "ttml";
                                }
                            }
                        }
                        streamSpec.Playlist.IsLive = isLive;
                        //设置刷新间隔 timeShiftBufferDepth / 2
                        if (timeShiftBufferDepth != null)
                        {
                            streamSpec.Playlist.RefreshIntervalMs = XmlConvert.ToTimeSpan(timeShiftBufferDepth).TotalMilliseconds / 2;
                        }

                        //读取声道数量
                        var audioChannelConfiguration = adaptationSet.Elements().Concat(representation.Elements()).Where(e => e.Name.LocalName == "AudioChannelConfiguration").FirstOrDefault();
                        if (audioChannelConfiguration != null)
                        {
                            streamSpec.Channels = audioChannelConfiguration.Attribute("value")?.Value;
                        }

                        //发布时间
                        if (!string.IsNullOrEmpty(publishTime))
                        {
                            streamSpec.PublishTime = DateTime.Parse(publishTime);
                        }


                        //第一种形式 SegmentBase
                        var segmentBaseElement = representation.Elements().Where(e => e.Name.LocalName == "SegmentBase").FirstOrDefault();
                        if (segmentBaseElement != null)
                        {
                            //处理init url
                            var initialization = segmentBaseElement.Elements().Where(e => e.Name.LocalName == "Initialization").FirstOrDefault();
                            if (initialization != null)
                            {
                                var sourceURL = initialization.Attribute("sourceURL")?.Value;
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
                                    var initUrl = ParserUtil.CombineURL(segBaseUrl, initialization.Attribute("sourceURL")?.Value!);
                                    var initRange = initialization.Attribute("range")?.Value;
                                    streamSpec.Playlist.MediaInit = new MediaSegment();
                                    streamSpec.Playlist.MediaInit.Index = -1; //便于排序
                                    streamSpec.Playlist.MediaInit.Url = initUrl;
                                    if (initRange != null)
                                    {
                                        var (start, expect) = ParserUtil.ParseRange(initRange);
                                        streamSpec.Playlist.MediaInit.StartRange = start;
                                        streamSpec.Playlist.MediaInit.ExpectLength = expect;
                                    }
                                }
                            }
                        }

                        //第二种形式 SegmentList.SegmentList
                        var segmentList = representation.Elements().Where(e => e.Name.LocalName == "SegmentList").FirstOrDefault();
                        if (segmentList != null)
                        {
                            var durationStr = segmentList.Attribute("duration")?.Value;
                            //处理init url
                            var initialization = segmentList.Elements().Where(e => e.Name.LocalName == "Initialization").FirstOrDefault();
                            if (initialization != null)
                            {
                                var initUrl = ParserUtil.CombineURL(segBaseUrl, initialization.Attribute("sourceURL")?.Value!);
                                var initRange = initialization.Attribute("range")?.Value;
                                streamSpec.Playlist.MediaInit = new MediaSegment();
                                streamSpec.Playlist.MediaInit.Index = -1; //便于排序
                                streamSpec.Playlist.MediaInit.Url = initUrl;
                                if (initRange != null)
                                {
                                    var (start, expect) = ParserUtil.ParseRange(initRange);
                                    streamSpec.Playlist.MediaInit.StartRange = start;
                                    streamSpec.Playlist.MediaInit.ExpectLength = expect;
                                }
                            }
                            //处理分片
                            var segmentURLs = segmentList.Elements().Where(e => e.Name.LocalName == "SegmentURL");
                            var timescaleStr = segmentList.Attribute("timescale")?.Value ?? "1";
                            for (int segmentIndex = 0; segmentIndex < segmentURLs.Count(); segmentIndex++)
                            {
                                var segmentURL = segmentURLs.ElementAt(segmentIndex);
                                var mediaUrl = ParserUtil.CombineURL(segBaseUrl, segmentURL.Attribute("media")?.Value!);
                                var mediaRange = segmentURL.Attribute("mediaRange")?.Value;
                                var timesacle = Convert.ToInt32(timescaleStr);
                                var duration = Convert.ToInt64(durationStr);
                                MediaSegment mediaSegment = new();
                                mediaSegment.Duration = duration / (double)timesacle;
                                mediaSegment.Url = mediaUrl;
                                mediaSegment.Index = segmentIndex;
                                if (mediaRange != null)
                                {
                                    var (start, expect) = ParserUtil.ParseRange(mediaRange);
                                    mediaSegment.StartRange = start;
                                    mediaSegment.ExpectLength = expect;
                                }
                                streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                            }
                        }

                        //第三种形式 SegmentTemplate+SegmentTimeline
                        //通配符有$RepresentationID$ $Bandwidth$ $Number$ $Time$

                        //adaptationSets中的segmentTemplate
                        var segmentTemplateElementsOuter = adaptationSet.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                        //representation中的segmentTemplate
                        var segmentTemplateElements = representation.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                        if (segmentTemplateElements.Any() || segmentTemplateElementsOuter.Any())
                        {
                            //优先使用最近的元素
                            var segmentTemplate = (segmentTemplateElements.FirstOrDefault() ?? segmentTemplateElementsOuter.FirstOrDefault())!;
                            var segmentTemplateOuter = (segmentTemplateElementsOuter.FirstOrDefault() ?? segmentTemplateElements.FirstOrDefault())!;
                            var varDic = new Dictionary<string, object?>();
                            varDic[DASHTags.TemplateRepresentationID] = streamSpec.GroupId;
                            varDic[DASHTags.TemplateBandwidth] = bandwidth?.Value;
                            //presentationTimeOffset
                            var presentationTimeOffsetStr = segmentTemplate.Attribute("presentationTimeOffset")?.Value ?? segmentTemplateOuter.Attribute("presentationTimeOffset")?.Value ?? "0";
                            //timesacle
                            var timescaleStr = segmentTemplate.Attribute("timescale")?.Value ?? segmentTemplateOuter.Attribute("timescale")?.Value ?? "1";
                            var durationStr = segmentTemplate.Attribute("duration")?.Value ?? segmentTemplateOuter.Attribute("duration")?.Value;
                            var startNumberStr = segmentTemplate.Attribute("startNumber")?.Value ?? segmentTemplateOuter.Attribute("startNumber")?.Value ?? "1";
                            //处理init url
                            var initialization = segmentTemplate.Attribute("initialization")?.Value ?? segmentTemplateOuter.Attribute("initialization")?.Value;
                            if (initialization != null)
                            {
                                var _init = ParserUtil.ReplaceVars(initialization, varDic);
                                var initUrl = ParserUtil.CombineURL(segBaseUrl, _init);
                                streamSpec.Playlist.MediaInit = new MediaSegment();
                                streamSpec.Playlist.MediaInit.Index = -1; //便于排序
                                streamSpec.Playlist.MediaInit.Url = initUrl;
                            }
                            //处理分片
                            var mediaTemplate = segmentTemplate.Attribute("media")?.Value ?? segmentTemplateOuter.Attribute("media")?.Value;
                            var segmentTimeline = segmentTemplate.Elements().Where(e => e.Name.LocalName == "SegmentTimeline").FirstOrDefault();
                            if (segmentTimeline != null)
                            {
                                //使用了SegmentTimeline 结果精确
                                var segNumber = Convert.ToInt64(startNumberStr);
                                var Ss = segmentTimeline.Elements().Where(e => e.Name.LocalName == "S");
                                var currentTime = 0L;
                                var segIndex = 0;
                                foreach (var S in Ss)
                                {
                                    //每个S元素包含三个属性:@t(start time)\@r(repeat count)\@d(duration)
                                    var _startTimeStr = S.Attribute("t")?.Value;
                                    var _durationStr = S.Attribute("d")?.Value;
                                    var _repeatCountStr = S.Attribute("r")?.Value;

                                    if (_startTimeStr != null) currentTime = Convert.ToInt64(_startTimeStr);
                                    var _duration = Convert.ToInt64(_durationStr);
                                    var timescale = Convert.ToInt32(timescaleStr);
                                    var _repeatCount = Convert.ToInt64(_repeatCountStr);
                                    varDic[DASHTags.TemplateTime] = currentTime;
                                    varDic[DASHTags.TemplateNumber] = segNumber++;
                                    var hasTime = mediaTemplate!.Contains(DASHTags.TemplateTime);
                                    var media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                    var mediaUrl = ParserUtil.CombineURL(segBaseUrl, media!);
                                    MediaSegment mediaSegment = new();
                                    mediaSegment.Url = mediaUrl;
                                    if (hasTime)
                                        mediaSegment.NameFromVar = currentTime.ToString();
                                    mediaSegment.Duration = _duration / (double)timescale;
                                    mediaSegment.Index = segIndex++;
                                    streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                                    if (_repeatCount < 0)
                                    {
                                        //负数表示一直重复 直到period结束 注意减掉已经加入的1个片段
                                        _repeatCount = (long)Math.Ceiling(XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale / _duration) - 1;
                                    }
                                    for (long i = 0; i < _repeatCount; i++)
                                    {
                                        currentTime += _duration;
                                        MediaSegment _mediaSegment = new();
                                        varDic[DASHTags.TemplateTime] = currentTime;
                                        varDic[DASHTags.TemplateNumber] = segNumber++;
                                        var _hashTime = mediaTemplate!.Contains(DASHTags.TemplateTime);
                                        var _media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                        var _mediaUrl = ParserUtil.CombineURL(segBaseUrl, _media);
                                        _mediaSegment.Url = _mediaUrl;
                                        _mediaSegment.Index = segIndex++;
                                        _mediaSegment.Duration = _duration / (double)timescale;
                                        if (_hashTime)
                                            _mediaSegment.NameFromVar = currentTime.ToString();
                                        streamSpec.Playlist.MediaParts[0].MediaSegments.Add(_mediaSegment);
                                    }
                                    currentTime += _duration;
                                }
                            }
                            else
                            {
                                //没用SegmentTimeline 需要计算总分片数量 不精确
                                var timescale = Convert.ToInt32(timescaleStr);
                                var startNumber = Convert.ToInt64(startNumberStr);
                                var duration = Convert.ToInt32(durationStr);
                                var totalNumber = (long)Math.Ceiling(XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale / duration);
                                //直播的情况，需要自己计算totalNumber
                                if (totalNumber == 0 && isLive)
                                {
                                    var now = DateTime.Now;
                                    var availableTime = DateTime.Parse(availabilityStartTime!);
                                    //可用时间+偏移量
                                    var offsetMs = TimeSpan.FromMilliseconds(Convert.ToInt64(presentationTimeOffsetStr) / 1000);
                                    availableTime = availableTime.Add(offsetMs);
                                    var ts = now - availableTime;
                                    var updateTs = XmlConvert.ToTimeSpan(timeShiftBufferDepth!);
                                    //(当前时间到发布时间的时间差 - 最小刷新间隔) / 分片时长
                                    startNumber += (long)((ts.TotalSeconds - updateTs.TotalSeconds) * timescale / duration);
                                    totalNumber = (long)(updateTs.TotalSeconds * timescale / duration);
                                }
                                for (long index = startNumber, segIndex = 0; index < startNumber + totalNumber; index++, segIndex++)
                                {
                                    varDic[DASHTags.TemplateNumber] = index;
                                    var hasNumber = mediaTemplate!.Contains(DASHTags.TemplateNumber);
                                    var media = ParserUtil.ReplaceVars(mediaTemplate!, varDic);
                                    var mediaUrl = ParserUtil.CombineURL(segBaseUrl, media!);
                                    MediaSegment mediaSegment = new();
                                    mediaSegment.Url = mediaUrl;
                                    if (hasNumber)
                                        mediaSegment.NameFromVar = index.ToString();
                                    mediaSegment.Index = isLive ? index : segIndex; //直播直接用startNumber
                                    mediaSegment.Duration = duration / (double)timescale;
                                    streamSpec.Playlist.MediaParts[0].MediaSegments.Add(mediaSegment);
                                }
                            }
                        }

                        //如果依旧没被添加分片，直接把BaseUrl塞进去就好
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

                        //判断加密情况
                        if (adaptationSet.Elements().Concat(representation.Elements()).Any(e => e.Name.LocalName == "ContentProtection"))
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

                        //处理同一ID分散在不同Period的情况
                        var _index = streamList.FindIndex(_f => _f.PeriodId != streamSpec.PeriodId && _f.GroupId == streamSpec.GroupId && _f.Resolution == streamSpec.Resolution && _f.MediaType == streamSpec.MediaType);
                        if (_index > -1)
                        {
                            if (isLive)
                            {
                                //直播，这种情况直接略过新的
                            }
                            else
                            {
                                //点播，这种情况如果URL不同则作为新的part出现，否则仅把时间加起来
                                var url1 = streamList[_index].Playlist!.MediaParts.Last().MediaSegments.Last().Url;
                                var url2 = streamSpec.Playlist.MediaParts[0].MediaSegments.LastOrDefault()?.Url;
                                if (url1 != url2)
                                {
                                    var startIndex = streamList[_index].Playlist!.MediaParts.Last().MediaSegments.Last().Index + 1;
                                    var enumerator = streamSpec.Playlist.MediaParts[0].MediaSegments.GetEnumerator();
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
                            //修复mp4类型字幕
                            if (streamSpec.MediaType == MediaType.SUBTITLES && streamSpec.Extension == "mp4")
                            {
                                streamSpec.Extension = "m4s";
                            }
                            //分片默认后缀m4s
                            if (streamSpec.MediaType != MediaType.SUBTITLES && (streamSpec.Extension == null || streamSpec.Playlist.MediaParts.Sum(x => x.MediaSegments.Count) > 1))
                            {
                                streamSpec.Extension = "m4s";
                            }
                            streamList.Add(streamSpec);
                        }
                        //恢复BaseURL相对位置
                        segBaseUrl = representationsBaseUrl;
                    }
                    //恢复BaseURL相对位置
                    segBaseUrl = adaptationSetsBaseUrl;
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
                        item.AudioId = aL.OrderByDescending(x => x.Bandwidth).First().GroupId;
                    }
                    if (sL.Any())
                    {
                        item.SubtitleId = sL.OrderByDescending(x => x.Bandwidth).First().GroupId;
                    }
                }
            }

            return streamList;
        }

        /// <summary>
        /// 如果有非法字符 返回und
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        private string? FilterLanguage(string? v)
        {
            if (v == null) return null;
            if (Regex.IsMatch(v, "^[\\w_\\-\\d]+$")) return v;
            return "und";
        }

        public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
        {
            if (streamSpecs.Count == 0) return;

            var (rawText, url) = ("", ParserConfig.Url);
            try
            {
                (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.Url, ParserConfig.Headers);
            }
            catch (HttpRequestException) when (ParserConfig.Url!= ParserConfig.OriginalUrl)
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

        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            //这里才调用URL预处理器，节省开销
            await ProcessUrlAsync(streamSpecs);
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
                if (p.CanProcess(ExtractorType, MpdContent, ParserConfig))
                {
                    MpdContent = p.Process(MpdContent, ParserConfig);
                }
            }
        }
    }
}
