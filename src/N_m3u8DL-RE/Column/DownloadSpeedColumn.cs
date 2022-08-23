using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Column
{
    internal sealed class DownloadSpeedColumn : ProgressColumn
    {
        private string DateTimeString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        private string Speed = "0Bps";
        protected override bool NoWrap => true;
        public SpeedContainer SpeedContainer { get; set; }

        public DownloadSpeedColumn(SpeedContainer SpeedContainer)
        {
            this.SpeedContainer = SpeedContainer;
        }

        public Style MyStyle { get; set; } = new Style(foreground: Color.Green);

        public override IRenderable Render(RenderContext context, ProgressTask task, TimeSpan deltaTime)
        {
            var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            //一秒汇报一次即可
            if (DateTimeString != now)
            {
                Speed = FormatFileSize(SpeedContainer.Downloaded);
                SpeedContainer.Reset();
                DateTimeString = now;
            }
            var flag = task.IsFinished || !task.IsStarted;
            var style = flag ? Style.Plain : MyStyle;
            return flag ? new Text("-", style).Centered() : new Text(Speed, style).Centered();
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
