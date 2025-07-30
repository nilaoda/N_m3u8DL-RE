using System.Text.RegularExpressions;

using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.StreamParser.Constants;

namespace N_m3u8DL_RE.StreamParser.Processor.HLS
{
    public partial class DefaultHLSContentProcessor : ContentProcessor
    {
        [GeneratedRegex("#EXT-X-DISCONTINUITY\\s+#EXT-X-MAP:URI=\\\"(.*?)\\\",BYTERANGE=\\\"(.*?)\\\"")]
        private static partial Regex YkDVRegex();
        [GeneratedRegex("#EXT-X-MAP:URI=\\\".*?BUMPER/[\\s\\S]+?#EXT-X-DISCONTINUITY")]
        private static partial Regex DNSPRegex();
        [GeneratedRegex(@"#EXTINF:.*?,\s+.*BUMPER.*\s+?#EXT-X-DISCONTINUITY")]
        private static partial Regex DNSPSubRegex();
        [GeneratedRegex("(#EXTINF.*)(\\s+)(#EXT-X-KEY.*)")]
        private static partial Regex OrderFixRegex();
        [GeneratedRegex(@"#EXT-X-MAP.*\.apple\.com/")]
        private static partial Regex ATVRegex();
        [GeneratedRegex(@"(#EXT-X-KEY:[\s\S]*?)(#EXT-X-DISCONTINUITY|#EXT-X-ENDLIST)")]
        private static partial Regex ATVRegex2();

        public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig)
        {
            return extractorType == ExtractorType.HLS;
        }

        public override string Process(string rawText, ParserConfig parserConfig)
        {
            // 处理content以\r作为换行符的情况
            if (rawText.Contains('\r') && !rawText.Contains('\n'))
            {
                rawText = rawText.Replace("\r", Environment.NewLine);
            }

            string m3u8Url = parserConfig.Url;
            // YSP回放
            if (m3u8Url.Contains("tlivecloud-playback-cdn.ysp.cctv.cn") && m3u8Url.Contains("endtime="))
            {
                rawText += Environment.NewLine + HLSTags.ext_x_endlist;
            }

            // IMOOC
            if (m3u8Url.Contains("imooc.com/"))
            {
                // M3u8Content = DecodeImooc.DecodeM3u8(M3u8Content);
            }

            // 针对YK #EXT-X-VERSION:7杜比视界片源修正
            if (rawText.Contains("#EXT-X-DISCONTINUITY") && rawText.Contains("#EXT-X-MAP") && rawText.Contains("ott.cibntv.net") && rawText.Contains("ccode="))
            {
                Regex ykmap = YkDVRegex();
                foreach (Match m in ykmap.Matches(rawText))
                {
                    rawText = rawText.Replace(m.Value, $"#EXTINF:0.000000,\n#EXT-X-BYTERANGE:{m.Groups[2].Value}\n{m.Groups[1].Value}");
                }
            }

            // 针对Disney+修正
            if (rawText.Contains("#EXT-X-DISCONTINUITY") && rawText.Contains("#EXT-X-MAP") && m3u8Url.Contains("media.dssott.com/"))
            {
                Regex ykmap = DNSPRegex();
                if (ykmap.IsMatch(rawText))
                {
                    rawText = rawText.Replace(ykmap.Match(rawText).Value, "#XXX");
                }
            }

            // 针对Disney+字幕修正
            if (rawText.Contains("#EXT-X-DISCONTINUITY") && rawText.Contains("seg_00000.vtt") && m3u8Url.Contains("media.dssott.com/"))
            {
                Regex ykmap = DNSPSubRegex();
                if (ykmap.IsMatch(rawText))
                {
                    rawText = rawText.Replace(ykmap.Match(rawText).Value, "#XXX");
                }
            }

            // 针对AppleTv修正
            if (rawText.Contains("#EXT-X-DISCONTINUITY") && rawText.Contains("#EXT-X-MAP") && (m3u8Url.Contains(".apple.com/") || ATVRegex().IsMatch(rawText)))
            {
                // 只取加密部分即可
                Regex ykmap = ATVRegex2();
                if (ykmap.IsMatch(rawText))
                {
                    rawText = "#EXTM3U\r\n" + ykmap.Match(rawText).Groups[1].Value + "\r\n#EXT-X-ENDLIST";
                }
            }

            // 修复#EXT-X-KEY与#EXTINF出现次序异常问题
            Regex regex = OrderFixRegex();
            if (regex.IsMatch(rawText))
            {
                rawText = regex.Replace(rawText, "$3$2$1");
            }

            return rawText;
        }
    }
}