using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Crypto
{
    internal class AESUtil
    {
        /// <summary>
        /// AES-128解密，解密后原地替换文件
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="keyByte"></param>
        /// <param name="ivByte"></param>
        /// <param name="mode"></param>
        /// <param name="padding"></param>
        public static void AES128Decrypt(string filePath, byte[] keyByte, byte[] ivByte, CipherMode mode = CipherMode.CBC, PaddingMode padding = PaddingMode.PKCS7)
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var decrypted = AES128Decrypt(fileBytes, keyByte, ivByte, mode, padding);
            File.WriteAllBytes(filePath, decrypted);
        }

        public static byte[] AES128Decrypt(byte[] encryptedBuff, byte[] keyByte, byte[] ivByte, CipherMode mode = CipherMode.CBC, PaddingMode padding = PaddingMode.PKCS7)
        {
            byte[] inBuff = encryptedBuff;

            Aes dcpt = Aes.Create();
            dcpt.BlockSize = 128;
            dcpt.KeySize = 128;
            dcpt.Key = keyByte;
            dcpt.IV = ivByte;
            dcpt.Mode = mode;
            dcpt.Padding = padding;

            ICryptoTransform cTransform = dcpt.CreateDecryptor();
            byte[] resultArray = cTransform.TransformFinalBlock(inBuff, 0, inBuff.Length);
            return resultArray;
        }
    }
}
