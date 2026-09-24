using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Column;

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
        // 单文件下载汇报进度
        if (!flag && speedContainer is { SingleSegment: true, ResponseLength: not null })
        {
            task.MaxValue = (double)speedContainer.ResponseLength;
            task.Value = speedContainer.RDownloaded;
        }
        // 一秒汇报一次即可
        if (DateTimeStringDic.TryGetValue(taskId, out var oldTime) && oldTime != now && !flag)
        {
            speedContainer.NowSpeed = speedContainer.Downloaded;
            // 速度为0，计数增加
            if (speedContainer.Downloaded <= _stopSpeed) { speedContainer.AddLowSpeedCount(); }
            else speedContainer.ResetLowSpeedCount();
            speedContainer.Reset();
        }
        DateTimeStringDic[taskId] = now;
        var style = flag ? Style.Plain : MyStyle;
        return flag ? new Text("-", style).Centered() : new Text(GlobalUtil.FormatFileSize(speedContainer.NowSpeed) + "ps" + (speedContainer.LowSpeedCount > 0 ? $"({speedContainer.LowSpeedCount})" : ""), style).Centered();
    }
}
