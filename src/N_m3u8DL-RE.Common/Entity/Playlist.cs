using N_m3u8DL_RE.Common.Enum;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Entity
{
    public class Playlist
    {
        //对应Url信息
        public string Url { get; set; }
        //是否直播
        public bool IsLive { get; set; } = false;
        //INIT信息
        public MediaSegment? MediaInit { get; set; }
        //分片信息
        public List<MediaPart> MediaParts { get; set; } = new List<MediaPart>();
    }
}
