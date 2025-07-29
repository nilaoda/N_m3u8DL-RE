using Sodium;

namespace N_m3u8DL_RE.Crypto
{
    internal static class ChaCha20Util
    {
        public static byte[] DecryptInChunks(byte[] encryptedBuff, byte[] keyBytes, byte[] nonceBytes)
        {
            ArgumentNullException.ThrowIfNull(keyBytes);
            ArgumentNullException.ThrowIfNull(nonceBytes);

            if (keyBytes.Length != 32)
            {
                throw new ArgumentException("Key must be 32 bytes.", nameof(keyBytes));
            }

            if (nonceBytes.Length is not 12 and not 8)
            {
                throw new ArgumentException("Nonce must be 12 or 8 bytes.", nameof(nonceBytes));
            }

            // Extend 8-byte nonce to 12 bytes if needed
            if (nonceBytes.Length == 8)
            {
                nonceBytes = [.. new byte[4] { 0, 0, 0, 0 }, .. nonceBytes];
            }

            // Use Sodium.Core for raw ChaCha20
            return StreamEncryption.DecryptChaCha20(encryptedBuff, nonceBytes, keyBytes);
        }
    }
}
