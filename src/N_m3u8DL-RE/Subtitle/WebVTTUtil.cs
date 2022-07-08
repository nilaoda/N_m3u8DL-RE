using N_m3u8DL_RE.Common.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Subtitle
{
    public class WebVTTUtil
    {
        /// <summary>
        /// 修复VTT起始时间戳 <br/>
        /// X-TIMESTAMP-MAP=MPEGTS:8528254208,LOCAL:00:00:00.000
        /// </summary>
        /// <param name="sub"></param>
        /// <param name="baseTimestamp">基础时间戳</param>
        /// <returns></returns>
        public static void FixTimestamp(WebSub sub, long baseTimestamp)
        {
            if (baseTimestamp == 0 || sub.MpegtsTimestamp == 0)
            {
                return;
            }

            //The MPEG2 transport stream clocks (PCR, PTS, DTS) all have units of 1/90000 second
            var seconds = (sub.MpegtsTimestamp - baseTimestamp) / 90000;
            for (int i = 0; i < sub.Cues.Count; i++)
            {
                sub.Cues[i].StartTime += TimeSpan.FromSeconds(seconds);
                sub.Cues[i].EndTime += TimeSpan.FromSeconds(seconds);
            }
        }
    }
}
