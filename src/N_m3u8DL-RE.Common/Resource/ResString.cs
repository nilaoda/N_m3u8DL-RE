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
        public static string autoBinaryMerge { get => GetText("autoBinaryMerge"); }
        public static string autoBinaryMerge2 { get => GetText("autoBinaryMerge2"); }
        public static string autoBinaryMerge3 { get => GetText("autoBinaryMerge3"); }
        public static string badM3u8 { get => GetText("badM3u8"); }
        public static string binaryMerge { get => GetText("binaryMerge"); }
        public static string checkingLast { get => GetText("checkingLast"); }
        public static string cmd_appendUrlParams { get => GetText("cmd_appendUrlParams"); }
        public static string cmd_autoSelect { get => GetText("cmd_autoSelect"); }
        public static string cmd_binaryMerge { get => GetText("cmd_binaryMerge"); }
        public static string cmd_checkSegmentsCount { get => GetText("cmd_checkSegmentsCount"); }
        public static string cmd_decryptionBinaryPath { get => GetText("cmd_decryptionBinaryPath"); }
        public static string cmd_delAfterDone { get => GetText("cmd_delAfterDone"); }
        public static string cmd_ffmpegBinaryPath { get => GetText("cmd_ffmpegBinaryPath"); }
        public static string cmd_header { get => GetText("cmd_header"); }
        public static string cmd_Input { get => GetText("cmd_Input"); }
        public static string cmd_keys { get => GetText("cmd_keys"); }
        public static string cmd_keyText { get => GetText("cmd_keyText"); }
        public static string cmd_loadKeyFailed { get => GetText("cmd_loadKeyFailed"); }
        public static string cmd_logLevel { get => GetText("cmd_logLevel"); }
        public static string cmd_MP4RealTimeDecryption { get => GetText("cmd_MP4RealTimeDecryption"); }
        public static string cmd_saveDir { get => GetText("cmd_saveDir"); }
        public static string cmd_saveName { get => GetText("cmd_saveName"); }
        public static string cmd_savePattern { get => GetText("cmd_savePattern"); }
        public static string cmd_skipDownload { get => GetText("cmd_skipDownload"); }
        public static string cmd_skipMerge { get => GetText("cmd_skipMerge"); }
        public static string cmd_subFormat { get => GetText("cmd_subFormat"); }
        public static string cmd_subOnly { get => GetText("cmd_subOnly"); }
        public static string cmd_subtitleFix { get => GetText("cmd_subtitleFix"); }
        public static string cmd_threadCount { get => GetText("cmd_threadCount"); }
        public static string cmd_tmpDir { get => GetText("cmd_tmpDir"); }
        public static string cmd_uiLanguage { get => GetText("cmd_uiLanguage"); }
        public static string cmd_urlProcessorArgs { get => GetText("cmd_urlProcessorArgs"); }
        public static string cmd_useShakaPackager { get => GetText("cmd_useShakaPackager"); }
        public static string cmd_writeMetaJson { get => GetText("cmd_writeMetaJson"); }
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
