using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor.HLS
{
    public partial class DefaultHLSContentProcessor : ContentProcessor
    {
        [GeneratedRegex("#EXT-X-DISCONTINUITY\\s+#EXT-X-MAP:URI=\\\"(.*?)\\\",BYTERANGE=\\\"(.*?)\\\"")]
        private static partial Regex YkDVRegex();
        [GeneratedRegex("#EXT-X-MAP:URI=\\\".*?BUMPER/[\\s\\S]+?#EXT-X-DISCONTINUITY")]
        private static partial Regex DNSPRegex();
        [GeneratedRegex("#EXTINF:.*?,\\s+.*BUMPER.*\\s+?#EXT-X-DISCONTINUITY")]
        private static partial Regex DNSPSubRegex();
        [GeneratedRegex("(#EXTINF.*)(\\s+)(#EXT-X-KEY.*)")]
        private static partial Regex OrderFixRegex();
        [GeneratedRegex("#EXT-X-MAP.*\\.apple\\.com/")]
        private static partial Regex ATVRegex();
        [GeneratedRegex("(#EXT-X-KEY:[\\s\\S]*?)(#EXT-X-DISCONTINUITY|#EXT-X-ENDLIST)")]
        private static partial Regex ATVRegex2();

        public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig) => extractorType == ExtractorType.HLS;

        public override string Process(string m3u8Content, ParserConfig parserConfig)
        {
            //处理content以\r作为换行符的情况
            if (m3u8Content.Contains("\r") && !m3u8Content.Contains("\n"))
            {
                m3u8Content = m3u8Content.Replace("\r", Environment.NewLine);
            }

            var m3u8Url = parserConfig.Url;
            //央视频回放
            if (m3u8Url.Contains("tlivecloud-playback-cdn.ysp.cctv.cn") && m3u8Url.Contains("endtime="))
            {
                m3u8Content += Environment.NewLine + HLSTags.ext_x_endlist;
            }

            //IMOOC
            if (m3u8Url.Contains("imooc.com/"))
            {
                //M3u8Content = DecodeImooc.DecodeM3u8(M3u8Content);
            }

            //iqy
            if (m3u8Content.StartsWith("{\"payload\""))
            {
                //
            }

            //针对优酷#EXT-X-VERSION:7杜比视界片源修正
            if (m3u8Content.Contains("#EXT-X-DISCONTINUITY") && m3u8Content.Contains("#EXT-X-MAP") && m3u8Content.Contains("ott.cibntv.net") && m3u8Content.Contains("ccode="))
            {
                Regex ykmap = YkDVRegex();
                foreach (Match m in ykmap.Matches(m3u8Content))
                {
                    m3u8Content = m3u8Content.Replace(m.Value, $"#EXTINF:0.000000,\n#EXT-X-BYTERANGE:{m.Groups[2].Value}\n{m.Groups[1].Value}");
                }
            }

            //针对Disney+修正
            if (m3u8Content.Contains("#EXT-X-DISCONTINUITY") && m3u8Content.Contains("#EXT-X-MAP") && m3u8Url.Contains("media.dssott.com/"))
            {
                Regex ykmap = DNSPRegex();
                if (ykmap.IsMatch(m3u8Content))
                {
                    m3u8Content = m3u8Content.Replace(ykmap.Match(m3u8Content).Value, "#XXX");
                }
            }

            //针对Disney+字幕修正
            if (m3u8Content.Contains("#EXT-X-DISCONTINUITY") && m3u8Content.Contains("seg_00000.vtt") && m3u8Url.Contains("media.dssott.com/"))
            {
                Regex ykmap = DNSPSubRegex();
                if (ykmap.IsMatch(m3u8Content))
                {
                    m3u8Content = m3u8Content.Replace(ykmap.Match(m3u8Content).Value, "#XXX");
                }
            }

            //针对AppleTv修正
            if (m3u8Content.Contains("#EXT-X-DISCONTINUITY") && m3u8Content.Contains("#EXT-X-MAP") && (m3u8Url.Contains(".apple.com/") || ATVRegex().IsMatch(m3u8Content)))
            {
                //只取加密部分即可
                Regex ykmap = ATVRegex2();
                if (ykmap.IsMatch(m3u8Content))
                {
                    m3u8Content = "#EXTM3U\r\n" + ykmap.Match(m3u8Content).Groups[1].Value + "\r\n#EXT-X-ENDLIST";
                }
            }

            //修复#EXT-X-KEY与#EXTINF出现次序异常问题
            var regex = OrderFixRegex();
            if (regex.IsMatch(m3u8Content))
            {
                m3u8Content = regex.Replace(m3u8Content, "$3$2$1");
            }

            return m3u8Content;
        }
    }
}
