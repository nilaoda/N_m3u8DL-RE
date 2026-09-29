using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.DownloadManager;

/// <summary>
/// 计算直播列表的刷新间隔，避免历史分片过多时等待过久。
/// </summary>
internal static class LiveRefreshInterval
{
    private const int MaximumAutomaticSeconds = 15;
    private const int DefaultSeconds = 5;

    /// <summary>
    /// 手动设置优先；DASH 优先采用 MPD 的最小更新周期，其余情况按首次保留的分片总时长的一半提前两秒刷新。
    /// 自动计算的间隔限制在 1 到 15 秒之间。
    /// </summary>
    public static int GetSeconds(List<StreamSpec> streams, ExtractorType extractorType, int? manualSeconds)
    {
        if (manualSeconds is { } seconds) return Math.Max(1, seconds);

        if (extractorType == ExtractorType.MPEG_DASH)
        {
            // 多条轨道使用最短的更新周期，及时发现任一轨道的新分片。
            var updatePeriod = streams
                .Select(s => s.Playlist?.MinimumUpdatePeriod?.TotalSeconds)
                .Where(s => s is > 0)
                .Min();
            if (updatePeriod is { } period)
                return ClampAutomatic(period);
        }

        // 启动录制时已经按 live-take-count 裁剪，沿用旧逻辑计算首个 MediaPart 的总时长。
        var playlistDuration = streams
            .Select(s => s.Playlist?.MediaParts.FirstOrDefault()?.MediaSegments.Sum(segment => segment.Duration))
            .Where(duration => duration.HasValue && double.IsFinite(duration.Value))
            .Min();

        // 保留旧逻辑的提前两秒，并在短播放列表时将间隔限制为至少一秒。
        return playlistDuration is { } duration
            ? ClampAutomatic(Math.Floor(duration / 2) - 2)
            : DefaultSeconds;
    }

    private static int ClampAutomatic(double seconds) =>
        (int)Math.Clamp(Math.Ceiling(seconds), 1, MaximumAutomaticSeconds);
}
