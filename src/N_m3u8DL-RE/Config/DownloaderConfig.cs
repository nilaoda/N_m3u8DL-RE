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
        public required MyOption MyOptions { get; set; }

        /// <summary>
        /// 前置阶段生成的文件夹名
        /// </summary>
        public required string DirPrefix { get; set; }
        /// <summary>
        /// 文件名模板
        /// </summary>
        public string? SavePattern { get; set; }
        /// <summary>
        /// 校验响应头的文件大小和实际大小
        /// </summary>
        public bool CheckContentLength { get; set; } = true;
        /// <summary>
        /// 请求头
        /// </summary>
        public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
    }
}
