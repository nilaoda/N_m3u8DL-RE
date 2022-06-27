using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace N_m3u8DL_RE.Parser.Extractor
{
    //code from https://github.com/ytdl-org/youtube-dl/blob/master/youtube_dl/extractor/common.py#L2076
    internal class DASHExtractor : IExtractor
    {
        private string MpdUrl = string.Empty;
        private string BaseUrl = string.Empty;
        private string MpdContent = string.Empty;

        public ParserConfig ParserConfig { get; set; }

        public DASHExtractor(ParserConfig parserConfig)
        {
            this.ParserConfig = parserConfig;
            this.MpdUrl = parserConfig.Url ?? string.Empty;
            if (!string.IsNullOrEmpty(parserConfig.BaseUrl))
                this.BaseUrl = parserConfig.BaseUrl;
        }


        public async Task<List<StreamSpec>> ExtractStreamsAsync(string rawText)
        {
            this.MpdContent = rawText;
            this.PreProcessContent();


            XmlDocument mpdDoc = new XmlDocument();
            mpdDoc.LoadXml(MpdContent);

            XmlNode xn = null;
            //Select MPD node
            foreach (XmlNode node in mpdDoc.ChildNodes)
            {
                if (node.NodeType == XmlNodeType.Element && node.Name == "MPD")
                {
                    xn = node;
                    break;
                }
            }

            var mediaPresentationDuration = ((XmlElement)xn).GetAttribute("mediaPresentationDuration");
            var ns = ((XmlElement)xn).GetAttribute("xmlns");

            XmlNamespaceManager nsMgr = new XmlNamespaceManager(mpdDoc.NameTable);
            nsMgr.AddNamespace("ns", ns);

            TimeSpan ts = XmlConvert.ToTimeSpan(mediaPresentationDuration); //时长

            //读取在MPD开头定义的<BaseURL>，并替换本身的URL
            var baseNode = xn.SelectSingleNode("ns:BaseURL", nsMgr);
            if (baseNode != null)
            {
                if (MpdUrl.Contains("kkbox.com.tw/"))
                {
                    var badUrl = baseNode.InnerText;
                    var goodUrl = badUrl.Replace("//https:%2F%2F", "//");
                    BaseUrl = goodUrl;
                }
                else
                {
                    BaseUrl = baseNode.InnerText;
                }
            }

            var formatList = new List<JsonObject>(); //存放所有音视频清晰度
            var periodIndex = 0; //解决同一个period且同id导致被重复添加分片


            foreach (XmlElement period in xn.SelectNodes("ns:Period", nsMgr))
            {
                periodIndex++;
                var periodDuration = string.IsNullOrEmpty(period.GetAttribute("duration")) ? XmlConvert.ToTimeSpan(mediaPresentationDuration) : XmlConvert.ToTimeSpan(period.GetAttribute("duration"));
                var periodMsInfo = ExtractMultisegmentInfo(period, nsMgr, new JsonObject()
                {
                    ["StartNumber"] = 1,
                    ["Timescale"] = 1
                });
                foreach (XmlElement adaptationSet in period.SelectNodes("ns:AdaptationSet", nsMgr))
                {
                    var adaptionSetMsInfo = ExtractMultisegmentInfo(adaptationSet, nsMgr, periodMsInfo);
                    foreach (XmlElement representation in adaptationSet.SelectNodes("ns:Representation", nsMgr))
                    {
                        string GetAttribute(string key)
                        {
                            var v1 = representation.GetAttribute(key);
                            if (string.IsNullOrEmpty(v1))
                                return adaptationSet.GetAttribute(key);
                            return v1;
                        }

                        var mimeType = GetAttribute("mimeType");
                        var contentType = mimeType.Split('/')[0];
                        if (contentType == "video" || contentType == "audio" || contentType == "text")
                        {
                            var baseUrl = "";
                            bool CheckBaseUrl()
                            {
                                return Regex.IsMatch(baseUrl, @"^https?://");
                            }

                            var list = new List<XmlNodeList>()
                            {
                                representation.ChildNodes,
                                adaptationSet.ChildNodes,
                                period.ChildNodes,
                                mpdDoc.ChildNodes
                            };

                            foreach (XmlNodeList xmlNodeList in list)
                            {
                                foreach (XmlNode node in xmlNodeList)
                                {
                                    if (node.Name == "BaseURL")
                                    {
                                        baseUrl = node.InnerText + baseUrl;
                                        if (CheckBaseUrl()) break;
                                    }
                                }
                                if (CheckBaseUrl()) break;
                            }

                            string GetBaseUrl(string url)
                            {
                                if (url.Contains("?"))
                                    url = url.Remove(url.LastIndexOf('?'));
                                url = url.Substring(0, url.LastIndexOf('/') + 1);
                                return url;
                            }

                            var mpdBaseUrl = string.IsNullOrEmpty(BaseUrl) ? GetBaseUrl(MpdUrl) : BaseUrl;
                            if (!string.IsNullOrEmpty(mpdBaseUrl) && !CheckBaseUrl())
                            {
                                if (!mpdBaseUrl.EndsWith("/") && !baseUrl.StartsWith("/"))
                                {
                                    mpdBaseUrl += "/";
                                }
                                baseUrl = ParserUtil.CombineURL(mpdBaseUrl, baseUrl);
                            }
                            var representationId = GetAttribute("id");
                            var lang = GetAttribute("lang");
                            var bandwidth = IntOrNull(GetAttribute("bandwidth"));
                            var frameRate = GetAttribute("frameRate");
                            if (frameRate.Contains("/"))
                            {
                                var d = Convert.ToDouble(frameRate.Split('/')[0]) / Convert.ToDouble(frameRate.Split('/')[1]);
                                frameRate = d.ToString("0.000");
                            }
                            var f = new JsonObject()
                            {
                                ["PeriodIndex"] = periodIndex,
                                ["ContentType"] = contentType,
                                ["FormatId"] = representationId,
                                ["ManifestUrl"] = MpdUrl,
                                ["Width"] = IntOrNull(GetAttribute("width")),
                                ["Height"] = IntOrNull(GetAttribute("height")),
                                ["Tbr"] = (int)DoubleOrNull(bandwidth),
                                ["Asr"] = IntOrNull(GetAttribute("audioSamplingRate")),
                                ["Fps"] = DoubleOrNull(frameRate),
                                ["Language"] = lang,
                                ["Codecs"] = GetAttribute("codecs")
                            };

                            var representationMsInfo = ExtractMultisegmentInfo(representation, nsMgr, adaptionSetMsInfo);

                            string PrepareTemplate(string templateName, string[] identifiers)
                            {
                                var tmpl = representationMsInfo?[templateName]?.GetValue<string>();
                                var t = new StringBuilder();
                                var inTemplate = false;
                                foreach (var ch in tmpl)
                                {
                                    t.Append(ch);
                                    if (ch == '$')
                                    {
                                        inTemplate = !inTemplate;
                                    }
                                    else if (ch == '%' && !inTemplate)
                                    {
                                        t.Append(ch);
                                    }
                                }
                                var str = t.ToString();
                                str = str.Replace("$RepresentationID$", representationId);
                                str = Regex.Replace(str, "\\$(" + string.Join("|", identifiers) + ")\\$", "{{$1}}");
                                str = Regex.Replace(str, "\\$(" + string.Join("|", identifiers) + ")%([^$]+)d\\$", "{{$1}}{0:D$2}");
                                str = str.Replace("$$", "$");
                                return str;
                            }

                            string PadNumber(string template, string key, long value)
                            {
                                string ReplaceFirst(string text, string search, string replace)
                                {
                                    int pos = text.IndexOf(search);
                                    if (pos < 0)
                                    {
                                        return text;
                                    }
                                    return text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
                                }

                                template = template.Replace("{{" + key + "}}", "");
                                var m = Regex.Match(template, "{0:D(\\d+)}");
                                return ReplaceFirst(template, m.Value, value.ToString("0".PadRight(Convert.ToInt32(m.Groups[1].Value), '0')));
                            }

                            if (representationMsInfo.ContainsKey("Initialization"))
                            {
                                var initializationTemplate = PrepareTemplate("Initialization", new string[] { "Bandwidth" });
                                var initializationUrl = "";
                                if (initializationTemplate.Contains("{0:D"))
                                {
                                    if (initializationTemplate.Contains("{{Bandwidth}}"))
                                        initializationUrl = PadNumber(initializationTemplate, "Bandwidth", bandwidth);
                                }
                                else
                                {
                                    initializationUrl = initializationTemplate.Replace("{{Bandwidth}}", bandwidth.ToString());
                                }
                                representationMsInfo["InitializationUrl"] = ParserUtil.CombineURL(baseUrl, initializationUrl);
                            }

                            string LocationKey(string location)
                            {
                                return Regex.IsMatch(location, "^https?://") ? "url" : "path";
                            }

                            if (!representationMsInfo.ContainsKey("SegmentUrls") && representationMsInfo.ContainsKey("Media"))
                            {
                                var mediaTemplate = PrepareTemplate("Media", new string[] { "Number", "Bandwidth", "Time" });
                                var mediaLocationKey = LocationKey(mediaTemplate);

                                if (mediaTemplate.Contains("{{Number") && !representationMsInfo.ContainsKey("S"))
                                {
                                    var segmentDuration = 0.0;
                                    if (!representationMsInfo.ContainsKey("TotalNumber") && representationMsInfo.ContainsKey("SegmentDuration"))
                                    {
                                        segmentDuration = DoubleOrNull(representationMsInfo["SegmentDuration"].GetValue<double>(), representationMsInfo["Timescale"].GetValue<int>());
                                        representationMsInfo["TotalNumber"] = (long)Math.Ceiling(periodDuration.TotalSeconds / segmentDuration);
                                    }
                                    var fragments = new JsonArray();
                                    for (int i = representationMsInfo["StartNumber"].GetValue<int>(); i < representationMsInfo["StartNumber"].GetValue<int>() + representationMsInfo["TotalNumber"].GetValue<long>(); i++)
                                    {
                                        var segUrl = "";
                                        if (mediaTemplate.Contains("{0:D"))
                                        {
                                            if (mediaTemplate.Contains("{{Bandwidth}}"))
                                                segUrl = PadNumber(mediaTemplate, "Bandwidth", bandwidth);
                                            if (mediaTemplate.Contains("{{Number}}"))
                                                segUrl = PadNumber(mediaTemplate, "Number", i);
                                        }
                                        else
                                        {
                                            segUrl = mediaTemplate.Replace("{{Bandwidth}}", bandwidth.ToString());
                                            segUrl = segUrl.Replace("{{Number}}", i.ToString());
                                        }
                                        fragments.Add(new JsonObject()
                                        {
                                            [mediaLocationKey] = ParserUtil.CombineURL(baseUrl, segUrl),
                                            ["duration"] = segmentDuration
                                        });
                                    }
                                    representationMsInfo["Fragments"] = fragments;
                                }
                                else
                                {
                                    var fragments = new JsonArray();

                                    var segmentTime = 0L;
                                    var segmentD = 0L;
                                    var segmentNumber = representationMsInfo["StartNumber"].GetValue<int>();

                                    void addSegmentUrl()
                                    {
                                        var segUrl = "";
                                        if (mediaTemplate.Contains("{0:D"))
                                        {
                                            if (mediaTemplate.Contains("{{Bandwidth}}"))
                                                segUrl = PadNumber(mediaTemplate, "Bandwidth", bandwidth);
                                            if (mediaTemplate.Contains("{{Number}}"))
                                                segUrl = PadNumber(mediaTemplate, "Number", segmentNumber);
                                            if (mediaTemplate.Contains("{{Time}}"))
                                                segUrl = PadNumber(mediaTemplate, "Time", segmentTime);
                                        }
                                        else
                                        {
                                            segUrl = mediaTemplate.Replace("{{Bandwidth}}", bandwidth.ToString());
                                            segUrl = segUrl.Replace("{{Number}}", segmentNumber.ToString());
                                            segUrl = segUrl.Replace("{{Time}}", segmentTime.ToString());
                                        }
                                        fragments.Add(new JsonObject()
                                        {
                                            [mediaLocationKey] = ParserUtil.CombineURL(baseUrl, segUrl),
                                            ["duration"] = DoubleOrNull(segmentD, representationMsInfo["Timescale"].GetValue<int>())
                                        });
                                    }

                                    if (representationMsInfo.ContainsKey("S"))
                                    {
                                        var array = representationMsInfo["S"].AsArray();
                                        for (int i = 0; i < array.Count; i++)
                                        {
                                            var s = array[i];
                                            segmentTime = s["t"].GetValue<long>() == 0L ? segmentTime : s["t"].GetValue<long>();
                                            segmentD = s["d"].GetValue<long>();
                                            addSegmentUrl();
                                            segmentNumber++;
                                            for (int j = 0; j < s["r"].GetValue<long>(); j++)
                                            {
                                                segmentTime += segmentD;
                                                addSegmentUrl();
                                                segmentNumber++;
                                            }
                                            segmentTime += segmentD;
                                        }
                                    }
                                    representationMsInfo["Fragments"] = fragments;
                                }
                            }
                            else if (representationMsInfo.ContainsKey("SegmentUrls") && representationMsInfo.ContainsKey("S"))
                            {
                                var fragments = new JsonArray();

                                var segmentIndex = 0;
                                var timescale = representationMsInfo["Timescale"].GetValue<int>();
                                foreach (var s in representationMsInfo["S"].AsArray())
                                {
                                    var duration = DoubleOrNull(s["d"], timescale);
                                    for (int j = 0; j < s["r"].GetValue<long>() + 1; j++)
                                    {
                                        var segmentUri = representationMsInfo["SegmentUrls"][segmentIndex].GetValue<string>();
                                        fragments.Add(new JsonObject()
                                        {
                                            [LocationKey(segmentUri)] = ParserUtil.CombineURL(baseUrl, segmentUri),
                                            ["duration"] = duration
                                        });
                                        segmentIndex++;
                                    }
                                }

                                representationMsInfo["Fragments"] = fragments;
                            }
                            else if (representationMsInfo.ContainsKey("SegmentUrls"))
                            {
                                var fragments = new JsonArray();

                                var segmentDuration = DoubleOrNull(representationMsInfo["SegmentDuration"].GetValue<double>(), representationMsInfo.ContainsKey("SegmentDuration") ? representationMsInfo["Timescale"].GetValue<int>() : 1);
                                foreach (var jsonNode in representationMsInfo["SegmentUrls"].AsArray())
                                {
                                    var segmentUrl = jsonNode.GetValue<string>();
                                    if (segmentDuration != -1)
                                    {
                                        fragments.Add(new JsonObject()
                                        {
                                            [LocationKey(segmentUrl)] = ParserUtil.CombineURL(baseUrl, segmentUrl),
                                            ["duration"] = segmentDuration
                                        });
                                    }
                                    else
                                    {
                                        fragments.Add(new JsonObject()
                                        {
                                            [LocationKey(segmentUrl)] = ParserUtil.CombineURL(baseUrl, segmentUrl)
                                        });
                                    }
                                }

                                representationMsInfo["Fragments"] = fragments;
                            }

                            if (representationMsInfo.ContainsKey("Fragments"))
                            {
                                f["Url"] = string.IsNullOrEmpty(MpdUrl) ? baseUrl : MpdUrl;
                                f["FragmentBaseUrl"] = baseUrl;
                                if (representationMsInfo.ContainsKey("InitializationUrl"))
                                {
                                    f["InitializationUrl"] = ParserUtil.CombineURL(baseUrl, representationMsInfo["InitializationUrl"].GetValue<string>());
                                    if (f["InitializationUrl"].GetValue<string>().StartsWith("$$Range"))
                                    {
                                        f["InitializationUrl"] = ParserUtil.CombineURL(baseUrl, f["InitializationUrl"].GetValue<string>());
                                    }
                                    f["Fragments"] = JsonArray.Parse(representationMsInfo["Fragments"].ToJsonString());
                                }
                            }
                            else
                            {
                                //整段mp4
                                f["Fragments"] = new JsonArray() {
                                    new JsonObject()
                                    {
                                        ["url"] = baseUrl,
                                        ["duration"] = ts.TotalSeconds
                                    }
                                };
                            }

                            //处理同一ID分散在不同Period的情况
                            if (formatList.Any(_f => _f["FormatId"].ToJsonString() == f["FormatId"].ToJsonString() && _f["Width"].ToJsonString() == f["Width"].ToJsonString() && _f["ContentType"].ToJsonString() == f["ContentType"].ToJsonString()))
                            {
                                for (int i = 0; i < formatList.Count; i++)
                                {
                                    //参数相同但不在同一个Period才可以
                                    if (formatList[i]["FormatId"].ToJsonString() == f["FormatId"].ToJsonString() && formatList[i]["Width"].ToJsonString() == f["Width"].ToJsonString() && formatList[i]["ContentType"].ToJsonString() == f["ContentType"].ToJsonString() && formatList[i]["PeriodIndex"].ToJsonString() != f["PeriodIndex"].ToJsonString())
                                    {
                                        var array = formatList[i]["Fragments"].AsArray();
                                        foreach (var item in f["Fragments"].AsArray())
                                        {
                                            array.Add(item);
                                        }
                                        formatList[i]["Fragments"] = array;
                                        break;
                                    }
                                }
                            }
                            else
                            {
                                formatList.Add(f);
                            }
                        }
                    }
                }
            }

            var streamList = new List<StreamSpec>();
            foreach (var item in formatList)
            {
                //基本信息
                StreamSpec streamSpec = new();
                streamSpec.GroupId = item["FormatId"].GetValue<string>();
                streamSpec.Bandwidth = item["Tbr"].GetValue<int>();
                streamSpec.Codecs = item["Codecs"].GetValue<string>();
                streamSpec.Language = item["Language"].GetValue<string>();
                streamSpec.FrameRate = item["Fps"].GetValue<double>();
                streamSpec.Resolution = item["Width"].GetValue<int>() != -1 ? $"{item["Width"]}x{item["Height"]}" : "";
                streamSpec.Url = MpdUrl;
                streamSpec.MediaType = item["ContentType"].GetValue<string>() switch
                {
                    "text" => MediaType.SUBTITLES,
                    "audio" => MediaType.AUDIO,
                    _ => null
                };

                //组装分片
                Playlist playlist = new();
                List<MediaSegment> segments = new();
                //Initial URL
                if (item.ContainsKey("InitializationUrl"))
                {
                    var initUrl = item["InitializationUrl"].GetValue<string>();
                    if (Regex.IsMatch(initUrl, "\\$\\$Range=(\\d+)-(\\d+)"))
                    {
                        var match = Regex.Match(initUrl, "\\$\\$Range=(\\d+)-(\\d+)");
                        string rangeStr = match.Value;
                        long start = Convert.ToInt64(match.Groups[1].Value);
                        long end = Convert.ToInt64(match.Groups[2].Value);
                        playlist.MediaInit = new MediaSegment()
                        {
                            EncryptInfo = new EncryptInfo()
                            {
                                Method = EncryptMethod.UNKNOWN
                            },
                            Url = PreProcessUrl(initUrl.Replace(rangeStr, "")),
                            StartRange = start,
                            ExpectLength = end - start + 1
                        };
                    }
                    else
                    {
                        playlist.MediaInit = new MediaSegment()
                        {
                            Url = PreProcessUrl(initUrl)
                        };
                    }
                }
                //分片地址
                var fragments = item["Fragments"].AsArray();
                var index = 0;
                foreach (var fragment in fragments)
                {
                    var seg = fragment.AsObject();
                    var dur = seg.ContainsKey("duration") ? seg["duration"].GetValue<double>() : 0.0;
                    var url = seg.ContainsKey("url") ? seg["url"].GetValue<string>() : seg["path"].GetValue<string>();
                    url = PreProcessUrl(url);
                    MediaSegment mediaSegment = new()
                    {
                        Index = index,
                        Duration = dur,
                        Url = url,
                    };

                    if (Regex.IsMatch(url, "\\$\\$Range=(\\d+)-(\\d+)"))
                    {
                        var match = Regex.Match(url, "\\$\\$Range=(\\d+)-(\\d+)");
                        string rangeStr = match.Value;
                        long start = Convert.ToInt64(match.Groups[1].Value);
                        long end = Convert.ToInt64(match.Groups[2].Value);
                        mediaSegment.StartRange = start;
                        mediaSegment.ExpectLength = end - start + 1;
                    }

                    segments.Add(mediaSegment);
                    index++;
                }
                playlist.MediaParts = new List<MediaPart>()
                {
                    new MediaPart()
                    {
                        MediaSegments = segments
                    }
                };

                //统一添加EncryptInfo
                if (playlist.MediaInit != null)
                {
                    playlist.MediaInit.EncryptInfo = new EncryptInfo()
                    {
                        Method = EncryptMethod.UNKNOWN
                    };
                }
                foreach (var seg in playlist.MediaParts[0].MediaSegments)
                {
                    seg.EncryptInfo = new EncryptInfo()
                    {
                        Method = EncryptMethod.UNKNOWN
                    };
                }
                streamSpec.Playlist = playlist;
                streamList.Add(streamSpec);
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

        bool CheckValid(string url)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(new Uri(url));
                if (ParserConfig.Headers != null)
                {
                    foreach (var item in ParserConfig.Headers)
                    {
                        request.Headers.Add(item.Key, item.Value);
                    }
                }
                request.Timeout = 120000;
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                if (((int)response.StatusCode).ToString().StartsWith("2")) return true;
                else return false;
            }
            catch (Exception) { return false; }
        }

        static double DoubleOrNull(object text, int scale = 1)
        {
            try
            {
                return Convert.ToDouble(text) / scale;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        static int IntOrNull(string text, int scale = 1)
        {
            try
            {
                return Convert.ToInt32(text) / scale;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private JsonObject ExtractMultisegmentInfo(XmlElement period, XmlNamespaceManager nsMgr, JsonObject jsonObject)
        {
            var MultisegmentInfo = new JsonObject();
            foreach (var item in jsonObject)
            {
                MultisegmentInfo[item.Key] = JsonNode.Parse(item.Value.ToJsonString());
            }
            void ExtractCommon(XmlNode source)
            {
                var sourceE = (XmlElement)source;
                var segmentTimeline = source.SelectSingleNode("ns:SegmentTimeline", nsMgr);
                if (segmentTimeline != null)
                {
                    var sE = segmentTimeline.SelectNodes("ns:S", nsMgr);
                    if (sE?.Count > 0)
                    {
                        MultisegmentInfo["TotalNumber"] = 0L;
                        var SList = new JsonArray();
                        foreach (XmlElement s in sE)
                        {
                            var r = string.IsNullOrEmpty(s.GetAttribute("r")) ? 0 : Convert.ToInt64(s.GetAttribute("r"));
                            MultisegmentInfo["TotalNumber"] = MultisegmentInfo["TotalNumber"]?.GetValue<long>() + 1 + r;
                            SList.Add(new JsonObject()
                            {
                                ["t"] = string.IsNullOrEmpty(s.GetAttribute("t")) ? 0 : Convert.ToInt64(s.GetAttribute("t")),
                                ["d"] = Convert.ToInt64(s.GetAttribute("d")),
                                ["r"] = r
                            });
                        }
                        MultisegmentInfo.Add("S", SList);
                    }
                }
                var startNumber = sourceE.GetAttribute("startNumber");
                if (!string.IsNullOrEmpty(startNumber))
                {
                    MultisegmentInfo["StartNumber"] = Convert.ToInt32(startNumber);
                }
                var timescale = sourceE.GetAttribute("timescale");
                if (!string.IsNullOrEmpty(timescale))
                {
                    MultisegmentInfo["Timescale"] = Convert.ToInt32(timescale);
                }
                var segmentDuration = sourceE.GetAttribute("duration");
                if (!string.IsNullOrEmpty(segmentDuration))
                {
                    MultisegmentInfo["SegmentDuration"] = Convert.ToDouble(segmentDuration);
                }
            }

            void ExtractInitialization(XmlNode source)
            {
                var initialization = source.SelectSingleNode("ns:Initialization", nsMgr);
                if (initialization != null)
                {
                    MultisegmentInfo["InitializationUrl"] = ((XmlElement)initialization).GetAttribute("sourceURL");
                    if (((XmlElement)initialization).HasAttribute("range"))
                    {
                        MultisegmentInfo["InitializationUrl"] += "$$Range=" + ((XmlElement)initialization).GetAttribute("range");
                    }
                }
            }

            var segmentList = period.SelectSingleNode("ns:SegmentList", nsMgr);
            if (segmentList != null)
            {
                ExtractCommon(segmentList);
                ExtractInitialization(segmentList);
                var segmentUrlsE = segmentList.SelectNodes("ns:SegmentURL", nsMgr);
                var urls = new JsonArray();
                foreach (XmlElement segment in segmentUrlsE)
                {
                    if (segment.HasAttribute("mediaRange"))
                    {
                        urls.Add("$$Range=" + segment.GetAttribute("mediaRange"));
                    }
                    else
                    {
                        urls.Add(segment.GetAttribute("media"));
                    }
                }
                MultisegmentInfo["SegmentUrls"] = urls;
            }
            else
            {
                var segmentTemplate = period.SelectSingleNode("ns:SegmentTemplate", nsMgr);
                if (segmentTemplate != null)
                {
                    ExtractCommon(segmentTemplate);
                    var media = ((XmlElement)segmentTemplate).GetAttribute("media");
                    if (!string.IsNullOrEmpty(media))
                    {
                        MultisegmentInfo["Media"] = media;
                    }
                    var initialization = ((XmlElement)segmentTemplate).GetAttribute("initialization");
                    if (!string.IsNullOrEmpty(initialization))
                    {
                        MultisegmentInfo["Initialization"] = initialization;
                    }
                    else
                    {
                        ExtractInitialization(segmentTemplate);
                    }
                }
            }

            return MultisegmentInfo;
        }

        /// <summary>
        /// 预处理URL
        /// </summary>
        private string PreProcessUrl(string url)
        {
            foreach (var p in ParserConfig.DASHUrlProcessors)
            {
                if (p.CanProcess(url, ParserConfig))
                {
                    url = p.Process(url, ParserConfig);
                }
            }

            return url;
        }

        private void PreProcessContent()
        {
            foreach (var p in ParserConfig.DASHContentProcessors)
            {
                if (p.CanProcess(MpdContent, ParserConfig))
                {
                    MpdContent = p.Process(MpdContent, ParserConfig);
                }
            }
        }

        public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
        {
            //MPD不需要重新去读取内容，只判断一下最后的分片是否有效即可
            foreach (var item in streamSpecs)
            {
                //检测最后一片的有效性
                if (item.Playlist.MediaParts[0].MediaSegments.Count > 1)
                {
                    var last = item.Playlist.MediaParts[0].MediaSegments.Last();
                    var secondToLast = item.Playlist.MediaParts[0].MediaSegments[item.Playlist.MediaParts[0].MediaSegments.Count - 2];
                    var urlLast = last.Url;
                    var urlSecondToLast = secondToLast.Url;
                    //普通分段才判断
                    if (urlLast.StartsWith("http") && !Regex.IsMatch(urlLast, "\\$\\$Range=(\\d+)-(\\d+)"))
                    {
                        Logger.Warn(ResString.checkingLast + $"({(item.MediaType == MediaType.AUDIO ? "Audio" : (item.MediaType == MediaType.SUBTITLES ? "Sub" : "Video"))})");
                        //倒数第二段正常，倒数第一段不正常
                        if (CheckValid(urlSecondToLast) && !CheckValid(urlLast))
                            item.Playlist.MediaParts[0].MediaSegments.RemoveAt(item.Playlist.MediaParts[0].MediaSegments.Count - 1);
                    }
                }
            }
        }
    }
}
