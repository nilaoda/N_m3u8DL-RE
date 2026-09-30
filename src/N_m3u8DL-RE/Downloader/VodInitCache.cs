using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Downloader;

internal sealed class VodInitCache(string directory)
{
    private readonly Dictionary<InitKey, string> files = [];
    private sealed record InitKey(string Url, long? Start, long? Length, string Method, string Key, string IV, string? Kid);

    private static InitKey Key(MediaSegment init) => new(init.Url, init.StartRange, init.ExpectLength,
        init.EncryptInfo.Method.ToString(), Convert.ToHexString(init.EncryptInfo.Key ?? []),
        Convert.ToHexString(init.EncryptInfo.IV ?? []), init.EncryptInfo.KID);

    // 预览探测与下载缓存使用同一身份，避免同 URL 的不同范围或密钥被错误复用。
    internal static string Identity(MediaSegment init)
    {
        var key = Key(init);
        var values = new[] { key.Url, key.Start?.ToString(CultureInfo.InvariantCulture),
            key.Length?.ToString(CultureInfo.InvariantCulture), key.Method, key.Key, key.IV, key.Kid };
        var identity = string.Concat(values.Select(value => value == null ? "-1:" : $"{value.Length}:{value}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    // 仅在同一轨道的顺序 part 下载中使用。缓存 AES 解密后的 init，CENC 解密/MSS 重建前复制到各 part。
    // 不能按 URL 单独复用：同 URL 的字节范围、AES 密钥或 IV 可能改变。
    public async Task<DownloadResult?> DownloadAsync(MediaSegment init, string savePath, SpeedContainer speed,
        Func<string, Task<DownloadResult?>> download)
    {
        var key = Key(init);
        var reused = files.TryGetValue(key, out var cached) && File.Exists(cached);
        if (!reused)
        {
            Directory.CreateDirectory(directory);
            // 长度前缀避免 URL 内的分隔符碰撞，也不依赖反射序列化，兼容 Native AOT。
            var hash = Identity(init);
            var result = await download(Path.Combine(directory, hash + ".mp4.tmp"));
            if (result is not { Success: true })
                return result;
            files[key] = cached = result.ActualFilePath;
        }
        var path = Path.ChangeExtension(savePath, null);
        // MSS 会就地修改 header，其他解密引擎也可能清理源 init，因此永远复制、不共享可变文件。
        File.Copy(cached!, path, overwrite: true);
        var size = new FileInfo(path).Length;
        if (reused)
            speed.Add(size);
        return new DownloadResult { ActualFilePath = path, ActualContentLength = size };
    }
}
