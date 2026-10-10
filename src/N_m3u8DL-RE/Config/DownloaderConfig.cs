using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Config;

internal class DownloaderConfig
{
    public required MyOption MyOptions { get; set; }

    /// <summary>
    /// 前置阶段生成的文件夹名
    /// </summary>
    public required string DirPrefix { get; set; }
    /// <summary>
    /// 前置阶段本次写出的清单和元数据，直播收尾时只清理这些文件
    /// </summary>
    public List<string> CreatedMetadataFiles { get; set; } = [];
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