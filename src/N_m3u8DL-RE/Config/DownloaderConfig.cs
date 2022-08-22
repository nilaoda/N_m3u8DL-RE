using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Parser.Config;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Config
{
    internal class DownloaderConfig
    {
        public DownloaderConfig() { }

        public DownloaderConfig(MyOption option)
        {
            AutoSubtitleFix = option.AutoSubtitleFix;
            SkipMerge = option.SkipMerge;
            BinaryMerge = option.BinaryMerge;
            DelAfterDone = option.DelAfterDone;
            CheckSegmentsCount = option.CheckSegmentsCount;
            SubtitleFormat = option.SubtitleFormat;
            TmpDir = option.TmpDir;
            SaveName = option.SaveName;
            SaveDir = option.SaveDir;
            ThreadCount = option.ThreadCount;
            SavePattern = option.SavePattern;
            Keys = option.Keys;
            MP4RealTimeDecryption = option.MP4RealTimeDecryption;
            UseShakaPackager = option.UseShakaPackager;
            DecryptionBinaryPath = option.DecryptionBinaryPath;
            FFmpegBinaryPath = option.FFmpegBinaryPath;
            KeyTextFile = option.KeyTextFile;
            DownloadRetryCount = option.DownloadRetryCount;
            MuxAfterDone = option.MuxAfterDone;
            UseMkvmerge = option.UseMkvmerge;
            MkvmergeBinaryPath = option.MkvmergeBinaryPath;
            MuxToMp4 = option.MuxToMp4;
        }

        /// <summary>
        /// 临时文件存储目录
        /// </summary>
        public string? TmpDir { get; set; }
        /// <summary>
        /// 文件存储目录
        /// </summary>
        public string? SaveDir { get; set; }
        /// <summary>
        /// 文件名
        /// </summary>
        public string? SaveName { get; set; }
        /// <summary>
        /// 文件名模板
        /// </summary>
        public string? SavePattern { get; set; }
        /// <summary>
        /// 线程数
        /// </summary>
        public int ThreadCount { get; set; } = 8;
        /// <summary>
        /// 每个分片的重试次数
        /// </summary>
        public int DownloadRetryCount { get; set; } = 3;
        /// <summary>
        /// 跳过合并
        /// </summary>
        public bool SkipMerge { get; set; } = false;
        /// <summary>
        /// 二进制合并
        /// </summary>
        public bool BinaryMerge { get; set; } = false;
        /// <summary>
        /// 完成后是否删除临时文件
        /// </summary>
        public bool DelAfterDone { get; set; } = false;
        /// <summary>
        /// 校验有没有下完全部分片
        /// </summary>
        public bool CheckSegmentsCount { get; set; } = true;
        /// <summary>
        /// 校验响应头的文件大小和实际大小
        /// </summary>
        public bool CheckContentLength { get; set; } = true;
        /// <summary>
        /// 自动修复字幕
        /// </summary>
        public bool AutoSubtitleFix { get; set; } = true;
        /// <summary>
        /// MP4实时解密
        /// </summary>
        public bool MP4RealTimeDecryption { get; set; } = true;
        /// <summary>
        /// 使用shaka-packager替代mp4decrypt
        /// </summary>
        public bool UseShakaPackager { get; set; }
        /// <summary>
        /// 自动混流音视频
        /// </summary>
        public bool MuxAfterDone { get; set; }
        /// <summary>
        /// 自动混流音视频容器使用mp4
        /// </summary>
        public bool MuxToMp4 { get; set; }
        /// <summary>
        /// 使用mkvmerge混流
        /// </summary>
        public bool UseMkvmerge { get; set; }
        /// <summary>
        /// MP4解密所用工具的全路径
        /// </summary>
        public string? DecryptionBinaryPath { get; set; }
        /// <summary>
        /// 字幕格式
        /// </summary>
        public SubtitleFormat SubtitleFormat { get; set; } = SubtitleFormat.VTT;
        /// <summary>
        /// 请求头
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>()
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
        };
        /// <summary>
        /// 解密KEYs
        /// </summary>
        public string[]? Keys { get; set; }
        /// <summary>
        /// KID-KEY文件
        /// </summary>
        public string? KeyTextFile { get; set; }
        /// <summary>
        /// ffmpeg路径
        /// </summary>
        public string? FFmpegBinaryPath { get; set; }
        /// <summary>
        /// mkvmerge路径
        /// </summary>
        public string? MkvmergeBinaryPath { get; set; }
    }
}
