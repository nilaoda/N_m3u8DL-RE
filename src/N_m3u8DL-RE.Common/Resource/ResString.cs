using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Resource
{
    public class ResString
    {
        public readonly static string ReLiveTs = "<RE_LIVE_TS>";
        public static string singleFileRealtimeDecryptWarn { get => GetText("singleFileRealtimeDecryptWarn"); }
        public static string singleFileSplitWarn { get => GetText("singleFileSplitWarn"); }
        public static string customRangeWarn { get => GetText("customRangeWarn"); }
        public static string customRangeFound { get => GetText("customRangeFound"); }
        public static string customAdKeywordsFound { get => GetText("customAdKeywordsFound"); }
        public static string customRangeInvalid { get => GetText("customRangeInvalid"); }
        public static string consoleRedirected { get => GetText("consoleRedirected"); }
        public static string autoBinaryMerge { get => GetText("autoBinaryMerge"); }
        public static string autoBinaryMerge2 { get => GetText("autoBinaryMerge2"); }
        public static string autoBinaryMerge3 { get => GetText("autoBinaryMerge3"); }
        public static string autoBinaryMerge4 { get => GetText("autoBinaryMerge4"); }
        public static string autoBinaryMerge5 { get => GetText("autoBinaryMerge5"); }
        public static string autoBinaryMerge6 { get => GetText("autoBinaryMerge6"); }
        public static string badM3u8 { get => GetText("badM3u8"); }
        public static string binaryMerge { get => GetText("binaryMerge"); }
        public static string checkingLast { get => GetText("checkingLast"); }
        public static string cmd_appendUrlParams { get => GetText("cmd_appendUrlParams"); }
        public static string cmd_autoSelect { get => GetText("cmd_autoSelect"); }
        public static string cmd_binaryMerge { get => GetText("cmd_binaryMerge"); }
        public static string cmd_useFFmpegConcatDemuxer { get => GetText("cmd_useFFmpegConcatDemuxer"); }
        public static string cmd_checkSegmentsCount { get => GetText("cmd_checkSegmentsCount"); }
        public static string cmd_decryptionBinaryPath { get => GetText("cmd_decryptionBinaryPath"); }
        public static string cmd_delAfterDone { get => GetText("cmd_delAfterDone"); }
        public static string cmd_ffmpegBinaryPath { get => GetText("cmd_ffmpegBinaryPath"); }
        public static string cmd_mkvmergeBinaryPath { get => GetText("cmd_mkvmergeBinaryPath"); }
        public static string cmd_baseUrl { get => GetText("cmd_baseUrl"); }
        public static string cmd_maxSpeed { get => GetText("cmd_maxSpeed"); }
        public static string cmd_adKeyword { get => GetText("cmd_adKeyword"); }
        public static string cmd_moreHelp { get => GetText("cmd_moreHelp"); }
        public static string cmd_header { get => GetText("cmd_header"); }
        public static string cmd_muxImport { get => GetText("cmd_muxImport"); }
        public static string cmd_muxImport_more { get => GetText("cmd_muxImport_more"); }
        public static string cmd_selectVideo { get => GetText("cmd_selectVideo"); }
        public static string cmd_dropVideo { get => GetText("cmd_dropVideo"); }
        public static string cmd_selectVideo_more { get => GetText("cmd_selectVideo_more"); }
        public static string cmd_selectAudio { get => GetText("cmd_selectAudio"); }
        public static string cmd_dropAudio { get => GetText("cmd_dropAudio"); }
        public static string cmd_selectAudio_more { get => GetText("cmd_selectAudio_more"); }
        public static string cmd_selectSubtitle { get => GetText("cmd_selectSubtitle"); }
        public static string cmd_dropSubtitle { get => GetText("cmd_dropSubtitle"); }
        public static string cmd_selectSubtitle_more { get => GetText("cmd_selectSubtitle_more"); }
        public static string cmd_custom_range { get => GetText("cmd_custom_range"); }
        public static string cmd_customHLSMethod { get => GetText("cmd_customHLSMethod"); }
        public static string cmd_customHLSKey { get => GetText("cmd_customHLSKey"); }
        public static string cmd_customHLSIv { get => GetText("cmd_customHLSIv"); }
        public static string cmd_Input { get => GetText("cmd_Input"); }
        public static string cmd_forceAnsiConsole { get => GetText("cmd_forceAnsiConsole"); }
        public static string cmd_noAnsiColor { get => GetText("cmd_noAnsiColor"); }
        public static string cmd_keys { get => GetText("cmd_keys"); }
        public static string cmd_keyText { get => GetText("cmd_keyText"); }
        public static string cmd_loadKeyFailed { get => GetText("cmd_loadKeyFailed"); }
        public static string cmd_logLevel { get => GetText("cmd_logLevel"); }
        public static string cmd_MP4RealTimeDecryption { get => GetText("cmd_MP4RealTimeDecryption"); }
        public static string cmd_saveDir { get => GetText("cmd_saveDir"); }
        public static string cmd_saveName { get => GetText("cmd_saveName"); }
        public static string cmd_savePattern { get => GetText("cmd_savePattern"); }
        public static string cmd_skipDownload { get => GetText("cmd_skipDownload"); }
        public static string cmd_noDateInfo { get => GetText("cmd_noDateInfo"); }
        public static string cmd_noLog { get => GetText("cmd_noLog"); }
        public static string cmd_skipMerge { get => GetText("cmd_skipMerge"); }
        public static string cmd_subFormat { get => GetText("cmd_subFormat"); }
        public static string cmd_subOnly { get => GetText("cmd_subOnly"); }
        public static string cmd_subtitleFix { get => GetText("cmd_subtitleFix"); }
        public static string cmd_threadCount { get => GetText("cmd_threadCount"); }
        public static string cmd_downloadRetryCount { get => GetText("cmd_downloadRetryCount"); }
        public static string cmd_tmpDir { get => GetText("cmd_tmpDir"); }
        public static string cmd_uiLanguage { get => GetText("cmd_uiLanguage"); }
        public static string cmd_urlProcessorArgs { get => GetText("cmd_urlProcessorArgs"); }
        public static string cmd_useShakaPackager { get => GetText("cmd_useShakaPackager"); }
        public static string cmd_concurrentDownload { get => GetText("cmd_concurrentDownload"); }
        public static string cmd_useSystemProxy { get => GetText("cmd_useSystemProxy"); }
        public static string cmd_customProxy { get => GetText("cmd_customProxy"); }
        public static string cmd_customRange { get => GetText("cmd_customRange"); }
        public static string cmd_liveKeepSegments { get => GetText("cmd_liveKeepSegments"); }
        public static string cmd_livePipeMux { get => GetText("cmd_livePipeMux"); }
        public static string cmd_liveRecordLimit { get => GetText("cmd_liveRecordLimit"); }
        public static string cmd_taskStartAt { get => GetText("cmd_taskStartAt"); }
        public static string cmd_liveWaitTime { get => GetText("cmd_liveWaitTime"); }
        public static string cmd_liveTakeCount { get => GetText("cmd_liveTakeCount"); }
        public static string cmd_liveFixVttByAudio { get => GetText("cmd_liveFixVttByAudio"); }
        public static string cmd_liveRealTimeMerge { get => GetText("cmd_liveRealTimeMerge"); }
        public static string cmd_livePerformAsVod { get => GetText("cmd_livePerformAsVod"); }
        public static string cmd_muxAfterDone { get => GetText("cmd_muxAfterDone"); }
        public static string cmd_muxAfterDone_more { get => GetText("cmd_muxAfterDone_more"); }
        public static string cmd_writeMetaJson { get => GetText("cmd_writeMetaJson"); }
        public static string liveLimit { get => GetText("liveLimit"); }
        public static string realTimeDecMessage { get => GetText("realTimeDecMessage"); }
        public static string liveLimitReached { get => GetText("liveLimitReached"); }
        public static string saveName { get => GetText("saveName"); }
        public static string taskStartAt { get => GetText("taskStartAt"); }
        public static string namedPipeCreated { get => GetText("namedPipeCreated"); }
        public static string namedPipeMux { get => GetText("namedPipeMux"); }
        public static string partMerge { get => GetText("partMerge"); }
        public static string fetch { get => GetText("fetch"); }
        public static string ffmpegMerge { get => GetText("ffmpegMerge"); }
        public static string ffmpegNotFound { get => GetText("ffmpegNotFound"); }
        public static string fixingTTML { get => GetText("fixingTTML"); }
        public static string fixingTTMLmp4 { get => GetText("fixingTTMLmp4"); }
        public static string fixingVTT { get => GetText("fixingVTT"); }
        public static string fixingVTTmp4 { get => GetText("fixingVTTmp4"); }
        public static string keyProcessorNotFound { get => GetText("keyProcessorNotFound"); }
        public static string liveFound { get => GetText("liveFound"); }
        public static string loadingUrl { get => GetText("loadingUrl"); }
        public static string masterM3u8Found { get => GetText("masterM3u8Found"); }
        public static string matchDASH { get => GetText("matchDASH"); }
        public static string matchMSS { get => GetText("matchMSS"); }
        public static string matchTS { get => GetText("matchTS"); }
        public static string matchHLS { get => GetText("matchHLS"); }
        public static string notSupported { get => GetText("notSupported"); }
        public static string parsingStream { get => GetText("parsingStream"); }
        public static string promptChoiceText { get => GetText("promptChoiceText"); }
        public static string promptInfo { get => GetText("promptInfo"); }
        public static string promptTitle { get => GetText("promptTitle"); }
        public static string readingInfo { get => GetText("readingInfo"); }
        public static string searchKey { get => GetText("searchKey"); }
        public static string segmentCountCheckNotPass { get => GetText("segmentCountCheckNotPass"); }
        public static string selectedStream { get => GetText("selectedStream"); }
        public static string startDownloading { get => GetText("startDownloading"); }
        public static string streamsInfo { get => GetText("streamsInfo"); }
        public static string writeJson { get => GetText("writeJson"); }
        public static string noStreamsToDownload { get => GetText("noStreamsToDownload"); }
        public static string newVersionFound { get => GetText("newVersionFound"); }
        public static string processImageSub { get => GetText("processImageSub"); }

        private static string GetText(string key)
        {
            if (!StaticText.LANG_DIC.ContainsKey(key))
                return "<...LANG TEXT MISSING...>";

            var current = Thread.CurrentThread.CurrentUICulture.Name;
            if (current == "zh-CN" || current == "zh-SG" || current == "zh-Hans")
                return StaticText.LANG_DIC[key].ZH_CN;
            else if (current.StartsWith("zh-"))
                return StaticText.LANG_DIC[key].ZH_TW;
            else
                return StaticText.LANG_DIC[key].EN_US;
        }
    }
}
