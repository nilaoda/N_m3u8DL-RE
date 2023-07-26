using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
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
    internal class RecordingSizeColumn : ProgressColumn
    {
        protected override bool NoWrap => true;
        private ConcurrentDictionary<int, double> RecodingSizeDic = new(); //临时的大小 每秒刷新用
        private ConcurrentDictionary<int, double> _recodingSizeDic;
        private ConcurrentDictionary<int, string> DateTimeStringDic = new();
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public RecordingSizeColumn(ConcurrentDictionary<int, double> recodingSizeDic)
        {
            _recodingSizeDic = recodingSizeDic;
        }
        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var taskId = task.Id;
            //一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(taskId, out var oldTime) && oldTime != now)
            {
                RecodingSizeDic[task.Id] = _recodingSizeDic[task.Id];
            }
            DateTimeStringDic[taskId] = now;
            var flag = RecodingSizeDic.TryGetValue(taskId, out var size);
            return new Text(GlobalUtil.FormatFileSize(flag ? size : 0), MyStyle).LeftJustified();
        }
    }
}
