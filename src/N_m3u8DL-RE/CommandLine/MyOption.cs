using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using System.Net;

namespace N_m3u8DL_RE.CommandLine
{
    internal class MyOption
    {
        /// <summary>
        /// See: <see cref="CommandInvoker.Input"/>.
        /// </summary>
        public string Input { get; set; } = default!;
        /// <summary>
        /// See: <see cref="CommandInvoker.Headers"/>.
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        /// <summary>
        /// See: <see cref="CommandInvoker.AdKeywords"/>.
        /// </summary>
        public string[]? AdKeywords { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.MaxSpeed"/>.
        /// </summary>
        public long? MaxSpeed { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.Keys"/>.
        /// </summary>
        public string[]? Keys { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.BaseUrl"/>.
        /// </summary>
        public string? BaseUrl { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.KeyTextFile"/>.
        /// </summary>
        public string? KeyTextFile { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.UrlProcessorArgs"/>.
        /// </summary>
        public string? UrlProcessorArgs { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LogLevel"/>.
        /// </summary>
        public LogLevel LogLevel { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.NoDateInfo"/>.
        /// </summary>
        public bool NoDateInfo { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.NoLog"/>.
        /// </summary>
        public bool NoLog { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.AutoSelect"/>.
        /// </summary>
        public bool AutoSelect { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SubOnly"/>.
        /// </summary>
        public bool SubOnly { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.ThreadCount"/>.
        /// </summary>
        public int ThreadCount { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DownloadRetryCount"/>.
        /// </summary>
        public int DownloadRetryCount { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveRecordLimit"/>.
        /// </summary>
        public TimeSpan? LiveRecordLimit { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.TaskStartAt"/>.
        /// </summary>
        public DateTime? TaskStartAt { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SkipMerge"/>.
        /// </summary>
        public bool SkipMerge { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.BinaryMerge"/>.
        /// </summary>
        public bool BinaryMerge { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.ForceAnsiConsole"/>.
        /// </summary>
        public bool ForceAnsiConsole { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.NoAnsiColor"/>.
        /// </summary>
        public bool NoAnsiColor { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.UseFFmpegConcatDemuxer"/>.
        /// </summary>
        public bool UseFFmpegConcatDemuxer { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DelAfterDone"/>.
        /// </summary>
        public bool DelAfterDone { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.AutoSubtitleFix"/>.
        /// </summary>
        public bool AutoSubtitleFix { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CheckSegmentsCount"/>.
        /// </summary>
        public bool CheckSegmentsCount { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SkipDownload"/>.
        /// </summary>
        public bool SkipDownload { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.WriteMetaJson"/>.
        /// </summary>
        public bool WriteMetaJson { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.AppendUrlParams"/>.
        /// </summary>
        public bool AppendUrlParams { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.MP4RealTimeDecryption"/>.
        /// </summary>
        public bool MP4RealTimeDecryption { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.UseShakaPackager"/>.
        /// </summary>
        public bool UseShakaPackager { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.MuxAfterDone"/>.
        /// </summary>
        public bool MuxAfterDone { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.ConcurrentDownload"/>.
        /// </summary>
        public bool ConcurrentDownload { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveRealTimeMerge"/>.
        /// </summary>
        public bool LiveRealTimeMerge { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveKeepSegments"/>.
        /// </summary>
        public bool LiveKeepSegments { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LivePerformAsVod"/>.
        /// </summary>
        public bool LivePerformAsVod { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.UseSystemProxy"/>.
        /// </summary>
        public bool UseSystemProxy { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SubtitleFormat"/>.
        /// </summary>
        public SubtitleFormat SubtitleFormat { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.TmpDir"/>.
        /// </summary>
        public string? TmpDir { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SaveDir"/>.
        /// </summary>
        public string? SaveDir { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SaveName"/>.
        /// </summary>
        public string? SaveName { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SavePattern"/>.
        /// </summary>
        public string? SavePattern { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.UILanguage"/>.
        /// </summary>
        public string? UILanguage { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DecryptionBinaryPath"/>.
        /// </summary>
        public string? DecryptionBinaryPath { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.FFmpegBinaryPath"/>.
        /// </summary>
        public string? FFmpegBinaryPath { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.MkvmergeBinaryPath"/>.
        /// </summary>
        public string? MkvmergeBinaryPath { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.MuxImports"/>.
        /// </summary>
        public List<OutputFile>? MuxImports { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.VideoFilter"/>.
        /// </summary>
        public StreamFilter? VideoFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DropVideoFilter"/>.
        /// </summary>
        public StreamFilter? DropVideoFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.AudioFilter"/>.
        /// </summary>
        public StreamFilter? AudioFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DropAudioFilter"/>.
        /// </summary>
        public StreamFilter? DropAudioFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.SubtitleFilter"/>.
        /// </summary>
        public StreamFilter? SubtitleFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.DropSubtitleFilter"/>.
        /// </summary>
        public StreamFilter? DropSubtitleFilter { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CustomHLSMethod"/>.
        /// </summary>
        public EncryptMethod? CustomHLSMethod { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CustomHLSKey"/>.
        /// </summary>
        public byte[]? CustomHLSKey { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CustomHLSIv"/>.
        /// </summary>
        public byte[]? CustomHLSIv { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CustomProxy"/>.
        /// </summary>
        public WebProxy? CustomProxy { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.CustomRange"/>.
        /// </summary>
        public CustomRange? CustomRange { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveWaitTime"/>.
        /// </summary>
        public int? LiveWaitTime { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveTakeCount"/>.
        /// </summary>
        public int LiveTakeCount { get; set; }
        public MuxOptions MuxOptions { get; set; }
        //public bool LiveWriteHLS { get; set; } = true;
        /// <summary>
        /// See: <see cref="CommandInvoker.LivePipeMux"/>.
        /// </summary>
        public bool LivePipeMux { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LiveFixVttByAudio"/>.
        /// </summary>
        public bool LiveFixVttByAudio { get; set; }
    }
}