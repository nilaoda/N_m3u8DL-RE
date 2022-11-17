using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    internal class ImageHeaderUtil
    {
        public static bool IsImageHeader(byte[] bArr)
        {
            var size = bArr.Length;
            //PNG HEADER检测
            if (size > 3 && 137 == bArr[0] && 80 == bArr[1] && 78 == bArr[2] && 71 == bArr[3])
                return true;
            //GIF HEADER检测
            else if (size > 3 && 0x47 == bArr[0] && 0x49 == bArr[1] && 0x46 == bArr[2] && 0x38 == bArr[3])
                return true;
            //BMP HEADER检测
            else if (size > 10 && 0x42 == bArr[0] && 0x4D == bArr[1] && 0x00 == bArr[5] && 0x00 == bArr[6] && 0x00 == bArr[7] && 0x00 == bArr[8])
                return true;
            //JPEG HEADER检测
            else if (size > 3 && 0xFF == bArr[0] && 0xD8 == bArr[1] && 0xFF == bArr[2])
                return true;
            return false;
        }

        public static async Task ProcessAsync(string sourcePath)
        {
            var sourceData = await File.ReadAllBytesAsync(sourcePath);

            //PNG HEADER
            if (137 == sourceData[0] && 80 == sourceData[1] && 78 == sourceData[2] && 71 == sourceData[3])
            {
                if (sourceData.Length > 120 && 137 == sourceData[0] && 80 == sourceData[1] && 78 == sourceData[2] && 71 == sourceData[3] && 96 == sourceData[118] && 130 == sourceData[119])
                    sourceData = sourceData[120..];
                else if (sourceData.Length > 6102 && 137 == sourceData[0] && 80 == sourceData[1] && 78 == sourceData[2] && 71 == sourceData[3] && 96 == sourceData[6100] && 130 == sourceData[6101])
                    sourceData = sourceData[6102..];
                else if (sourceData.Length > 69 && 137 == sourceData[0] && 80 == sourceData[1] && 78 == sourceData[2] && 71 == sourceData[3] && 96 == sourceData[67] && 130 == sourceData[68])
                    sourceData = sourceData[69..];
                else if (sourceData.Length > 771 && 137 == sourceData[0] && 80 == sourceData[1] && 78 == sourceData[2] && 71 == sourceData[3] && 96 == sourceData[769] && 130 == sourceData[770])
                    sourceData = sourceData[771..];
                else
                {
                    //手动查询结尾标记 0x47 出现两次
                    int skip = 0;
                    for (int i = 4; i < sourceData.Length - 188 * 2 - 4; i++)
                    {
                        if (sourceData[i] == 0x47 && sourceData[i + 188] == 0x47 && sourceData[i + 188 + 188] == 0x47)
                        {
                            skip = i;
                            break;
                        }
                    }
                    sourceData = sourceData[skip..];
                }
            }
            //GIF HEADER
            else if (0x47 == sourceData[0] && 0x49 == sourceData[1] && 0x46 == sourceData[2] && 0x38 == sourceData[3])
            {
                sourceData = sourceData[42..];
            }
            //BMP HEADER
            else if (0x42 == sourceData[0] && 0x4D == sourceData[1] && 0x00 == sourceData[5] && 0x00 == sourceData[6] && 0x00 == sourceData[7] && 0x00 == sourceData[8])
            {
                sourceData = sourceData[0x3E..];
            }
            //JPEG HEADER检测
            else if (0xFF == sourceData[0] && 0xD8 == sourceData[1] && 0xFF == sourceData[2])
            {
                //手动查询结尾标记 0x47 出现两次
                int skip = 0;
                for (int i = 4; i < sourceData.Length - 188 * 2 - 4; i++)
                {
                    if (sourceData[i] == 0x47 && sourceData[i + 188] == 0x47 && sourceData[i + 188 + 188] == 0x47)
                    {
                        skip = i;
                        break;
                    }
                }
                sourceData = sourceData[skip..];
            }

            await File.WriteAllBytesAsync(sourcePath, sourceData);
        }
    }
}
