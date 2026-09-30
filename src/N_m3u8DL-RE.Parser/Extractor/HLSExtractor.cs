using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Util;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Parser.Extractor;

internal class HLSExtractor : IExtractor
{
    private static readonly HashSet<string> AudioCodecIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4a", "ac-3", "ec-3", "ec+3", "alac", "flac", "opus", "mp3",
        "dtsc", "dtse", "dtsh", "dtsl", "mha1", "mha2", "mhm1", "mhm2"
    };

    public ExtractorType ExtractorType => ExtractorType.HLS;

    private string M3u8Url = string.Empty;
    private string BaseUrl = string.Empty;
    private string M3u8Content = string.Empty;
    private bool MasterM3u8Flag = false;

    public ParserConfig ParserConfig { get; set; }

    public HLSExtractor(ParserConfig parserConfig)
    {
        this.ParserConfig = parserConfig;
        this.M3u8Url = parserConfig.Url ?? string.Empty;
        this.SetBaseUrl();
    }

    private void SetBaseUrl()
    {
        this.BaseUrl = !string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.BaseUrl : this.M3u8Url;
    }

    /// <summary>
    /// 预处理m3u8内容
    /// </summary>
    public void PreProcessContent()
    {
        M3u8Content = M3u8Content.Trim();
        if (!M3u8Content.StartsWith(HLSTags.ext_m3u))
        {
            throw new Exception(ResString.badM3u8);
        }

        foreach (var p in ParserConfig.ContentProcessors)
        {
            if (p.CanProcess(ExtractorType, M3u8Content, ParserConfig))
            {
                M3u8Content = p.Process(M3u8Content, ParserConfig);
            }
        }
    }

    /// <summary>
    /// 预处理URL
    /// </summary>
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

    private Task<List<StreamSpec>> ParseMasterListAsync()
    {
        MasterM3u8Flag = true;

        List<StreamSpec> streams = [];

        using StringReader sr = new StringReader(M3u8Content);
        string? line;
        bool expectPlaylist = false;
        StreamSpec streamSpec = new();

        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith(HLSTags.ext_x_stream_inf))
            {
                streamSpec = new();
                streamSpec.OriginalUrl = ParserConfig.OriginalUrl;
                var bandwidth = string.IsNullOrEmpty(ParserUtil.GetAttribute(line, "AVERAGE-BANDWIDTH")) ? ParserUtil.GetAttribute(line, "BANDWIDTH") : ParserUtil.GetAttribute(line, "AVERAGE-BANDWIDTH");
                streamSpec.Bandwidth = Convert.ToInt32(bandwidth);
                streamSpec.Codecs = ParserUtil.GetAttribute(line, "CODECS");
                streamSpec.Resolution = ParserUtil.GetAttribute(line, "RESOLUTION");

                var frameRate = ParserUtil.GetAttribute(line, "FRAME-RATE");
                if (!string.IsNullOrEmpty(frameRate))
                    streamSpec.FrameRate = Convert.ToDouble(frameRate);

                var audioId = ParserUtil.GetAttribute(line, "AUDIO");
                if (!string.IsNullOrEmpty(audioId))
                    streamSpec.AudioId = audioId;

                var videoId = ParserUtil.GetAttribute(line, "VIDEO");
                if (!string.IsNullOrEmpty(videoId))
                    streamSpec.VideoId = videoId;

                var subtitleId = ParserUtil.GetAttribute(line, "SUBTITLES");
                if (!string.IsNullOrEmpty(subtitleId))
                    streamSpec.SubtitleId = subtitleId;

                var videoRange = ParserUtil.GetAttribute(line, "VIDEO-RANGE");
                if (!string.IsNullOrEmpty(videoRange))
                    streamSpec.VideoRange = videoRange;

                // 清除多余的编码信息 dvh1.05.06,ec-3 => dvh1.05.06
                if (!string.IsNullOrEmpty(streamSpec.Codecs) && !string.IsNullOrEmpty(streamSpec.AudioId))
                {
                    streamSpec.Codecs = streamSpec.Codecs.Split(',')[0];
                }

                expectPlaylist = true;
            }
            else if (line.StartsWith(HLSTags.ext_x_media))
            {
                streamSpec = new();
                var type = ParserUtil.GetAttribute(line, "TYPE").Replace("-", "_");
                if (Enum.TryParse<MediaType>(type, out var mediaType))
                {
                    streamSpec.MediaType = mediaType;
                }

                // 跳过CLOSED_CAPTIONS类型（目前不支持）
                if (streamSpec.MediaType == MediaType.CLOSED_CAPTIONS)
                {
                    continue;
                }

                var url = ParserUtil.GetAttribute(line, "URI");

                /**
                 *    The URI attribute of the EXT-X-MEDIA tag is REQUIRED if the media
                      type is SUBTITLES, but OPTIONAL if the media type is VIDEO or AUDIO.
                      If the media type is VIDEO or AUDIO, a missing URI attribute
                      indicates that the media data for this Rendition is included in the
                      Media Playlist of any EXT-X-STREAM-INF tag referencing this EXT-
                      X-MEDIA tag.  If the media TYPE is AUDIO and the URI attribute is
                      missing, clients MUST assume that the audio data for this Rendition
                      is present in every video Rendition specified by the EXT-X-STREAM-INF
                      tag.

                      此处直接忽略URI属性为空的情况
                 */
                if (string.IsNullOrEmpty(url))
                {
                    continue;
                }

                url = ParserUtil.CombineURL(BaseUrl, url);
                streamSpec.Url = PreProcessUrl(url);

                var groupId = ParserUtil.GetAttribute(line, "GROUP-ID");
                streamSpec.GroupId = groupId;

                var lang = ParserUtil.GetAttribute(line, "LANGUAGE");
                if (!string.IsNullOrEmpty(lang))
                    streamSpec.Language = lang;

                var name = ParserUtil.GetAttribute(line, "NAME");
                if (!string.IsNullOrEmpty(name))
                    streamSpec.Name = name;

                var def = ParserUtil.GetAttribute(line, "DEFAULT");
                if (Enum.TryParse<Choise>(type, out var defaultChoise))
                {
                    streamSpec.Default = defaultChoise;
                }

                var channels = ParserUtil.GetAttribute(line, "CHANNELS");
                if (!string.IsNullOrEmpty(channels))
                    streamSpec.Channels = channels;

                var characteristics = ParserUtil.GetAttribute(line, "CHARACTERISTICS");
                if (!string.IsNullOrEmpty(characteristics))
                    streamSpec.Characteristics = characteristics.Split(',').Last().Split('.').Last();

                streams.Add(streamSpec);
            }
            else if (line.StartsWith('#'))
            {
                continue;
            }
            else if (expectPlaylist)
            {
                var url = ParserUtil.CombineURL(BaseUrl, line);
                streamSpec.Url = PreProcessUrl(url);
                expectPlaylist = false;
                streams.Add(streamSpec);
            }
        }

        return Task.FromResult(streams);
    }

    private static bool IsAudioOnlyVariant(StreamSpec stream)
    {
        if (stream.MediaType != null || string.IsNullOrWhiteSpace(stream.Codecs) ||
            stream.Resolution != null || stream.FrameRate != null ||
            stream.VideoRange != null || stream.VideoId != null || stream.AudioId != null)
            return false;

        return stream.Codecs.Split(',').All(codec => AudioCodecIds.Contains(codec.Trim().Split('.')[0]));
    }

    private Task<Playlist> ParseListAsync(StreamSpec? stream = null)
    {
        // 独立媒体播放列表没有轨道类型；无法识别的主变体沿用视频范围。
        var applyCustomHLS = stream == null || (ParserConfig.CustomHLSScope switch
        {
            CustomHlsScope.ALL => true,
            CustomHlsScope.VIDEO => stream.MediaType == MediaType.VIDEO ||
                                    (stream.MediaType == null && !IsAudioOnlyVariant(stream)),
            CustomHlsScope.AUDIO => stream.MediaType == MediaType.AUDIO || IsAudioOnlyVariant(stream),
            _ => false
        });
        var keyConfig = applyCustomHLS ? ParserConfig : ParserConfig.WithoutCustomHLSOverrides();

        // 标记是否已清除广告分片
        bool hasAd = false;
        bool allowHlsMultiExtMap = ParserConfig.CustomParserArgs.TryGetValue("AllowHlsMultiExtMap", out var allMultiExtMap) && allMultiExtMap == "true";
        if (allowHlsMultiExtMap)
        {
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.allowHlsMultiExtMap}[/]");
        }
        
        using StringReader sr = new StringReader(M3u8Content);
        string? line;
        bool expectSegment = false;
        bool isEndlist = false;
        long segIndex = 0;
        bool isAd = false;
        long startIndex;

        Playlist playlist = new();
        MediaSegment? currentInit = null;
        long discontinuitySequence = 0;
        // 点播保留全部 MAP；实验选项仍控制尚未支持切 init 的直播路径。
        var isVod = M3u8Content.Split('\n').Any(line => line.Trim() == HLSTags.ext_x_endlist ||
            line.Trim().StartsWith(HLSTags.ext_x_playlist_type) && line.Trim().EndsWith("VOD"));
        List<MediaPart> mediaParts = [];

        // 当前的加密信息
        EncryptInfo currentEncryptInfo = new();
        if (keyConfig.CustomMethod != null)
            currentEncryptInfo.Method = keyConfig.CustomMethod.Value;
        if (keyConfig.CustomeKey is { Length: > 0 })
            currentEncryptInfo.Key = keyConfig.CustomeKey;
        if (keyConfig.CustomeIV is { Length: > 0 })
            currentEncryptInfo.IV = keyConfig.CustomeIV;
        // 上次读取到的加密行，#EXT-X-KEY:……
        string lastKeyLine = "";

        MediaPart mediaPart = new();
        MediaSegment segment = new();
        List<MediaSegment> segments = [];


        while ((line = sr.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(line))
                continue;

            // 只下载部分字节
            if (line.StartsWith(HLSTags.ext_x_byterange))
            {
                var p = ParserUtil.GetAttribute(line);
                var (n, o) = ParserUtil.GetRange(p);
                segment.ExpectLength = n;
                // MAP 切换会关闭当前 part，隐式偏移仍应接续播放列表的上一媒体范围。
                var previous = segments.LastOrDefault() ?? mediaParts.LastOrDefault()?.MediaSegments.LastOrDefault();
                segment.StartRange = o ?? previous?.StartRange + previous?.ExpectLength
                    ?? throw new FormatException(ResString.hlsByteRangeMissingPrevious);
                expectSegment = true;
            }
            else if (line.StartsWith(HLSTags.ext_x_playlist_type))
            {
                isEndlist = line.Trim().EndsWith("VOD");
            }
            // 国家地理去广告
            else if (line.StartsWith("#UPLYNK-SEGMENT"))
            {
                if (line.Contains(",ad"))
                    isAd = true;
                else if (line.Contains(",segment"))
                    isAd = false;
            }
            // 国家地理去广告
            else if (isAd)
            {
                continue;
            }
            // 解析定义的分段长度
            else if (line.StartsWith(HLSTags.ext_x_targetduration))
            {
                playlist.TargetDuration = Convert.ToDouble(ParserUtil.GetAttribute(line));
            }
            // 解析起始编号
            else if (line.StartsWith(HLSTags.ext_x_media_sequence))
            {
                segIndex = Convert.ToInt64(ParserUtil.GetAttribute(line));
                startIndex = segIndex;
            }
            // program date time
            else if (line.StartsWith(HLSTags.ext_x_program_date_time))
            {
                segment.DateTime = DateTime.Parse(ParserUtil.GetAttribute(line));
            }
            else if (line.StartsWith(HLSTags.ext_x_discontinuity_sequence))
            {
                discontinuitySequence = Convert.ToInt64(ParserUtil.GetAttribute(line));
            }
            // 解析不连续标记，需要单独合并（timestamp不同）
            else if (line.StartsWith(HLSTags.ext_x_discontinuity))
            {
                // 修复去除广告后的遗留问题 去除discontinuity标记
                if (hasAd && mediaParts.Count > 0)
                {
                    segments = mediaParts[^1].MediaSegments;
                    currentInit = mediaParts[^1].MediaInit;
                    discontinuitySequence = mediaParts[^1].DiscontinuitySequence ?? discontinuitySequence;
                    mediaParts.RemoveAt(mediaParts.Count - 1);
                    hasAd = false;
                    continue;
                }
                // 常规情况的#EXT-X-DISCONTINUITY标记，新建part
                if (hasAd || segments.Count < 1)
                {
                    discontinuitySequence++;
                    continue;
                }
                
                mediaParts.Add(new MediaPart
                {
                    MediaInit = currentInit,
                    DiscontinuitySequence = discontinuitySequence,
                    MediaSegments = segments,
                });
                segments = new();
                discontinuitySequence++;
            }
            // 解析KEY
            else if (line.StartsWith(HLSTags.ext_x_key))
            {
                // 如果KEY line相同则不再重复解析
                if (line != lastKeyLine)
                {
                    // 调用处理器进行解析
                    var parsedInfo = ParseKey(line, keyConfig);
                    currentEncryptInfo.Method = parsedInfo.Method;
                    currentEncryptInfo.Key = parsedInfo.Key;
                    currentEncryptInfo.IV = parsedInfo.IV;
                }
                lastKeyLine = line;
            }
            // 解析分片时长
            else if (line.StartsWith(HLSTags.extinf))
            {
                string[] tmp = ParserUtil.GetAttribute(line).Split(',');
                segment.Duration = Convert.ToDouble(tmp[0]);
                segment.Index = segIndex;
                // 是否有加密，有的话写入KEY和IV
                if (currentEncryptInfo.Method != EncryptMethod.NONE)
                {
                    segment.EncryptInfo.Method = currentEncryptInfo.Method;
                    segment.EncryptInfo.Key = currentEncryptInfo.Key;
                    segment.EncryptInfo.IV = currentEncryptInfo.IV ?? HexUtil.HexToBytes(Convert.ToString(segIndex, 16).PadLeft(32, '0'));
                }
                expectSegment = true;
                segIndex++;
            }
            // m3u8主体结束
            else if (line.StartsWith(HLSTags.ext_x_endlist))
            {
                if (segments.Count > 0)
                {
                    mediaParts.Add(new MediaPart()
                    {
                        MediaInit = currentInit,
                        DiscontinuitySequence = discontinuitySequence,
                        MediaSegments = segments
                    });
                }
                segments = new();
                isEndlist = true;
            }
            // #EXT-X-MAP
            else if (line.StartsWith(HLSTags.ext_x_map))
            {
                var nextInit = new MediaSegment()
                {
                    Url = PreProcessUrl(ParserUtil.CombineURL(BaseUrl, ParserUtil.GetAttribute(line, "URI"))),
                    Index = -1, // 便于排序
                };
                if (line.Contains("BYTERANGE"))
                {
                    var p = ParserUtil.GetAttribute(line, "BYTERANGE");
                    var (n, o) = ParserUtil.GetRange(p);
                    nextInit.ExpectLength = n;
                    nextInit.StartRange = o ?? 0L;
                }
                // 有加密的话写入KEY和IV，MAP 的加密状态在声明时固定。
                if (currentEncryptInfo.Method != EncryptMethod.NONE)
                {
                    nextInit.EncryptInfo.Method = currentEncryptInfo.Method;
                    nextInit.EncryptInfo.Key = currentEncryptInfo.Key;
                    nextInit.EncryptInfo.IV = currentEncryptInfo.IV ?? HexUtil.HexToBytes(Convert.ToString(segIndex, 16).PadLeft(32, '0'));
                }
                var sameInit = currentInit != null && SameInit(currentInit, nextInit);
                if (sameInit)
                    continue;
                // 遇到其它 MAP 时点播按段处理；直播仍保留原先的截断保护。
                if (!hasAd)
                {
                    if (segments.Count > 0)
                    {
                        mediaParts.Add(new MediaPart()
                        {
                            MediaInit = currentInit,
                            DiscontinuitySequence = discontinuitySequence,
                            MediaSegments = segments
                        });
                    }
                    segments = new();
                    if (currentInit != null && !isVod && !allowHlsMultiExtMap)
                    {
                        isEndlist = true;
                        break;
                    }
                }
                currentInit = nextInit;
            }
            // 评论行不解析
            else if (line.StartsWith('#')) continue;
            // 空白行不解析
            else if (line.StartsWith("\r\n")) continue;
            // 解析分片的地址
            else if (expectSegment)
            {
                var segUrl = PreProcessUrl(ParserUtil.CombineURL(BaseUrl, line));
                segment.Url = segUrl;
                segments.Add(segment);
                segment = new();
                // 广告分段则清除此分片
                // 需要注意，遇到广告说明程序对上文的#EXT-X-DISCONTINUITY做出的动作是不必要的，
                // 其实上下文是同一种编码，需要恢复到原先的part上
                if (segUrl.Contains("ccode=") && segUrl.Contains("/ad/") && segUrl.Contains("duration="))
                {
                    segments.RemoveAt(segments.Count - 1);
                    segIndex--;
                    hasAd = true;
                }
                // 广告(4K分辨率测试)
                if (segUrl.Contains("ccode=0902") && segUrl.Contains("duration="))
                {
                    segments.RemoveAt(segments.Count - 1);
                    segIndex--;
                    hasAd = true;
                }
                expectSegment = false;
            }
        }

        // 直播的情况，无法遇到m3u8结束标记，需要手动将segments加入parts。
        // PLAYLIST-TYPE:VOD 即使未带 ENDLIST，也必须收进最后一组媒体。
        if (segments.Count > 0 || !isEndlist)
        {
            mediaParts.Add(new MediaPart()
            {
                MediaInit = currentInit,
                DiscontinuitySequence = discontinuitySequence,
                MediaSegments = segments
            });
        }

        playlist.MediaParts = mediaParts;
        playlist.IsLive = !isEndlist;
        // 直播尚未发布首片时仍需要保留 init，供原有录制流程初始化；点播去掉孤立 MAP。
        if (!playlist.IsLive)
            playlist.RemoveEmptyParts();

        // 直播刷新间隔
        if (playlist.IsLive)
        {
            // 由于播放器默认从最后3个分片开始播放 此处设置刷新间隔为TargetDuration的2倍
            playlist.RefreshIntervalMs = (int)((playlist.TargetDuration ?? 5) * 2 * 1000);
        }

        return Task.FromResult(playlist);
    }

    private EncryptInfo ParseKey(string keyLine, ParserConfig keyConfig)
    {
        foreach (var p in keyConfig.KeyProcessors)
        {
            if (p.CanProcess(ExtractorType, keyLine, M3u8Url, M3u8Content, keyConfig))
            {
                // 匹配到对应处理器后不再继续
                return p.Process(keyLine, M3u8Url, M3u8Content, keyConfig);
            }
        }

        throw new Exception(ResString.keyProcessorNotFound);
    }

    public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
    {
        this.M3u8Content = rawText;
        this.PreProcessContent();
        if (M3u8Content.Contains(HLSTags.ext_x_stream_inf))
        {
            Logger.Warn(ResString.masterM3u8Found);
            var lists = await ParseMasterListAsync();
            lists = lists.DistinctBy(p => p.Url).ToList();
            return lists;
        }

        var playlist = await ParseListAsync();
        return
        [
            new()
            {
                Url = ParserConfig.Url,
                Playlist = playlist,
                Extension = playlist.MediaParts.Any(part => part.MediaInit != null) ? "mp4" : "ts"
            }
        ];
    }

    private async Task LoadM3u8FromUrlAsync(string url)
    {
        // Logger.Info(ResString.loadingUrl + url);
        if (url.StartsWith("file:"))
        {
            var uri = new Uri(url);
            this.M3u8Content = File.ReadAllText(uri.LocalPath);
        }
        else if (url.StartsWith("http"))
        {
            try
            {
                (this.M3u8Content, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(url, ParserConfig.Headers);
            }
            catch (HttpRequestException) when (ParserConfig.OriginalUrl.StartsWith("http") && url != ParserConfig.OriginalUrl)
            {
                // 当URL无法访问时，再请求原始URL
                (this.M3u8Content, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.OriginalUrl, ParserConfig.Headers);
            }
        }

        this.M3u8Url = url;
        this.SetBaseUrl();
        this.PreProcessContent();
    }

    /// <summary>
    /// 从Master链接中刷新各个流的URL
    /// </summary>
    /// <param name="lists"></param>
    /// <returns></returns>
    private async Task RefreshUrlFromMaster(List<StreamSpec> lists)
    {
        // 重新加载master m3u8, 刷新选中流的URL
        await LoadM3u8FromUrlAsync(ParserConfig.Url);
        var newStreams = await ParseMasterListAsync();
        newStreams = newStreams.DistinctBy(p => p.Url).ToList();
        foreach (var l in lists)
        {
            var match = newStreams.Where(n => n.ToShortString() == l.ToShortString()).ToList();
            if (match.Count == 0) continue;
            
            Logger.DebugMarkUp($"{l.Url} => {match.First().Url}");
            l.Url = match.First().Url;
        }
    }

    public async Task FetchPlayListAsync(List<StreamSpec> lists)
    {
        for (int i = 0; i < lists.Count; i++)
        {
            try
            {
                // 直接重新加载m3u8
                await LoadM3u8FromUrlAsync(lists[i].Url!);
            }
            catch (HttpRequestException) when (MasterM3u8Flag)
            {
                Logger.WarnMarkUp("Can not load m3u8. Try refreshing url from master url...");
                // 当前URL无法加载 尝试从Master链接中刷新URL
                await RefreshUrlFromMaster(lists);
                await LoadM3u8FromUrlAsync(lists[i].Url!);
            }

            var newPlaylist = await ParseListAsync(MasterM3u8Flag ? lists[i] : null);
            var previousInits = lists[i].Playlist?.MediaParts.Select(part => part.MediaInit).OfType<MediaSegment>().ToList() ?? [];
            foreach (var part in newPlaylist.MediaParts)
            {
                // 刷新时相同的 init 复用原对象；新的 MAP 仍属于它实际覆盖的媒体段。
                // 直播消费者也可继续持有已下载对象，不会因为刷新而丢失文件字典键。
                var previous = part.MediaInit == null ? null : previousInits.FirstOrDefault(init => SameInit(init, part.MediaInit));
                if (previous != null)
                    part.MediaInit = previous;
            }
            lists[i].Playlist = newPlaylist;

            if (lists[i].MediaType == MediaType.SUBTITLES)
            {
                var a = lists[i].Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.Url.Contains(".ttml")));
                var b = lists[i].Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.Url.Contains(".vtt") || m.Url.Contains(".webvtt")));
                if (a) lists[i].Extension = "ttml";
                if (b) lists[i].Extension = "vtt";
            }
            else
            {
                lists[i].Extension = lists[i].Playlist!.MediaParts.Any(part => part.MediaInit != null) ? "m4s" : "ts";
            }
        }
    }

    public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
    {
        await FetchPlayListAsync(streamSpecs);
    }

    private static bool SameInit(MediaSegment a, MediaSegment b) =>
        a.Url == b.Url && a.StartRange == b.StartRange && a.ExpectLength == b.ExpectLength &&
        a.EncryptInfo.Method == b.EncryptInfo.Method &&
        (a.EncryptInfo.Key ?? []).SequenceEqual(b.EncryptInfo.Key ?? []) &&
        (a.EncryptInfo.IV ?? []).SequenceEqual(b.EncryptInfo.IV ?? []);
}
