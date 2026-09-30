using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Util;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace N_m3u8DL_RE.Parser.Extractor;

// https://blog.csdn.net/leek5533/article/details/117750191
internal partial class DASHExtractor2 : IExtractor
{
    private static EncryptMethod DEFAULT_METHOD = EncryptMethod.CENC;

    public ExtractorType ExtractorType => ExtractorType.MPEG_DASH;

    private string MpdUrl = string.Empty;
    private string BaseUrl = string.Empty;
    private string MpdContent = string.Empty;
    private readonly TimeProvider timeProvider;
    public ParserConfig ParserConfig { get; set; }

    public DASHExtractor2(ParserConfig parserConfig, TimeProvider? timeProvider = null)
    {
        this.ParserConfig = parserConfig;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        SetInitUrl();
    }


    private void SetInitUrl()
    {
        this.MpdUrl = ParserConfig.Url ?? string.Empty;
        this.BaseUrl = !string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.BaseUrl : this.MpdUrl;
    }

    private string ExtendBaseUrl(XElement element, string oriBaseUrl)
    {
        var target = element.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL");
        if (target != null)
        {
            oriBaseUrl = ParserUtil.CombineURL(oriBaseUrl, target.Value);
        }

        return oriBaseUrl;
    }

    private double? GetFrameRate(XElement element)
    {
        var frameRate = element.Attribute("frameRate")?.Value;
        if (frameRate == null || !frameRate.Contains('/')) return null;
        
        var d = Convert.ToDouble(frameRate.Split('/')[0]) / Convert.ToDouble(frameRate.Split('/')[1]);
        frameRate = d.ToString("0.000");
        return Convert.ToDouble(frameRate);
    }

    public Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
    {
        var streamList = new List<StreamSpec>();

        this.MpdContent = rawText;
        this.PreProcessContent();


        var xmlDocument = XDocument.Parse(MpdContent);

        // 选中第一个MPD节点
        var mpdElement = xmlDocument.Elements().First(e => e.Name.LocalName == "MPD");

        // 类型 static点播, dynamic直播
        var type = mpdElement.Attribute("type")?.Value;
        bool isLive = type == "dynamic";
        TimeSpan? minimumUpdatePeriod = null;
        var minimumUpdatePeriodValue = mpdElement.Attribute("minimumUpdatePeriod")?.Value;
        if (isLive && !string.IsNullOrWhiteSpace(minimumUpdatePeriodValue))
        {
            try
            {
                var value = XmlConvert.ToTimeSpan(minimumUpdatePeriodValue);
                if (value > TimeSpan.Zero) minimumUpdatePeriod = value;
            }
            catch (Exception ex) when (ex is FormatException or OverflowException)
            {
                // 无效的更新周期按未提供处理，仍可根据分片时长刷新。
            }
        }

        // 分片最大时长
        var maxSegmentDuration = mpdElement.Attribute("maxSegmentDuration")?.Value;
        // 分片从该时间起可用
        var availabilityStartTime = mpdElement.Attribute("availabilityStartTime")?.Value;
        // 在availabilityStartTime的前XX段时间，分片有效
        var timeShiftBufferDepth = mpdElement.Attribute("timeShiftBufferDepth")?.Value;
        if (string.IsNullOrEmpty(timeShiftBufferDepth))
        {
            // 如果没有 默认一分钟有效
            timeShiftBufferDepth = "PT1M";
        }
        // MPD发布时间
        var publishTime = mpdElement.Attribute("publishTime")?.Value;
        // MPD总时长
        var mediaPresentationDuration = mpdElement.Attribute("mediaPresentationDuration")?.Value;

        // 读取在MPD开头定义的<BaseURL>，并替换本身的URL
        var baseUrlElement = mpdElement.Elements().FirstOrDefault(e => e.Name.LocalName == "BaseURL");
        if (baseUrlElement != null)
        {
            var baseUrl = baseUrlElement.Value;
            if (baseUrl.Contains("kkbox.com.tw/")) baseUrl = baseUrl.Replace("//https:%2F%2F", "//");
            this.BaseUrl = ParserUtil.CombineURL(this.MpdUrl, baseUrl);
        }

        // 全部Period
        var periods = mpdElement.Elements().Where(e => e.Name.LocalName == "Period").ToList();
        var periodTimings = ResolvePeriodTimings(periods, mediaPresentationDuration);
        for (var periodIndex = 0; periodIndex < periods.Count; periodIndex++)
        {
            var period = periods[periodIndex];
            // 本Period时长
            var (periodStartSeconds, periodDurationSeconds) = periodTimings[periodIndex];
            var periodDuration = periodDurationSeconds is { } seconds
                ? XmlConvert.ToString(TimeSpan.FromSeconds(seconds)) : period.Attribute("duration")?.Value;

            // 本Period ID
            var periodId = period.Attribute("id")?.Value;

            // 最终分片会使用的baseurl
            var segBaseUrl = this.BaseUrl;

            // 处理baseurl嵌套
            segBaseUrl = ExtendBaseUrl(period, segBaseUrl);

            var adaptationSetsBaseUrl = segBaseUrl;

            // 本Period中的全部AdaptationSet
            var adaptationSets = period.Elements().Where(e => e.Name.LocalName == "AdaptationSet");
            foreach (var adaptationSet in adaptationSets)
            {
                // 处理baseurl嵌套
                segBaseUrl = ExtendBaseUrl(adaptationSet, segBaseUrl);

                var representationsBaseUrl = segBaseUrl;

                var mimeType = adaptationSet.Attribute("contentType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                var frameRate = GetFrameRate(adaptationSet);
                // 本AdaptationSet中的全部Representation
                var representations = adaptationSet.Elements().Where(e => e.Name.LocalName == "Representation");
                foreach (var representation in representations)
                {
                    // 处理baseurl嵌套
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
                    streamSpec.Playlist.MediaParts.Add(new MediaPart
                    {
                        PeriodIndex = periodIndex,
                        PeriodId = periodId,
                        PeriodStart = periodStartSeconds,
                        PeriodDuration = periodDurationSeconds,
                        RepresentationId = representation.Attribute("id")?.Value,
                    });
                    var mediaPart = streamSpec.Playlist.MediaParts[0];
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
                    // 特殊处理
                    if (representation.Attribute("volumeAdjust") != null)
                    {
                        streamSpec.GroupId += "-" + representation.Attribute("volumeAdjust")?.Value;
                    }
                    // 推测后缀名
                    var mType = representation.Attribute("mimeType")?.Value ?? adaptationSet.Attribute("mimeType")?.Value;
                    if (mType != null)
                    {
                        var mTypeSplit = mType.Split('/');
                        streamSpec.Extension = mTypeSplit.Length == 2 ? mTypeSplit[1] : null;
                    }
                    // 优化字幕场景识别
                    if (streamSpec.Codecs is "stpp" or "wvtt")
                    {
                        streamSpec.MediaType = MediaType.SUBTITLES;
                    }
                    // 优化字幕场景识别
                    var role = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "Role") ?? adaptationSet.Elements().FirstOrDefault(e => e.Name.LocalName == "Role");
                    if (role != null)
                    {
                        var roleValue = role.Attribute("value")?.Value;
                        if (Enum.TryParse(roleValue, true, out RoleType roleType))
                        {
                            streamSpec.Role = roleType;

                            if (roleType == RoleType.Subtitle)
                            {
                                streamSpec.MediaType = MediaType.SUBTITLES;
                                if (mType != null && mType.Contains("ttml"))
                                    streamSpec.Extension = "ttml";
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
                                        streamSpec.Extension = "ttml";
                                }
                            }
                        }
                    }
                    streamSpec.Playlist.IsLive = isLive;
                    streamSpec.Playlist.MinimumUpdatePeriod = minimumUpdatePeriod;
                    // 设置刷新间隔 timeShiftBufferDepth / 2
                    if (timeShiftBufferDepth != null)
                    {
                        streamSpec.Playlist.RefreshIntervalMs = XmlConvert.ToTimeSpan(timeShiftBufferDepth).TotalMilliseconds / 2;
                    }

                    // 读取声道数量
                    var audioChannelConfiguration = adaptationSet.Elements().Concat(representation.Elements()).FirstOrDefault(e => e.Name.LocalName == "AudioChannelConfiguration");
                    if (audioChannelConfiguration != null)
                    {
                        streamSpec.Channels = audioChannelConfiguration.Attribute("value")?.Value;
                    }

                    // 发布时间
                    if (!string.IsNullOrEmpty(publishTime))
                    {
                        streamSpec.PublishTime = DateTime.Parse(publishTime);
                    }


                    // 第一种形式 SegmentBase
                    var segmentBaseElement = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentBase");
                    if (segmentBaseElement != null)
                    {
                        mediaPart.PresentationTimeOffset =
                            Convert.ToDouble(segmentBaseElement.Attribute("presentationTimeOffset")?.Value ?? "0", CultureInfo.InvariantCulture) /
                            Convert.ToDouble(segmentBaseElement.Attribute("timescale")?.Value ?? "1", CultureInfo.InvariantCulture);
                        // 处理init url
                        var initialization = segmentBaseElement.Elements().FirstOrDefault(e => e.Name.LocalName == "Initialization");
                        if (initialization != null)
                        {
                            var sourceURL = initialization.Attribute("sourceURL")?.Value;
                            if (sourceURL == null)
                            {
                                mediaPart.MediaSegments.Add
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
                                mediaPart.MediaInit = new MediaSegment();
                                mediaPart.MediaInit.Index = -1; // 便于排序
                                mediaPart.MediaInit.Url = initUrl;
                                if (initRange != null)
                                {
                                    var (start, expect) = ParserUtil.ParseRange(initRange);
                                    mediaPart.MediaInit.StartRange = start;
                                    mediaPart.MediaInit.ExpectLength = expect;
                                }
                            }
                        }
                    }

                    // 第二种形式 SegmentList.SegmentList
                    var segmentList = representation.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentList");
                    if (segmentList != null)
                    {
                        mediaPart.PresentationTimeOffset =
                            Convert.ToDouble(segmentList.Attribute("presentationTimeOffset")?.Value ?? "0", CultureInfo.InvariantCulture) /
                            Convert.ToDouble(segmentList.Attribute("timescale")?.Value ?? "1", CultureInfo.InvariantCulture);
                        var durationStr = segmentList.Attribute("duration")?.Value;
                        // 处理init url
                        var initialization = segmentList.Elements().FirstOrDefault(e => e.Name.LocalName == "Initialization");
                        if (initialization != null)
                        {
                            var initUrl = ParserUtil.CombineURL(segBaseUrl, initialization.Attribute("sourceURL")?.Value!);
                            var initRange = initialization.Attribute("range")?.Value;
                            mediaPart.MediaInit = new MediaSegment();
                            mediaPart.MediaInit.Index = -1; // 便于排序
                            mediaPart.MediaInit.Url = initUrl;
                            if (initRange != null)
                            {
                                var (start, expect) = ParserUtil.ParseRange(initRange);
                                mediaPart.MediaInit.StartRange = start;
                                mediaPart.MediaInit.ExpectLength = expect;
                            }
                        }
                        // 处理分片
                        var segmentURLs = segmentList.Elements().Where(e => e.Name.LocalName == "SegmentURL").ToList();
                        var timescaleStr = segmentList.Attribute("timescale")?.Value ?? "1";
                        for (int segmentIndex = 0; segmentIndex < segmentURLs.Count; segmentIndex++)
                        {
                            var segmentURL = segmentURLs.ElementAt(segmentIndex);
                            var mediaUrl = ParserUtil.CombineURL(segBaseUrl, segmentURL.Attribute("media")?.Value!);
                            var mediaRange = segmentURL.Attribute("mediaRange")?.Value;
                            var timesacle = Convert.ToInt32(timescaleStr);
                            var duration = Convert.ToInt64(durationStr);
                            MediaSegment mediaSegment = new();
                            mediaSegment.Duration = duration / (double)timesacle;
                            mediaSegment.PresentationTime = (mediaPart.PresentationTimeOffset ?? 0) + segmentIndex * duration / (double)timesacle;
                            mediaSegment.Url = mediaUrl;
                            mediaSegment.Index = segmentIndex;
                            if (mediaRange != null)
                            {
                                var (start, expect) = ParserUtil.ParseRange(mediaRange);
                                mediaSegment.StartRange = start;
                                mediaSegment.ExpectLength = expect;
                            }
                            mediaPart.MediaSegments.Add(mediaSegment);
                        }
                    }

                    // 第三种形式 SegmentTemplate+SegmentTimeline
                    // 通配符有$RepresentationID$ $Bandwidth$ $Number$ $Time$

                    // adaptationSets中的segmentTemplate
                    var segmentTemplateElementsOuter = adaptationSet.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                    // representation中的segmentTemplate
                    var segmentTemplateElements = representation.Elements().Where(e => e.Name.LocalName == "SegmentTemplate");
                    if (segmentTemplateElements.Any() || segmentTemplateElementsOuter.Any())
                    {
                        // 优先使用最近的元素
                        var segmentTemplate = (segmentTemplateElements.FirstOrDefault() ?? segmentTemplateElementsOuter.FirstOrDefault())!;
                        var segmentTemplateOuter = (segmentTemplateElementsOuter.FirstOrDefault() ?? segmentTemplateElements.FirstOrDefault())!;
                        var varDic = new Dictionary<string, object?>();
                        varDic[DASHTags.TemplateRepresentationID] = streamSpec.GroupId;
                        varDic[DASHTags.TemplateBandwidth] = bandwidth?.Value;
                        // presentationTimeOffset
                        var presentationTimeOffsetStr = segmentTemplate.Attribute("presentationTimeOffset")?.Value ?? segmentTemplateOuter.Attribute("presentationTimeOffset")?.Value ?? "0";
                        // timesacle
                        var timescaleStr = segmentTemplate.Attribute("timescale")?.Value ?? segmentTemplateOuter.Attribute("timescale")?.Value ?? "1";
                        mediaPart.PresentationTimeOffset =
                            Convert.ToDouble(presentationTimeOffsetStr, CultureInfo.InvariantCulture) /
                            Convert.ToDouble(timescaleStr, CultureInfo.InvariantCulture);
                        var durationStr = segmentTemplate.Attribute("duration")?.Value ?? segmentTemplateOuter.Attribute("duration")?.Value;
                        var startNumberStr = segmentTemplate.Attribute("startNumber")?.Value ?? segmentTemplateOuter.Attribute("startNumber")?.Value ?? "1";
                        // 处理init url
                        var initialization = segmentTemplate.Attribute("initialization")?.Value ?? segmentTemplateOuter.Attribute("initialization")?.Value;
                        if (initialization != null)
                        {
                            var _init = ParserUtil.ReplaceVars(initialization, varDic);
                            var initUrl = ParserUtil.CombineURL(segBaseUrl, _init);
                            mediaPart.MediaInit = new MediaSegment();
                            mediaPart.MediaInit.Index = -1; // 便于排序
                            mediaPart.MediaInit.Url = initUrl;
                        }
                        // 处理分片
                        var mediaTemplate = segmentTemplate.Attribute("media")?.Value ?? segmentTemplateOuter.Attribute("media")?.Value;
                        var segmentTimeline = segmentTemplate.Elements().FirstOrDefault(e => e.Name.LocalName == "SegmentTimeline");
                        if (segmentTimeline != null)
                        {
                            // 使用了SegmentTimeline 结果精确
                            var segNumber = Convert.ToInt64(startNumberStr);
                            var Ss = segmentTimeline.Elements().Where(e => e.Name.LocalName == "S");
                            var currentTime = 0L;
                            var segIndex = 0;
                            foreach (var S in Ss)
                            {
                                // 每个S元素包含三个属性:@t(start time)\@r(repeat count)\@d(duration)
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
                                mediaSegment.PresentationTime = currentTime / (double)timescale;
                                mediaSegment.Index = segIndex++;
                                mediaPart.MediaSegments.Add(mediaSegment);
                                if (_repeatCount < 0)
                                {
                                    // r=-1 重复到下一个显式 t 或本 Period 结束；结束时间在源时间轴上，
                                    // 需要加 PTO 并扣掉当前 t，不能重复整个 Period 的时长。
                                    var nextTime = S.ElementsAfterSelf().FirstOrDefault(e => e.Name.LocalName == "S")?.Attribute("t")?.Value;
                                    var endTime = nextTime != null ? Convert.ToDouble(nextTime, CultureInfo.InvariantCulture)
                                        : XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale
                                            + Convert.ToDouble(presentationTimeOffsetStr, CultureInfo.InvariantCulture);
                                    _repeatCount = Math.Max(0, (long)Math.Ceiling((endTime - currentTime) / _duration) - 1);
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
                                    _mediaSegment.PresentationTime = currentTime / (double)timescale;
                                    if (_hashTime)
                                        _mediaSegment.NameFromVar = currentTime.ToString();
                                    mediaPart.MediaSegments.Add(_mediaSegment);
                                }
                                currentTime += _duration;
                            }
                        }
                        else
                        {
                            // 没用SegmentTimeline 需要计算总分片数量 不精确
                            var timescale = Convert.ToInt32(timescaleStr);
                            var startNumber = Convert.ToInt64(startNumberStr);
                            var duration = Convert.ToInt64(durationStr);
                            var totalNumber = (long)Math.Ceiling(XmlConvert.ToTimeSpan(periodDuration ?? mediaPresentationDuration ?? "PT0S").TotalSeconds * timescale / duration);
                            // 直播的情况，需要自己计算totalNumber
                            if (totalNumber == 0 && isLive)
                            {
                                var now = timeProvider.GetUtcNow();
                                var availableTime = DateTimeOffset.Parse(availabilityStartTime!, CultureInfo.InvariantCulture);
                                // 可用时间+偏移量
                                // presentationTimeOffset 的单位是 timescale, 不是毫秒
                                var offset = TimeSpan.FromSeconds(Convert.ToDouble(presentationTimeOffsetStr) / timescale);
                                availableTime = availableTime.Add(offset);
                                var ts = now - availableTime;
                                var updateTs = XmlConvert.ToTimeSpan(timeShiftBufferDepth!);
                                // (当前时间到发布时间的时间差 - 最小刷新间隔) / 分片时长
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
                                mediaSegment.Index = isLive ? index : segIndex; // 直播直接用startNumber
                                mediaSegment.Duration = duration / (double)timescale;
                                mediaSegment.PresentationTime = (mediaPart.PresentationTimeOffset ?? 0) + (index - startNumber) * duration / (double)timescale;
                                mediaPart.MediaSegments.Add(mediaSegment);
                            }
                        }
                    }

                    // 去除重复分片(重叠Period/SegmentTimeline/connectivity duplicates等会导致同一分片被引用多次)
                    // 以 URL + 字节范围 作为唯一标识, 保持原始顺序, 避免同一分片被下载两次 (#684)
                    var _segs = mediaPart.MediaSegments;
                    if (_segs.Count > 1)
                    {
                        var _seen = new HashSet<string>();
                        var _deduped = _segs.Where(s => _seen.Add($"{s.Url}|{s.StartRange}|{s.ExpectLength}")).ToList();
                        if (_deduped.Count != _segs.Count)
                        {
                            Logger.Debug($"[DASH] removed {_segs.Count - _deduped.Count} duplicate segment(s) in {streamSpec.GroupId}");
                            mediaPart.MediaSegments = _deduped;
                        }
                    }

                    // 如果依旧没被添加分片，直接把BaseUrl塞进去就好
                    if (mediaPart.MediaSegments.Count == 0)
                    {
                        mediaPart.MediaSegments.Add
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
                        if (mediaPart.MediaInit != null)
                        {
                            mediaPart.MediaInit.EncryptInfo.Method = DEFAULT_METHOD;
                        }
                        foreach (var item in mediaPart.MediaSegments)
                        {
                            item.EncryptInfo.Method = DEFAULT_METHOD;
                        }

                        // 尝试从 cenc:default_KID 提取 KID
                        XNamespace cencNs = "urn:mpeg:cenc:2013";
                        var cpKid = adaptationSet.Elements().Concat(representation.Elements())
                            .FirstOrDefault(e => e.Name.LocalName == "ContentProtection" && e.Attribute(cencNs + "default_KID") != null);
                        if (cpKid != null && mediaPart.MediaInit != null)
                        {
                            var kidRaw = cpKid.Attribute(cencNs + "default_KID")!.Value;
                            // UUID格式 -> 十六进制小写无分隔符
                            mediaPart.MediaInit.EncryptInfo.KID = kidRaw.Replace("-", "").ToLower();
                        }
                    }

                    mediaPart.Codecs = streamSpec.Codecs;
                    // 处理同一ID分散在不同Period的情况；点播先保留 Period，选流后再编排。
                    var _index = isLive ? streamList.FindIndex(_f => _f.PeriodId != streamSpec.PeriodId && _f.GroupId == streamSpec.GroupId && _f.Resolution == streamSpec.Resolution && _f.MediaType == streamSpec.MediaType) : -1;
                    if (_index > -1)
                    {
                        // 直播，这种情况直接略过新的；直播多 Period 单独改造。
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
        var aL = streamList.Where(s => s.MediaType == MediaType.AUDIO).ToList();
        var sL = streamList.Where(s => s.MediaType == MediaType.SUBTITLES).ToList();
        foreach (var item in streamList.Where(item => !string.IsNullOrEmpty(item.Resolution)))
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
    private string? FilterLanguage(string? v)
    {
        if (v == null) return null;
        return LangCodeRegex().IsMatch(v) ? v : "und";
    }

    private static (double? Start, double? Duration)[] ResolvePeriodTimings(List<XElement> periods, string? presentationDuration)
    {
        static double? Read(string? value) => value == null ? null : XmlConvert.ToTimeSpan(value).TotalSeconds;
        var starts = periods.Select(p => Read(p.Attribute("start")?.Value)).ToArray();
        var durations = periods.Select(p => Read(p.Attribute("duration")?.Value)).ToArray();
        if (starts.Length == 0)
            return [];
        starts[0] ??= 0;
        var end = Read(presentationDuration);
        // start/duration 可以相互推导，不能把整个 MPD 时长当作每个 Period 时长。
        for (var pass = 0; pass < periods.Count; pass++)
        {
            for (var i = 0; i < periods.Count; i++)
            {
                if (i > 0 && starts[i - 1] is { } previousStart && durations[i - 1] is { } previousDuration)
                    starts[i] ??= previousStart + previousDuration;
                var nextStart = i + 1 < periods.Count ? starts[i + 1] : end;
                if (starts[i] is { } start && nextStart is { } stop && stop >= start)
                    durations[i] ??= stop - start;
            }
        }
        return starts.Select((start, i) => (start, durations[i])).ToArray();
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
            // 当URL无法访问时，再请求原始URL
            (rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.OriginalUrl, ParserConfig.Headers);
        }

        ParserConfig.Url = url;
        SetInitUrl();

        var newStreams = await ExtractStreamsAsync(rawText);
        foreach (var streamSpec in streamSpecs)
        {
            // 有的网站每次请求MPD返回的码率不一致，导致ToShortString()无法匹配 无法更新playlist
            // 故增加通过init url来匹配 (如果有的话)
            var match = newStreams.Where(n => n.ToShortString() == streamSpec.ToShortString());
            if (!match.Any())
                match = newStreams.Where(n => n.Playlist?.MediaParts.FirstOrDefault()?.MediaInit?.Url == streamSpec.Playlist?.MediaParts.FirstOrDefault()?.MediaInit?.Url);

            if (match.Any())
                streamSpec.Playlist!.MediaParts = match.First().Playlist!.MediaParts; // 不更新init
        }
        // 这里才调用URL预处理器，节省开销
        await ProcessUrlAsync(streamSpecs);
    }

    private Task ProcessUrlAsync(List<StreamSpec> streamSpecs)
    {
        foreach (var streamSpec in streamSpecs)
        {
            var playlist = streamSpec.Playlist;
            if (playlist == null) continue;
            
            var inits = playlist.MediaParts.Select(part => part.MediaInit)
                .OfType<MediaSegment>().Distinct<MediaSegment>(ReferenceEqualityComparer.Instance);
            foreach (MediaSegment init in inits)
            {
                init.Url = PreProcessUrl(init.Url);
            }
            for (var ii = 0; ii < playlist!.MediaParts.Count; ii++)
            {
                var part = playlist.MediaParts[ii];
                foreach (var mediaSegment in part.MediaSegments)
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

    [GeneratedRegex(@"^[\w_\-\d]+$")]
    private static partial Regex LangCodeRegex();
}
