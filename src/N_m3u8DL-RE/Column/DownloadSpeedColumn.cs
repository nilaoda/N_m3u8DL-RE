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
        private TimeSpan CalcTimeSpan = TimeSpan.FromSeconds(0);
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
            CalcTimeSpan = CalcTimeSpan.Add(deltaTime);
            //一秒汇报一次即可
            if (CalcTimeSpan.TotalSeconds > 1)
            {
                Speed = FormatFileSize(SpeedContainer.Downloaded / CalcTimeSpan.TotalSeconds);
                SpeedContainer.Reset();
                CalcTimeSpan = TimeSpan.FromSeconds(0);
            }
            var percentage = (int)task.Percentage;
            var flag = percentage == 100 || percentage == 0;
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
                _ => string.Format("{0}Bps", fileSize)
            };
        }
    }
}
