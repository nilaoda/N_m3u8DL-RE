using System.Collections.Concurrent;

using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;

using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column
{
    internal sealed class DownloadSpeedColumn(ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic) : ProgressColumn
    {
        // private readonly long _stopSpeed;
        private readonly ConcurrentDictionary<int, string> DateTimeStringDic = new();
        protected override bool NoWrap => true;
        private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; } = SpeedContainerDic;

        public Style MyStyle { get; set; } = new Style(foreground: Color.Green);

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            int taskId = task.Id;
            SpeedContainer speedContainer = SpeedContainerDic[taskId];
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            bool flag = task.IsFinished || !task.IsStarted;
            // 单文件下载汇报进度
            if (!flag && speedContainer is { SingleSegment: true, ResponseLength: not null })
            {
                task.MaxValue = (double)speedContainer.ResponseLength;
                task.Value = speedContainer.RDownloaded;
            }
            // 一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(taskId, out string? oldTime) && oldTime != now && !flag)
            {
                speedContainer.NowSpeed = speedContainer.Downloaded;
                // 速度为0，计数增加
                _ = speedContainer.Downloaded <= 0 ? speedContainer.AddLowSpeedCount() : speedContainer.ResetLowSpeedCount();

                speedContainer.Reset();
            }
            DateTimeStringDic[taskId] = now;
            Style style = flag ? Style.Plain : MyStyle;
            return flag ? new Text("-", style).Centered() : new Text(GlobalUtil.FormatFileSize(speedContainer.NowSpeed) + "ps" + (speedContainer.LowSpeedCount > 0 ? $"({speedContainer.LowSpeedCount})" : ""), style).Centered();
        }
    }
}
