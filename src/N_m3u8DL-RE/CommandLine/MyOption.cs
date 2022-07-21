using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Enum;

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
        public string[]? Headers { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.Keys"/>.
        /// </summary>
        public string[]? Keys { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.LogLevel"/>.
        /// </summary>
        public LogLevel LogLevel { get; set; }
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
        /// See: <see cref="CommandInvoker.SkipMerge"/>.
        /// </summary>
        public bool SkipMerge { get; set; }
        /// <summary>
        /// See: <see cref="CommandInvoker.BinaryMerge"/>.
        /// </summary>
        public bool BinaryMerge { get; set; }
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
    }
}