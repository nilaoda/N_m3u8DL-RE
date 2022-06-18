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
