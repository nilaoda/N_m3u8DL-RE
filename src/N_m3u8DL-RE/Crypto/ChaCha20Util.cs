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

            if (nonceBytes.Length == 8)
            {
                nonceBytes = [.. new byte[4] { 0, 0, 0, 0 }, .. nonceBytes];
            }

            using MemoryStream decStream = new();
            using BinaryReader reader = new(new MemoryStream(encryptedBuff));
            using BinaryWriter writer = new(decStream);

            ChaCha20 forDecrypting = new(keyBytes, nonceBytes, 0); // counter = 0

            while (true)
            {
                byte[] buffer = reader.ReadBytes(1024);
                if (buffer.Length == 0)
                {
                    break;
                }

                byte[] dec = new byte[buffer.Length];
                forDecrypting.DecryptBytes(dec, buffer); // Continues from previous counter state
                writer.Write(dec);
            }

            return decStream.ToArray();
        }
    }
}
