using N_m3u8DL_RE.Common.Util;
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
        protected override bool NoWrap => true;
        private ConcurrentDictionary<int, long> DownloadedSizeDic = new();
        public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
        public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

        public DownloadStatusColumn(ConcurrentDictionary<int, long> downloadedSizeDic)
        {
            this.DownloadedSizeDic = downloadedSizeDic;
        }

        public override IRenderable Render(RenderContext context, ProgressTask task, TimeSpan deltaTime)
        {
            var done = task.IsFinished;
            var flag = DownloadedSizeDic.TryGetValue(task.Id, out var size);
            var totalSize = flag ? (size / (task.Value / task.MaxValue)) : 0;
            var sizeStr = size == 0 ? "" : $"{GlobalUtil.FormatFileSize(flag ? size : 0)}/{GlobalUtil.FormatFileSize(totalSize)}";
            if (done) sizeStr = GlobalUtil.FormatFileSize(totalSize);

            return new Markup(sizeStr, MyStyle).RightAligned();
        }
    }
}
