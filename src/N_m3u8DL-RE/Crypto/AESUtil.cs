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
        public static byte[] AES128Decrypt(string filePath, byte[] keyByte, byte[] ivByte, CipherMode mode = CipherMode.CBC, PaddingMode padding = PaddingMode.PKCS7)
        {
            FileStream fs = new FileStream(filePath, FileMode.Open);
            //获取文件大小
            long size = fs.Length;
            byte[] inBuff = new byte[size];
            fs.Read(inBuff, 0, inBuff.Length);
            fs.Close();

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
