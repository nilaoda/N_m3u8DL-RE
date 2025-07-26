using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Parser.Mp4;

namespace Mp4SubtitleParser
{
    internal class SubEntity
    {
        public required string Begin { get; set; }
        public required string End { get; set; }
        public required string Region { get; set; }
        public List<XmlElement> Contents { get; set; } = [];
        public List<string> ContentStrings { get; set; } = [];

        public override bool Equals(object? obj)
        {
            return obj is SubEntity entity &&
                   Begin == entity.Begin &&
                   End == entity.End &&
                   Region == entity.Region &&
                   ContentStrings.SequenceEqual(entity.ContentStrings);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Begin, End, Region, ContentStrings);
        }
    }

    public static partial class MP4TtmlUtil
    {
        [GeneratedRegex(" \\w+:\\w+=\\\"[^\\\"]*\\\"")]
        private static partial Regex AttrRegex();
        [GeneratedRegex("<p.*?>((.|\n)+?)<\\/p>")]
        private static partial Regex LabelFixRegex();
        [GeneratedRegex(@"\<tt[\s\S]*?\<\/tt\>")]
        private static partial Regex MultiElementsFixRegex();
        [GeneratedRegex("\\<smpte:image.*xml:id=\\\"(.*?)\\\".*\\>([\\s\\S]*?)<\\/smpte:image>")]
        private static partial Regex ImageRegex();

        public static bool CheckInit(byte[] data)
        {
            bool sawSTPP = false;

            // parse init
            new MP4Parser()
                .Box("moov", MP4Parser.Children)
                .Box("trak", MP4Parser.Children)
                .Box("mdia", MP4Parser.Children)
                .Box("minf", MP4Parser.Children)
                .Box("stbl", MP4Parser.Children)
                .FullBox("stsd", MP4Parser.SampleDescription)
                .Box("stpp", box =>
                {
                    sawSTPP = true;
                })
                .Parse(data);

            return sawSTPP;
        }

        private static string ShiftTime(string xmlSrc, long segTimeMs, int index)
        {
            string Add(string xmlTime)
            {
                DateTime dt = DateTime.ParseExact(xmlTime, "HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
                TimeSpan ts = TimeSpan.FromMilliseconds(dt.TimeOfDay.TotalMilliseconds + (segTimeMs * index));
                return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}.{ts.Milliseconds:000}";
            }

            if (!xmlSrc.Contains("<tt") || !xmlSrc.Contains("<head>"))
            {
                return xmlSrc;
            }

            XmlDocument xmlDoc = new();
            XmlNamespaceManager? nsMgr = null;
            xmlDoc.LoadXml(xmlSrc);
            XmlNode? ttNode = xmlDoc.LastChild;
            if (nsMgr == null)
            {
                string ns = ((XmlElement)ttNode!).GetAttribute("xmlns");
                nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
                nsMgr.AddNamespace("ns", ns);
            }

            XmlNode? bodyNode = ttNode!.SelectSingleNode("ns:body", nsMgr);
            if (bodyNode == null)
            {
                return xmlSrc;
            }

            XmlNode? _div = bodyNode.SelectSingleNode("ns:div", nsMgr);
            // Parse <p> label
            foreach (XmlElement _p in _div!.SelectNodes("ns:p", nsMgr)!)
            {
                string _begin = _p.GetAttribute("begin");
                string _end = _p.GetAttribute("end");
                // Handle namespace
                foreach (XmlAttribute attr in _p.Attributes)
                {
                    if (attr.LocalName == "begin")
                    {
                        _begin = attr.Value;
                    }
                    else if (attr.LocalName == "end")
                    {
                        _end = attr.Value;
                    }
                }
                _p.SetAttribute("begin", Add(_begin));
                _p.SetAttribute("end", Add(_end));
                // Console.WriteLine($"{_begin} {_p.GetAttribute("begin")}");
                // Console.WriteLine($"{_end} {_p.GetAttribute("begin")}");
            }

            return xmlDoc.OuterXml;
        }

        private static string GetTextFromElement(XmlElement node)
        {
            StringBuilder sb = new();
            foreach (XmlNode item in node.ChildNodes)
            {
                if (item.NodeType == XmlNodeType.Text)
                {
                    sb.Append(item.InnerText.Trim());
                }
                else if (item is { NodeType: XmlNodeType.Element, Name: "br" })
                {
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }

        private static List<string> SplitMultipleRootElements(string xml)
        {
            return !MultiElementsFixRegex().IsMatch(xml) ? [] : [.. MultiElementsFixRegex().Matches(xml).Select(m => m.Value)];
        }

        public static WebVttSub ExtractFromMp4(string item, long segTimeMs, long baseTimestamp = 0L)
        {
            return ExtractFromMp4s([item], segTimeMs, baseTimestamp);
        }

        private static WebVttSub ExtractFromMp4s(IEnumerable<string> items, long segTimeMs, long baseTimestamp = 0L)
        {
            // read ttmls
            List<string> xmls = [];
            int segIndex = 0;
            foreach (string item in items)
            {
                byte[] dataSeg = File.ReadAllBytes(item);

                bool sawMDAT = false;
                // parse media
                new MP4Parser()
                    .Box("mdat", MP4Parser.AllData(data =>
                    {
                        sawMDAT = true;
                        // Join this to any previous payload, in case the mp4 has multiple
                        // mdats.
                        if (segTimeMs != 0)
                        {
                            List<string> datas = SplitMultipleRootElements(Encoding.UTF8.GetString(data));
                            foreach (string item in datas)
                            {
                                xmls.Add(ShiftTime(item, segTimeMs, segIndex));
                            }
                        }
                        else
                        {
                            List<string> datas = SplitMultipleRootElements(Encoding.UTF8.GetString(data));
                            xmls.AddRange(datas);
                        }
                    }))
                    .Parse(dataSeg,/* partialOkay= */ false);
                segIndex++;
            }

            return ExtractSub(xmls, baseTimestamp);
        }

        public static WebVttSub ExtractFromTTML(string item, long segTimeMs, long baseTimestamp = 0L)
        {
            return ExtractFromTTMLs([item], segTimeMs, baseTimestamp);
        }

        public static WebVttSub ExtractFromTTMLs(IEnumerable<string> items, long segTimeMs, long baseTimestamp = 0L)
        {
            // read ttmls
            List<string> xmls = [];
            int segIndex = 0;
            foreach (string item in items)
            {
                string xml = File.ReadAllText(item);
                xmls.Add(segTimeMs != 0 ? ShiftTime(xml, segTimeMs, segIndex) : xml);
                segIndex++;
            }

            return ExtractSub(xmls, baseTimestamp);
        }

        private static WebVttSub ExtractSub(List<string> xmls, long baseTimestamp)
        {
            // parsing
            XmlDocument xmlDoc = new();
            List<SubEntity> finalSubs = [];
            XmlNode? headNode = null;
            XmlNamespaceManager? nsMgr = null;
            Regex regex = LabelFixRegex();
            Regex attrRegex = AttrRegex();
            foreach (string item in xmls)
            {
                string xmlContent = item;
                if (!xmlContent.Contains("<tt"))
                {
                    continue;
                }

                // fix non-standard xml 
                string xmlContentFix = xmlContent;
                if (regex.IsMatch(xmlContent))
                {
                    foreach (Match m in regex.Matches(xmlContentFix))
                    {
                        try
                        {
                            string inner = m.Groups[1].Value;
                            if (attrRegex.IsMatch(inner))
                            {
                                inner = attrRegex.Replace(inner, "");
                            }
                            new XmlDocument().LoadXml($"<p>{inner}</p>");
                        }
                        catch (Exception)
                        {
                            xmlContentFix = xmlContentFix.Replace(m.Groups[1].Value, System.Web.HttpUtility.HtmlEncode(m.Groups[1].Value));
                        }
                    }
                }
                xmlDoc.LoadXml(xmlContentFix);
                XmlNode? ttNode = xmlDoc.LastChild;
                if (nsMgr == null)
                {
                    string ns = ((XmlElement)ttNode!).GetAttribute("xmlns");
                    nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
                    nsMgr.AddNamespace("ns", ns);
                }
                headNode ??= ttNode!.SelectSingleNode("ns:head", nsMgr);

                XmlNode? bodyNode = ttNode!.SelectSingleNode("ns:body", nsMgr);
                if (bodyNode == null)
                {
                    continue;
                }

                XmlNode? _div = bodyNode.SelectSingleNode("ns:div", nsMgr);
                if (_div == null)
                {
                    continue;
                }


                // PNG Subs
                Dictionary<string, string> imageDic = []; // id, Base64
                if (ImageRegex().IsMatch(xmlDoc.InnerXml))
                {
                    foreach (Match img in ImageRegex().Matches(xmlDoc.InnerXml))
                    {
                        imageDic.Add(img.Groups[1].Value.Trim(), img.Groups[2].Value.Trim());
                    }
                }

                // convert <div> to <p>
                if (_div!.SelectNodes("ns:p", nsMgr) == null || _div!.SelectNodes("ns:p", nsMgr)!.Count == 0)
                {
                    foreach (XmlElement _tDiv in bodyNode.SelectNodes("ns:div", nsMgr)!)
                    {
                        XmlDocumentFragment _p = xmlDoc.CreateDocumentFragment();
                        _p.InnerXml = _tDiv.OuterXml.Replace("<div ", "<p ").Replace("</div>", "</p>");
                        _div.AppendChild(_p);
                    }
                }

                // Parse <p> label
                foreach (XmlElement _p in _div!.SelectNodes("ns:p", nsMgr)!)
                {
                    string _begin = _p.GetAttribute("begin");
                    string _end = _p.GetAttribute("end");
                    string _region = _p.GetAttribute("region");
                    string _bgImg = _p.GetAttribute("smpte:backgroundImage");
                    // Handle namespace
                    foreach (XmlAttribute attr in _p.Attributes)
                    {
                        if (attr.LocalName == "begin")
                        {
                            _begin = attr.Value;
                        }
                        else if (attr.LocalName == "end")
                        {
                            _end = attr.Value;
                        }
                        else if (attr.LocalName == "region")
                        {
                            _region = attr.Value;
                        }
                    }
                    SubEntity sub = new()
                    {
                        Begin = _begin,
                        End = _end,
                        Region = _region
                    };

                    if (string.IsNullOrEmpty(_bgImg))
                    {
                        XmlNodeList _spans = _p.ChildNodes;
                        // Collect <span>
                        foreach (XmlNode _node in _spans)
                        {
                            if (_node.NodeType == XmlNodeType.Element)
                            {
                                XmlElement _span = (XmlElement)_node;
                                if (string.IsNullOrEmpty(_span.InnerText))
                                {
                                    continue;
                                }

                                sub.Contents.Add(_span);
                                sub.ContentStrings.Add(_span.OuterXml);
                            }
                            else if (_node.NodeType == XmlNodeType.Text)
                            {
                                XmlElement _span = new XmlDocument().CreateElement("span");
                                _span.InnerText = _node.Value!;
                                sub.Contents.Add(_span);
                                sub.ContentStrings.Add(_span.OuterXml);
                            }
                        }
                    }
                    else
                    {
                        string id = _bgImg.Replace("#", "");
                        if (imageDic.TryGetValue(id, out string? value))
                        {
                            XmlElement _span = new XmlDocument().CreateElement("span");
                            _span.InnerText = $"Base64::{value}";
                            sub.Contents.Add(_span);
                            sub.ContentStrings.Add(_span.OuterXml);
                        }
                    }

                    // Check if one <p> has been splitted
                    int index = finalSubs.FindLastIndex(s => s.End == _begin && s.Region == _region && s.ContentStrings.SequenceEqual(sub.ContentStrings));
                    // Skip empty lines
                    if (sub.ContentStrings.Count <= 0)
                    {
                        continue;
                    }
                    // Extend <p> duration
                    if (index != -1)
                    {
                        finalSubs[index].End = sub.End;
                    }
                    else if (!finalSubs.Contains(sub))
                    {
                        finalSubs.Add(sub);
                    }
                }
            }


            Dictionary<string, string> dic = [];
            foreach (SubEntity sub in finalSubs)
            {
                string key = $"{sub.Begin} --> {sub.End}";
                foreach (XmlElement item in sub.Contents)
                {
                    if (dic.ContainsKey(key))
                    {
                        dic[key] = item.GetAttribute("tts:fontStyle") is "italic" or "oblique"
                            ? $"{dic[key]}\r\n<i>{GetTextFromElement(item)}</i>"
                            : $"{dic[key]}\r\n{GetTextFromElement(item)}";
                    }
                    else
                    {
                        if (item.GetAttribute("tts:fontStyle") is "italic" or "oblique")
                        {
                            dic.Add(key, $"<i>{GetTextFromElement(item)}</i>");
                        }
                        else
                        {
                            dic.Add(key, GetTextFromElement(item));
                        }
                    }
                }
            }


            StringBuilder vtt = new();
            vtt.AppendLine("WEBVTT");
            foreach (KeyValuePair<string, string> item in dic)
            {
                vtt.AppendLine(item.Key);
                vtt.AppendLine(item.Value);
                vtt.AppendLine();
            }

            return WebVttSub.Parse(vtt.ToString(), baseTimestamp);
        }
    }
}