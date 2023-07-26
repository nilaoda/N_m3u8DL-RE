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
    internal class DownloadStatusColumn : ProgressColumn
    {
        private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; }
        private ConcurrentDictionary<int, string> DateTimeStringDic = new();
        private ConcurrentDictionary<int, string> SizeDic = new();
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

        public DownloadStatusColumn(ConcurrentDictionary<int, SpeedContainer> speedContainerDic)
        {
            this.SpeedContainerDic = speedContainerDic;
        }

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (task.Value == 0) return new Text("-", MyStyle).RightJustified();
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            var speedContainer = SpeedContainerDic[task.Id];
            var size = speedContainer.RDownloaded;

            //一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(task.Id, out var oldTime) && oldTime != now)
            {
                var totalSize = speedContainer.SingleSegment ? (speedContainer.ResponseLength ?? 0) : (long)(size / (task.Value / task.MaxValue));
                SizeDic[task.Id] = $"{GlobalUtil.FormatFileSize(size)}/{GlobalUtil.FormatFileSize(totalSize)}";
            }
            DateTimeStringDic[task.Id] = now;
            SizeDic.TryGetValue(task.Id, out var sizeStr);

            if (task.IsFinished) sizeStr = GlobalUtil.FormatFileSize(size);

            return new Text(sizeStr ?? "-", MyStyle).RightJustified();
        }
    }
}
