﻿namespace N_m3u8DL_RE.Entity
{
    internal sealed class DownloadResult
    {
        public bool Success => (ActualContentLength != null && RespContentLength != null) ? (RespContentLength == ActualContentLength) : (ActualContentLength != null);
        public long? RespContentLength { get; set; }
        public long? ActualContentLength { get; set; }
        public bool ImageHeader { get; set; }  // 图片伪装
        public bool GzipHeader { get; set; }  // GZip压缩
        public required string ActualFilePath { get; set; }
    }
}