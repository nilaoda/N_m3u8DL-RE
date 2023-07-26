using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Column
{
    internal class RecordingDurationColumn : ProgressColumn
    {
        protected override bool NoWrap => true;
        private ConcurrentDictionary<int, int> _recodingDurDic;
        private ConcurrentDictionary<int, int>? _refreshedDurDic;
        public Style GreyStyle { get; set; } = new Style(foreground: Color.Grey);
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkGreen);
        public RecordingDurationColumn(ConcurrentDictionary<int, int> recodingDurDic)
        {
            _recodingDurDic = recodingDurDic;
        }
        public RecordingDurationColumn(ConcurrentDictionary<int, int> recodingDurDic, ConcurrentDictionary<int, int> refreshedDurDic)
        {
            _recodingDurDic = recodingDurDic;
            _refreshedDurDic = refreshedDurDic;
        }
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (_refreshedDurDic == null)
                return new Text($"{GlobalUtil.FormatTime(_recodingDurDic[task.Id])}", MyStyle).LeftJustified();
            else
            {                
                return new Text($"{GlobalUtil.FormatTime(_recodingDurDic[task.Id])}/{GlobalUtil.FormatTime(_refreshedDurDic[task.Id])}", GreyStyle);
            }
        }
    }
}
