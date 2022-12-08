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
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

        public DownloadStatusColumn(ConcurrentDictionary<int, SpeedContainer> speedContainerDic)
        {
            this.SpeedContainerDic = speedContainerDic;
        }

        public override IRenderable Render(RenderContext context, ProgressTask task, TimeSpan deltaTime)
        {
            if (task.Value == 0) return new Text("-", MyStyle).RightAligned();

            var speedContainer = SpeedContainerDic[task.Id];

            var size = speedContainer.RDownloaded;
            var totalSize = speedContainer.SingleSegment ? (speedContainer.ResponseLength ?? 0) : (long)(size / (task.Value / task.MaxValue));

            var sizeStr = $"{GlobalUtil.FormatFileSize(size)}/{GlobalUtil.FormatFileSize(totalSize)}";
            if (task.IsFinished) sizeStr = GlobalUtil.FormatFileSize(size);
            return new Text(sizeStr, MyStyle).RightAligned();
        }
    }
}
