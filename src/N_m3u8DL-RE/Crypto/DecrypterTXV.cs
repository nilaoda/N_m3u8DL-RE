using System.Text;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Crypto.TXVDRM;

namespace N_m3u8DL_RE.Crypto;
//C121745A1EF00C85 Chacha20V3
internal static class DecrypterTXV
{
    private const int KeySize = 32;
    private const int NonceSize = 12;

    public static void Decrypt(string filePath, byte[]? v3Key, byte[]? v3Nonce, byte[]? v2Key, byte[]? v2Nonce)
    {
        var data = File.ReadAllBytes(filePath);
        if (data.Length == 0) return;

        if (MediaDecryptor.LooksLikeTs(data) || MediaDecryptor.LooksLikeFmp4(data) || LooksLikeText(data))
        {
            Logger.Debug("TXVDRM: skip non-encrypted data: {}", filePath);
            return;
        }
        //CacheManager::SetEncryptKeyAndNonce sse f n
        var key3 = NormalizeKey(v3Key, "--custom-hls-key");
        var nonce3 = NormalizeNonce(v3Nonce, "--custom-hls-iv");
        //TVKVodTXVDrmKeyExtractor extractFromJsonFormat k1 nonce1
        var key2 = NormalizeKey(v2Key, "--custom-txvdrm-key");
        var nonce2 = NormalizeNonce(v2Nonce, "--custom-txvdrm-iv");

        var decrypted = MediaDecryptor.DecryptPer(data, key3, nonce3, key2, nonce2);
        File.WriteAllBytes(filePath, decrypted);

        Logger.Debug("TXVDRM: {} bytes -> {} bytes", data.Length, decrypted.Length);
    }

    private static byte[] NormalizeKey(byte[]? key, string optionName)
    {
        if (key is not { Length: > 0 })
            throw new MediaDecryptException($"TXVDRM: 未提供 {optionName}");

        if (key.Length == 16)
            return Encoding.ASCII.GetBytes(Convert.ToHexString(key));

        if (key.Length != KeySize)
            throw new MediaDecryptException($"TXVDRM: {optionName} 需要 32 字节，当前 {key.Length} 字节");

        return key;
    }

    private static byte[] NormalizeNonce(byte[]? nonce, string optionName)
    {
        if (nonce is not { Length: > 0 })
            throw new MediaDecryptException($"TXVDRM: 未提供 {optionName}");

        if (nonce.Length == 16)
            nonce = Encoding.ASCII.GetBytes(Convert.ToHexString(nonce));

        if (nonce.Length < NonceSize)
            throw new MediaDecryptException($"TXVDRM: {optionName} 需要 12 字节，当前 {nonce.Length} 字节");

        return nonce.Length == NonceSize ? nonce : nonce[..NonceSize];
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return true;

        return data.StartsWith("WEBVTT"u8)
               || data.StartsWith("#EXTM3U"u8)
               || data.StartsWith("<?xml"u8);
    }
}
