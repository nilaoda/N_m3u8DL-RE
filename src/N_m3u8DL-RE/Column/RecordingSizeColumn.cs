using System.Collections.Concurrent;
using System.Globalization;

using N_m3u8DL_RE.Common.Util;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column
{
    internal sealed class RecordingSizeColumn(ConcurrentDictionary<int, double> recodingSizeDic) : ProgressColumn
    {
        protected override bool NoWrap => true;
        private readonly ConcurrentDictionary<int, double> RecodingSizeDic = new(); // 临时的大小 每秒刷新用
        private readonly ConcurrentDictionary<int, double> _recodingSizeDic = recodingSizeDic;
        private readonly ConcurrentDictionary<int, string> DateTimeStringDic = new();
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            int taskId = task.Id;
            // 一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(taskId, out string? oldTime) && oldTime != now)
            {
                RecodingSizeDic[task.Id] = _recodingSizeDic[task.Id];
            }
            DateTimeStringDic[taskId] = now;
            bool flag = RecodingSizeDic.TryGetValue(taskId, out double size);
            return new Text(GlobalUtil.FormatFileSize(flag ? size : 0), MyStyle).LeftJustified();
        }
    }
}