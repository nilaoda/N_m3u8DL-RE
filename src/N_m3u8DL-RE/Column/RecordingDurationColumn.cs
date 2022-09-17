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
        public Style MyStyle { get; set; } = new Style(foreground: Color.Grey);
        public RecordingDurationColumn(ConcurrentDictionary<int, int> recodingDurDic)
        {
            _recodingDurDic = recodingDurDic;
        }
        public override IRenderable Render(RenderContext context, ProgressTask task, TimeSpan deltaTime)
        {
            return new Text(GlobalUtil.FormatTime(_recodingDurDic[task.Id]), MyStyle).LeftAligned();
        }
    }
}
