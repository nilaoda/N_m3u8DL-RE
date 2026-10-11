using N_m3u8DL_RE.Entity;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Column;

internal sealed class DownloadSpeedColumn : ProgressColumn
{
    private long _stopSpeed = 0;
    private readonly ProgressSample<long> _speed = new();
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
        var flag = task.IsFinished || task.StopTime != null || !task.IsStarted;
        // 单文件下载汇报进度
        if (!flag && speedContainer is { SingleSegment: true, ResponseLength: not null })
        {
            task.MaxValue = Math.Max(1, speedContainer.ResponseLength.Value);
            // 字节读满后还需校验响应并提交文件；先限制在 99.99%，避免显示完成且阻止重试回退。
            task.Value = Math.Min(speedContainer.RDownloaded, Math.Floor(task.MaxValue * 0.9999));
        }
        // 一秒采样一次，按实际间隔计算速度；首次显示不清空接收计数。
        if (!flag)
        {
            _speed.GetValue(task, elapsed =>
            {
                if (elapsed == TimeSpan.Zero) return speedContainer.NowSpeed;
                var downloaded = speedContainer.Reset();
                speedContainer.NowSpeed = (long)(downloaded / elapsed.TotalSeconds);
                // 速度为0，计数增加
                if (downloaded <= _stopSpeed) { speedContainer.AddLowSpeedCount(); }
                else speedContainer.ResetLowSpeedCount();
                return speedContainer.NowSpeed;
            });
        }
        var style = flag ? Style.Plain : MyStyle;
        return flag ? new Text("-", style).Centered() : new Text(GlobalUtil.FormatFileSize(speedContainer.NowSpeed) + "ps" + (speedContainer.LowSpeedCount > 0 ? $"({speedContainer.LowSpeedCount})" : ""), style).Centered();
    }
}
