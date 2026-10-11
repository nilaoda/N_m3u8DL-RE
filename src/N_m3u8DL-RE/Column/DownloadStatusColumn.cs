using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.Column;

internal class DownloadStatusColumn : ProgressColumn
{
    private ConcurrentDictionary<int, SpeedContainer> SpeedContainerDic { get; set; }
    private readonly ProgressSample<string> _size = new();
    public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
    public Style FinishedStyle { get; set; } = new Style(foreground: Color.Green);

    public DownloadStatusColumn(ConcurrentDictionary<int, SpeedContainer> speedContainerDic)
    {
        this.SpeedContainerDic = speedContainerDic;
    }

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        // 一秒刷新一次显示，结束时立即显示最终大小。
        var sizeStr = _size.GetValue(task, _ =>
        {
            var speedContainer = SpeedContainerDic[task.Id];
            var size = speedContainer.RDownloaded;
            if (task.IsFinished || task.StopTime != null) return GlobalUtil.FormatFileSize(size);
            if (task.Value == 0) return "-";
            var totalSize = speedContainer.SingleSegment ? (speedContainer.ResponseLength ?? 0) : (long)(size / (task.Value / task.MaxValue));
            return $"{GlobalUtil.FormatFileSize(size)}/{GlobalUtil.FormatFileSize(totalSize)}";
        });
        return new Text(sizeStr, MyStyle).RightJustified();
    }
}