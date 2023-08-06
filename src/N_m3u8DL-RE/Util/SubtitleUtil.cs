using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    internal class SubtitleUtil
    {
        /// <summary>
        /// 写出图形字幕PNG文件
        /// </summary>
        /// <param name="finalVtt"></param>
        /// <param name="tmpDir">临时目录</param>
        /// <returns></returns>
        public static async Task<bool> TryWriteImagePngsAsync(WebVttSub? finalVtt, string tmpDir)
        {
            if (finalVtt != null && finalVtt.Cues.Any(v => v.Payload.StartsWith("Base64::")))
            {
                Logger.WarnMarkUp(ResString.processImageSub);
                var _i = 0;
                foreach (var img in finalVtt.Cues.Where(v => v.Payload.StartsWith("Base64::")))
                {
                    var name = $"{_i++}.png";
                    var dest = "";
                    for (; File.Exists(dest = Path.Combine(tmpDir, name)); name = $"{_i++}.png") ;
                    var base64 = img.Payload[8..];
                    await File.WriteAllBytesAsync(dest, Convert.FromBase64String(base64));
                    img.Payload = name;
                }
                return true;
            }
            else return false;
        }
    }
}
