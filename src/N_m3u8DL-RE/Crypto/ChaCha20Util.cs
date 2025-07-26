using CSChaCha20;

namespace N_m3u8DL_RE.Crypto
{
    internal static class ChaCha20Util
    {
        public static byte[] DecryptPer1024Bytes(byte[] encryptedBuff, byte[] keyBytes, byte[] nonceBytes)
        {
            if (keyBytes.Length != 32)
            {
                throw new Exception("Key must be 32 bytes!");
            }

            if (nonceBytes.Length != 12 && nonceBytes.Length != 8)
            {
                throw new Exception("Key must be 12 or 8 bytes!");
            }

            if (nonceBytes.Length == 8)
            {
                nonceBytes = [.. (new byte[4] { 0, 0, 0, 0 }), .. nonceBytes];
            }

            MemoryStream decStream = new();
            using BinaryReader reader = new(new MemoryStream(encryptedBuff));
            using (BinaryWriter writer = new(decStream))
            {
                while (true)
                {
                    byte[] buffer = reader.ReadBytes(1024);
                    byte[] dec = new byte[buffer.Length];
                    if (buffer.Length > 0)
                    {
                        ChaCha20 forDecrypting = new(keyBytes, nonceBytes, 0);
                        forDecrypting.DecryptBytes(dec, buffer);
                        writer.Write(dec, 0, dec.Length);
                    }
                    else
                    {
                        break;
                    }
                }
            }

            return decStream.ToArray();
        }
    }
}