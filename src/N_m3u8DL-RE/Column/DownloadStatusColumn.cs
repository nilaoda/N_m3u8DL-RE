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
        private ConcurrentDictionary<int, long> DownloadedSizeDic = new();
        private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; }
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

        public DownloadStatusColumn(ConcurrentDictionary<int, long> downloadedSizeDic, ConcurrentDictionary<int, SpeedContainer> speedContainerDic)
        {
            this.DownloadedSizeDic = downloadedSizeDic;
            this.SpeedContainerDic = speedContainerDic;
        }

        public override IRenderable Render(RenderContext context, ProgressTask task, TimeSpan deltaTime)
        {
            if (task.Value == 0) return new Text("-", MyStyle).RightAligned();

            var done = task.IsFinished;


            var flag = DownloadedSizeDic.TryGetValue(task.Id, out var size);
            var totalSize = flag ? (size / (task.Value / task.MaxValue)) : 0;

            //单文件下载汇报进度
            var speedContainer = SpeedContainerDic[task.Id];
            if (!done && speedContainer.SingleSegment && speedContainer.ResponseLength != null)
            {
                task.MaxValue = (double)speedContainer.ResponseLength;
                task.Value = speedContainer.RDownloaded;
                size = speedContainer.RDownloaded;
                totalSize = (double)speedContainer.ResponseLength;
            }

            var sizeStr = $"{GlobalUtil.FormatFileSize(flag ? size : 0)}/{GlobalUtil.FormatFileSize(totalSize)}";
            if (done) sizeStr = GlobalUtil.FormatFileSize(totalSize);
            return new Text(sizeStr, MyStyle).RightAligned();
        }
    }
}
