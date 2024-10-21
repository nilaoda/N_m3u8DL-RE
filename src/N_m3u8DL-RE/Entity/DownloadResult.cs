namespace N_m3u8DL_RE.Entity;

internal class DownloadResult
{
    public bool Success { get => (ActualContentLength != null && RespContentLength != null) ? (RespContentLength == ActualContentLength) : (ActualContentLength == null ? false : true); }
    public long? RespContentLength { get; set; }
    public long? ActualContentLength { get; set; }
    public bool ImageHeader { get; set; } = false; //图片伪装
    public bool GzipHeader { get; set; } = false; //GZip压缩
    public required string ActualFilePath { get; set; }
}