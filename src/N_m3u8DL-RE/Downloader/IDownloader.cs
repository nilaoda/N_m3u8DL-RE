using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Downloader;

internal interface IDownloader
{
    Task<DownloadResult?> DownloadSegmentAsync(MediaSegment segment, string savePath, SpeedContainer speedContainer, Dictionary<string, string>? headers = null);
}