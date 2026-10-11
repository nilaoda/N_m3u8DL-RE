using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column;

internal sealed class BinaryDownloadSpeedColumn(Func<long> receivedBytes) : ProgressColumn
{
    private readonly ProgressSample<double> _speed = new();
    private long _lastReceived;

    protected override bool NoWrap => true;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var stopped = task.IsFinished || task.StopTime != null || !task.IsStarted;
        var speed = stopped ? 0 : _speed.GetValue(task, elapsed =>
        {
            if (elapsed == TimeSpan.Zero) return 0;
            var received = receivedBytes();
            var value = Math.Max(0, received - _lastReceived) / elapsed.TotalSeconds;
            _lastReceived = received;
            return value;
        });
        var style = stopped ? Style.Plain : new Style(foreground: Color.Green);
        var value = stopped ? "-" : GlobalUtil.FormatFileSize(speed) + "ps";
        return new Text(value, style).Centered();
    }
}
