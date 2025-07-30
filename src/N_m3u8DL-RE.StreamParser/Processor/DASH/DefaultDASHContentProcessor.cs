using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.StreamParser.Config;

namespace N_m3u8DL_RE.StreamParser.Processor.DASH
{
    /// <summary>
    /// XG视频处理
    /// </summary>
    public class DefaultDASHContentProcessor : ContentProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string rawText, ParserConfig parserConfig)
        {
            return extractorType == ExtractorType.MPEGDASH && rawText.Contains("<mas:") && !rawText.Contains("xmlns:mas");
        }

        public override string Process(string rawText, ParserConfig parserConfig)
        {
            Logger.Debug("Fix xigua mpd...");
            rawText = rawText.Replace("<MPD ", "<MPD xmlns:mas=\"urn:marlin:mas:1-0:services:schemas:mpd\" ");

            return rawText;
        }
    }
}