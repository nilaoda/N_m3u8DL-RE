using System.Collections.Concurrent;

using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column
{
    internal class DownloadStatusColumn(ConcurrentDictionary<int, SpeedContainer> speedContainerDic) : ProgressColumn
    {
        private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; } = speedContainerDic;
        private ConcurrentDictionary<int, string> DateTimeStringDic = new();
        private ConcurrentDictionary<int, string> SizeDic = new();
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            if (task.Value == 0) return new Text("-", MyStyle).RightJustified();
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            SpeedContainer speedContainer = SpeedContainerDic[task.Id];
            long size = speedContainer.RDownloaded;

            // 一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(task.Id, out string? oldTime) && oldTime != now)
            {
                long totalSize = speedContainer.SingleSegment ? (speedContainer.ResponseLength ?? 0) : (long)(size / (task.Value / task.MaxValue));
                SizeDic[task.Id] = $"{GlobalUtil.FormatFileSize(size)}/{GlobalUtil.FormatFileSize(totalSize)}";
            }
            DateTimeStringDic[task.Id] = now;
            SizeDic.TryGetValue(task.Id, out string? sizeStr);

            if (task.IsFinished) sizeStr = GlobalUtil.FormatFileSize(size);

            return new Text(sizeStr ?? "-", MyStyle).RightJustified();
        }
    }
}