using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Common.Resource
{
    internal class TextContainer
    {
        public string ZH_CN { get; set; }
        public string ZH_TW { get; set; }
        public string EN_US { get; set; }

        public TextContainer(string zhCN, string zhTW, string enUS)
        {
            ZH_CN = zhCN;
            ZH_TW = zhTW;
            EN_US = enUS;
        }
    }
}
