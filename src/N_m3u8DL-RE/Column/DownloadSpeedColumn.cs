using N_m3u8DL_RE.Common.Log;
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
    internal sealed class DownloadSpeedColumn : ProgressColumn
    {
        private long _stopSpeed = 0;
        private ConcurrentDictionary<int, string> DateTimeStringDic = new();
        protected override bool NoWrap => true;
        private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; }

        public DownloadSpeedColumn(ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic)
        {
            this.SpeedContainerDic = SpeedContainerDic;
        }

        public Style MyStyle { get; set; } = new Style(foreground: Color.Green);

        public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
        {
            var taskId = task.Id;
            var speedContainer = SpeedContainerDic[taskId];
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var flag = task.IsFinished || !task.IsStarted;
            //单文件下载汇报进度
            if (!flag && speedContainer.SingleSegment && speedContainer.ResponseLength != null)
            {
                task.MaxValue = (double)speedContainer.ResponseLength;
                task.Value = speedContainer.RDownloaded;
            }
            //一秒汇报一次即可
            if (DateTimeStringDic.TryGetValue(taskId, out var oldTime) && oldTime != now && !flag)
            {
                speedContainer.NowSpeed = speedContainer.Downloaded;
                //速度为0，计数增加
                if (speedContainer.Downloaded <= _stopSpeed) { speedContainer.AddLowSpeedCount(); }
                else speedContainer.ResetLowSpeedCount();
                speedContainer.Reset();
            }
            DateTimeStringDic[taskId] = now;
            var style = flag ? Style.Plain : MyStyle;
            return flag ? new Text("-", style).Centered() : new Text(FormatFileSize(speedContainer.NowSpeed) + (speedContainer.LowSpeedCount > 0 ? $"({speedContainer.LowSpeedCount})" : ""), style).Centered();
        }

        private static string FormatFileSize(double fileSize)
        {
            return fileSize switch
            {
                < 0 => throw new ArgumentOutOfRangeException(nameof(fileSize)),
                >= 1024 * 1024 * 1024 => string.Format("{0:########0.00}GBps", (double)fileSize / (1024 * 1024 * 1024)),
                >= 1024 * 1024 => string.Format("{0:####0.00}MBps", (double)fileSize / (1024 * 1024)),
                >= 1024 => string.Format("{0:####0.00}KBps", (double)fileSize / 1024),
                _ => string.Format("{0:####0.00}Bps", fileSize)
            };
        }
    }
}
