﻿using System.Globalization;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.StreamParser.Constants;
using N_m3u8DL_RE.StreamParser.Util;

namespace N_m3u8DL_RE.StreamParser.Extractor
{
    internal sealed class HLSExtractor : IExtractor
    {
        public ExtractorType ExtractorType => ExtractorType.HLS;

        private string M3u8Url = string.Empty;
        private string BaseUrl = string.Empty;
        private string M3u8Content = string.Empty;
        private bool MasterM3u8Flag;

        public ParserConfig ParserConfig { get; set; }

        public HLSExtractor(ParserConfig parserConfig)
        {
            ParserConfig = parserConfig;
            M3u8Url = parserConfig.Url ?? string.Empty;
            SetBaseUrl();
        }

        private void SetBaseUrl()
        {
            BaseUrl = !string.IsNullOrEmpty(ParserConfig.BaseUrl) ? ParserConfig.BaseUrl : M3u8Url;
        }

        /// <summary>
        /// 预处理m3u8内容
        /// </summary>
        public void PreProcessContent()
        {
            M3u8Content = M3u8Content.Trim();
            if (!M3u8Content.StartsWith(HLSTags.ext_m3u, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(ResString.BadM3u8);
            }

            foreach (Processor.ContentProcessor p in ParserConfig.ContentProcessors)
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
            foreach (Processor.UrlProcessor p in ParserConfig.UrlProcessors)
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

            using StringReader sr = new(M3u8Content);
            string? line;
            bool expectPlaylist = false;
            StreamSpec streamSpec = new();

            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                if (line.StartsWith(HLSTags.ext_x_stream_inf, StringComparison.OrdinalIgnoreCase))
                {
                    streamSpec = new()
                    {
                        OriginalUrl = ParserConfig.OriginalUrl
                    };
                    string bandwidth = string.IsNullOrEmpty(ParserUtil.GetAttribute(line, "AVERAGE-BANDWIDTH")) ? ParserUtil.GetAttribute(line, "BANDWIDTH") : ParserUtil.GetAttribute(line, "AVERAGE-BANDWIDTH");
                    streamSpec.Bandwidth = Convert.ToInt32(bandwidth, CultureInfo.InvariantCulture);
                    streamSpec.Codecs = ParserUtil.GetAttribute(line, "CODECS");
                    streamSpec.Resolution = ParserUtil.GetAttribute(line, "RESOLUTION");

                    string frameRate = ParserUtil.GetAttribute(line, "FRAME-RATE");
                    if (!string.IsNullOrEmpty(frameRate))
                    {
                        streamSpec.FrameRate = Convert.ToDouble(frameRate, CultureInfo.InvariantCulture);
                    }

                    string audioId = ParserUtil.GetAttribute(line, "AUDIO");
                    if (!string.IsNullOrEmpty(audioId))
                    {
                        streamSpec.AudioId = audioId;
                    }

                    string videoId = ParserUtil.GetAttribute(line, "VIDEO");
                    if (!string.IsNullOrEmpty(videoId))
                    {
                        streamSpec.VideoId = videoId;
                    }

                    string subtitleId = ParserUtil.GetAttribute(line, "SUBTITLES");
                    if (!string.IsNullOrEmpty(subtitleId))
                    {
                        streamSpec.SubtitleId = subtitleId;
                    }

                    string videoRange = ParserUtil.GetAttribute(line, "VIDEO-RANGE");
                    if (!string.IsNullOrEmpty(videoRange))
                    {
                        streamSpec.VideoRange = videoRange;
                    }

                    // 清除多余的编码信息 dvh1.05.06,ec-3 => dvh1.05.06
                    if (!string.IsNullOrEmpty(streamSpec.Codecs) && !string.IsNullOrEmpty(streamSpec.AudioId))
                    {
                        streamSpec.Codecs = streamSpec.Codecs.Split(',')[0];
                    }

                    expectPlaylist = true;
                }
                else if (line.StartsWith(HLSTags.ext_x_media, StringComparison.OrdinalIgnoreCase))
                {
                    streamSpec = new();
                    string type = ParserUtil.GetAttribute(line, "TYPE").Replace("-", "_");
                    if (Enum.TryParse(type, out MediaType mediaType))
                    {
                        streamSpec.MediaType = mediaType;
                    }

                    // 跳过CLOSED_CAPTIONS类型（目前不支持）
                    if (streamSpec.MediaType == MediaType.CLOSEDCAPTIONS)
                    {
                        continue;
                    }

                    string url = ParserUtil.GetAttribute(line, "URI");

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

                    string groupId = ParserUtil.GetAttribute(line, "GROUP-ID");
                    streamSpec.GroupId = groupId;

                    string lang = ParserUtil.GetAttribute(line, "LANGUAGE");
                    if (!string.IsNullOrEmpty(lang))
                    {
                        streamSpec.Language = lang;
                    }

                    string name = ParserUtil.GetAttribute(line, "NAME");
                    if (!string.IsNullOrEmpty(name))
                    {
                        streamSpec.Name = name;
                    }

                    string def = ParserUtil.GetAttribute(line, "DEFAULT");
                    if (Enum.TryParse(type, out Choice defaultChoise))
                    {
                        streamSpec.Default = defaultChoise;
                    }

                    string channels = ParserUtil.GetAttribute(line, "CHANNELS");
                    if (!string.IsNullOrEmpty(channels))
                    {
                        streamSpec.Channels = channels;
                    }

                    string characteristics = ParserUtil.GetAttribute(line, "CHARACTERISTICS");
                    if (!string.IsNullOrEmpty(characteristics))
                    {
                        streamSpec.Characteristics = characteristics.Split(',').Last().Split('.').Last();
                    }

                    streams.Add(streamSpec);
                }
                else if (line.StartsWith('#'))
                {
                    continue;
                }
                else if (expectPlaylist)
                {
                    string url = ParserUtil.CombineURL(BaseUrl, line);
                    streamSpec.Url = PreProcessUrl(url);
                    expectPlaylist = false;
                    streams.Add(streamSpec);
                }
            }

            return Task.FromResult(streams);
        }

        private Task<Playlist> ParseListAsync()
        {
            // 标记是否已清除广告分片
            bool hasAd = false;
            ;
            bool allowHlsMultiExtMap = ParserConfig.CustomParserArgs.TryGetValue("AllowHlsMultiExtMap", out string? allMultiExtMap) && allMultiExtMap == "true";
            if (allowHlsMultiExtMap)
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.AllowHlsMultiExtMap}[/]");
            }

            using StringReader sr = new(M3u8Content);
            string? line;
            bool expectSegment = false;
            bool isEndlist = false;
            long segIndex = 0;
            bool isAd = false;
            long startIndex;

            Playlist playlist = new();
            List<MediaPart> mediaParts = [];

            // 当前的加密信息
            EncryptInfo currentEncryptInfo = new();
            if (ParserConfig.CustomMethod != null)
            {
                currentEncryptInfo.Method = ParserConfig.CustomMethod.Value;
            }

            if (ParserConfig.CustomeKey is { Length: > 0 })
            {
                currentEncryptInfo.Key = ParserConfig.CustomeKey;
            }

            if (ParserConfig.CustomeIV is { Length: > 0 })
            {
                currentEncryptInfo.IV = ParserConfig.CustomeIV;
            }
            // 上次读取到的加密行，#EXT-X-KEY:……
            string lastKeyLine = "";

            MediaPart mediaPart = new();
            MediaSegment segment = new();
            List<MediaSegment> segments = [];


            while ((line = sr.ReadLine()) != null)
            {
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                // 只下载部分字节
                if (line.StartsWith(HLSTags.ext_x_byterange, StringComparison.OrdinalIgnoreCase))
                {
                    string p = ParserUtil.GetAttribute(line);
                    (long n, long? o) = ParserUtil.GetRange(p);
                    segment.ExpectLength = n;
                    segment.StartRange = o ?? segments.Last().StartRange + segments.Last().ExpectLength;
                    expectSegment = true;
                }
                else if (line.StartsWith(HLSTags.ext_x_playlist_type, StringComparison.OrdinalIgnoreCase))
                {
                    isEndlist = line.Trim().EndsWith("VOD", StringComparison.OrdinalIgnoreCase);
                }
                // 国家地理去广告
                else if (line.StartsWith("#UPLYNK-SEGMENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (line.Contains(",ad"))
                    {
                        isAd = true;
                    }
                    else if (line.Contains(",segment"))
                    {
                        isAd = false;
                    }
                }
                // 国家地理去广告
                else if (isAd)
                {
                    continue;
                }
                // 解析定义的分段长度
                else if (line.StartsWith(HLSTags.ext_x_targetduration, StringComparison.OrdinalIgnoreCase))
                {
                    playlist.TargetDuration = Convert.ToDouble(ParserUtil.GetAttribute(line), CultureInfo.InvariantCulture);
                }
                // 解析起始编号
                else if (line.StartsWith(HLSTags.ext_x_media_sequence, StringComparison.OrdinalIgnoreCase))
                {
                    segIndex = Convert.ToInt64(ParserUtil.GetAttribute(line), CultureInfo.InvariantCulture);
                    startIndex = segIndex;
                }
                // program date time
                else if (line.StartsWith(HLSTags.ext_x_program_date_time, StringComparison.OrdinalIgnoreCase))
                {
                    segment.DateTime = DateTime.Parse(ParserUtil.GetAttribute(line), CultureInfo.InvariantCulture);
                }
                // 解析不连续标记，需要单独合并（timestamp不同）
                else if (line.StartsWith(HLSTags.ext_x_discontinuity, StringComparison.OrdinalIgnoreCase))
                {
                    // 修复YK去除广告后的遗留问题
                    if (hasAd && mediaParts.Count > 0)
                    {
                        segments = mediaParts[^1].MediaSegments;
                        mediaParts.RemoveAt(mediaParts.Count - 1);
                        hasAd = false;
                        continue;
                    }
                    // 常规情况的#EXT-X-DISCONTINUITY标记，新建part
                    if (hasAd || segments.Count < 1)
                    {
                        continue;
                    }

                    mediaParts.Add(new MediaPart
                    {
                        MediaSegments = segments,
                    });
                    segments = [];
                }
                // 解析KEY
                else if (line.StartsWith(HLSTags.ext_x_key, StringComparison.OrdinalIgnoreCase))
                {
                    string uri = ParserUtil.GetAttribute(line, "URI");
                    string uri_last = ParserUtil.GetAttribute(lastKeyLine, "URI");

                    // 如果KEY URL相同，不进行重复解析
                    if (uri != uri_last)
                    {
                        // 调用处理器进行解析
                        EncryptInfo parsedInfo = ParseKey(line);
                        currentEncryptInfo.Method = parsedInfo.Method;
                        currentEncryptInfo.Key = parsedInfo.Key;
                        currentEncryptInfo.IV = parsedInfo.IV;
                    }
                    lastKeyLine = line;
                }
                // 解析分片时长
                else if (line.StartsWith(HLSTags.extinf, StringComparison.OrdinalIgnoreCase))
                {
                    string[] tmp = ParserUtil.GetAttribute(line).Split(',');
                    segment.Duration = Convert.ToDouble(tmp[0], CultureInfo.InvariantCulture);
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
                else if (line.StartsWith(HLSTags.ext_x_endlist, StringComparison.OrdinalIgnoreCase))
                {
                    if (segments.Count > 0)
                    {
                        mediaParts.Add(new MediaPart()
                        {
                            MediaSegments = segments
                        });
                    }
                    segments = [];
                    isEndlist = true;
                }
                // #EXT-X-MAP
                else if (line.StartsWith(HLSTags.ext_x_map, StringComparison.OrdinalIgnoreCase))
                {
                    if (playlist.MediaInit == null || hasAd)
                    {
                        playlist.MediaInit = new MediaSegment()
                        {
                            Url = PreProcessUrl(ParserUtil.CombineURL(BaseUrl, ParserUtil.GetAttribute(line, "URI"))),
                            Index = -1, // 便于排序
                        };
                        if (line.Contains("BYTERANGE"))
                        {
                            string p = ParserUtil.GetAttribute(line, "BYTERANGE");
                            (long n, long? o) = ParserUtil.GetRange(p);
                            playlist.MediaInit.ExpectLength = n;
                            playlist.MediaInit.StartRange = o ?? 0L;
                        }
                        if (currentEncryptInfo.Method == EncryptMethod.NONE)
                        {
                            continue;
                        }
                        // 有加密的话写入KEY和IV
                        playlist.MediaInit.EncryptInfo.Method = currentEncryptInfo.Method;
                        playlist.MediaInit.EncryptInfo.Key = currentEncryptInfo.Key;
                        playlist.MediaInit.EncryptInfo.IV = currentEncryptInfo.IV ?? HexUtil.HexToBytes(Convert.ToString(segIndex, 16).PadLeft(32, '0'));
                    }
                    // 遇到了其他的map，说明已经不是一个视频了，全部丢弃即可
                    else
                    {
                        if (segments.Count > 0)
                        {
                            mediaParts.Add(new MediaPart()
                            {
                                MediaSegments = segments
                            });
                        }
                        segments = [];
                        if (!allowHlsMultiExtMap)
                        {
                            isEndlist = true;
                            break;
                        }
                    }
                }
                // 评论行不解析
                else if (line.StartsWith('#'))
                {
                    continue;
                }
                // 空白行不解析
                else if (line.StartsWith("\r\n", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                // 解析分片的地址
                else if (expectSegment)
                {
                    string segUrl = PreProcessUrl(ParserUtil.CombineURL(BaseUrl, line));
                    segment.Url = segUrl;
                    segments.Add(segment);
                    segment = new();
                    // YK的广告分段则清除此分片
                    // 需要注意，遇到广告说明程序对上文的#EXT-X-DISCONTINUITY做出的动作是不必要的，
                    // 其实上下文是同一种编码，需要恢复到原先的part上
                    if (segUrl.Contains("ccode=") && segUrl.Contains("/ad/") && segUrl.Contains("duration="))
                    {
                        segments.RemoveAt(segments.Count - 1);
                        segIndex--;
                        hasAd = true;
                    }
                    // YK广告(4K分辨率测试)
                    if (segUrl.Contains("ccode=0902") && segUrl.Contains("duration="))
                    {
                        segments.RemoveAt(segments.Count - 1);
                        segIndex--;
                        hasAd = true;
                    }
                    expectSegment = false;
                }
            }

            // 直播的情况，无法遇到m3u8结束标记，需要手动将segments加入parts
            if (!isEndlist)
            {
                mediaParts.Add(new MediaPart()
                {
                    MediaSegments = segments
                });
            }

            playlist.MediaParts = mediaParts;
            playlist.IsLive = !isEndlist;

            // 直播刷新间隔
            if (playlist.IsLive)
            {
                // 由于播放器默认从最后3个分片开始播放 此处设置刷新间隔为TargetDuration的2倍
                playlist.RefreshIntervalMs = (int)((playlist.TargetDuration ?? 5) * 2 * 1000);
            }

            return Task.FromResult(playlist);
        }

        private EncryptInfo ParseKey(string keyLine)
        {
            foreach (Processor.KeyProcessor p in ParserConfig.KeyProcessors)
            {
                if (p.CanProcess(ExtractorType, keyLine, M3u8Url, M3u8Content, ParserConfig))
                {
                    // 匹配到对应处理器后不再继续
                    return p.Process(keyLine, M3u8Url, M3u8Content, ParserConfig);
                }
            }

            throw new InvalidOperationException(ResString.KeyProcessorNotFound);
        }

        public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            M3u8Content = rawText;
            PreProcessContent();
            if (M3u8Content.Contains(HLSTags.ext_x_stream_inf))
            {
                Logger.Warn(ResString.MasterM3u8Found);
                List<StreamSpec> lists = await ParseMasterListAsync();
                lists = [.. lists.DistinctBy(p => p.Url)];
                return lists;
            }

            Playlist playlist = await ParseListAsync();
            return
            [
                new()
                {
                    Url = ParserConfig.Url,
                    Playlist = playlist,
                    Extension = playlist.MediaInit != null ? "mp4" : "ts"
                }
            ];
        }

        private async Task LoadM3u8FromUrlAsync(string url)
        {
            // Logger.Info(ResString.loadingUrl + url);
            if (url.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                Uri uri = new(url);
                M3u8Content = File.ReadAllText(uri.LocalPath);
            }
            else if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    (M3u8Content, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(url, ParserConfig.Headers);
                }
                catch (HttpRequestException) when (url != ParserConfig.OriginalUrl)
                {
                    // 当URL无法访问时，再请求原始URL
                    (M3u8Content, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(ParserConfig.OriginalUrl, ParserConfig.Headers);
                }
            }

            M3u8Url = url;
            SetBaseUrl();
            PreProcessContent();
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
            List<StreamSpec> newStreams = await ParseMasterListAsync();
            newStreams = [.. newStreams.DistinctBy(p => p.Url)];
            foreach (StreamSpec l in lists)
            {
                List<StreamSpec> match = [.. newStreams.Where(n => n.ToShortString() == l.ToShortString())];
                if (match.Count == 0)
                {
                    continue;
                }

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

                Playlist newPlaylist = await ParseListAsync();
                if (lists[i].Playlist?.MediaInit != null)
                {
                    lists[i].Playlist!.MediaParts = newPlaylist.MediaParts; // 不更新init
                }
                else
                {
                    lists[i].Playlist = newPlaylist;
                }

                if (lists[i].MediaType == MediaType.SUBTITLES)
                {
                    bool a = lists[i].Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.Url.Contains(".ttml")));
                    bool b = lists[i].Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.Url.Contains(".vtt") || m.Url.Contains(".webvtt")));
                    if (a)
                    {
                        lists[i].Extension = "ttml";
                    }

                    if (b)
                    {
                        lists[i].Extension = "vtt";
                    }
                }
                else
                {
                    lists[i].Extension = lists[i].Playlist!.MediaInit != null ? "m4s" : "ts";
                }
            }
        }

        public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
        {
            await FetchPlayListAsync(streamSpecs);
        }
    }
}