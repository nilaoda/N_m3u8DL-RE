using System.Security.Cryptography;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util;

// 仅在同一轨道的顺序 part 下载期间复用相同内容的 init，不复用各 part 的媒体时钟。
internal sealed class VodInitInfoCache(string ffmpeg)
{
    private readonly Dictionary<string, List<Mediainfo>> results = [];

    internal async Task<List<Mediainfo>> ReadInitAsync(string file)
    {
        string? key = null;
        await using (var stream = File.OpenRead(file))
        {
            // 异常大 init 不额外扫描完整文件，沿用原有探测。
            if (stream.Length <= 4 * 1024 * 1024)
                key = Convert.ToHexString(await SHA256.HashDataAsync(stream));
        }
        if (key != null && results.TryGetValue(key, out var cached))
        {
            Logger.Debug("Reusing media info for identical init.");
            return cached.Select(info => info.CopyWithoutTiming()).ToList();
        }
        var infos = await MediainfoUtil.ReadInfoAsync(ffmpeg, file);
        if (key != null && infos.Any(info => info.Type is "Video" or "Audio" or "Subtitle"))
            results[key] = infos.Select(info => info.CopyWithoutTiming()).ToList();
        return infos;
    }
}
