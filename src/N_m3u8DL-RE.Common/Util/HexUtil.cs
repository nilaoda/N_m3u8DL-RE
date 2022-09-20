using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Util
{
    public class HexUtil
    {
        public static string BytesToHex(byte[] data, string split = "")
        {
            return BitConverter.ToString(data).Replace("-", split);
        }

        /// <summary>
        /// 判断是不是HEX字符串
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        public static bool TryParseHexString(string input, out byte[]? bytes)
        {
            bytes = null;
            input = input.ToUpper();
            if (input.StartsWith("0X"))
                input = input[2..];
            if (input.Length % 2 != 0)
                return false;
            if (input.Any(c => !"0123456789ABCDEF".Contains(c)))
                return false;
            bytes = HexToBytes(input);
            return true;
        }

        public static byte[] HexToBytes(string hex)
        {
            hex = hex.Trim();
            if (hex.StartsWith("0x") || hex.StartsWith("0X"))
                hex = hex.Substring(2);
            byte[] bytes = new byte[hex.Length / 2];

            for (int i = 0; i < hex.Length; i += 2)
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);

            return bytes;
        }
    }
}
