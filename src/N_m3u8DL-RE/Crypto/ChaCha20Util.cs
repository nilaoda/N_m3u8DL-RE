using CSChaCha20;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Crypto
{
    internal class ChaCha20Util
    {
        public static byte[] DecryptPer1024Bytes(byte[] encryptedBuff, byte[] keyBytes, byte[] nonceBytes)
        {
            if (keyBytes.Length != 32)
                throw new Exception("Key must be 32 bytes!");
            if (nonceBytes.Length != 12 && nonceBytes.Length != 8)
                throw new Exception("Key must be 12 or 8 bytes!");
            if (nonceBytes.Length == 8)
                nonceBytes = (new byte[4] { 0, 0, 0, 0 }).Concat(nonceBytes).ToArray();

            var decStream = new MemoryStream();
            using BinaryReader reader = new BinaryReader(new MemoryStream(encryptedBuff));
            using (BinaryWriter writer = new BinaryWriter(decStream))
            while (true)
            {
                var buffer = reader.ReadBytes(1024);
                byte[] dec = new byte[buffer.Length];
                if (buffer.Length > 0)
                {
                    ChaCha20 forDecrypting = new ChaCha20(keyBytes, nonceBytes, 0);
                    forDecrypting.DecryptBytes(dec, buffer);
                    writer.Write(dec, 0, dec.Length);
                }
                else
                {
                    break;
                }
            }

            return decStream.ToArray();
        }
    }
}
