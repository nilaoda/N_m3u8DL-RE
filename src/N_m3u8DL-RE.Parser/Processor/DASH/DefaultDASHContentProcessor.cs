using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor.DASH
{
    /// <summary>
    /// 西瓜视频处理
    /// </summary>
    public class DefaultDASHContentProcessor : ContentProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string mpdContent, ParserConfig parserConfig)
        {
            if (extractorType != ExtractorType.MPEG_DASH) return false;

            if (mpdContent.Contains("<mas:") && !mpdContent.Contains("xmlns:mas"))
            {
                return true;
            }
            return false;
        }

        public override string Process(string mpdContent, ParserConfig parserConfig)
        {
            Logger.Debug("Fix xigua mpd...");
            mpdContent = mpdContent.Replace("<MPD ", "<MPD xmlns:mas=\"urn:marlin:mas:1-0:services:schemas:mpd\" ");

            return mpdContent;
        }
    }
}
