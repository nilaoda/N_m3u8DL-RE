using System.Diagnostics;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column;

internal sealed class BinaryDownloadSpeedColumn(Func<long> receivedBytes) : ProgressColumn
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private double _lastSampleSeconds;
    private long _lastReceived;
    private double _speed;

    protected override bool NoWrap => true;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var elapsed = _watch.Elapsed.TotalSeconds - _lastSampleSeconds;
        if (elapsed >= 1)
        {
            var received = receivedBytes();
            _speed = Math.Max(0, received - _lastReceived) / elapsed;
            _lastReceived = received;
            _lastSampleSeconds = _watch.Elapsed.TotalSeconds;
        }
        else if (_lastSampleSeconds == 0 && elapsed > 0)
        {
            _speed = receivedBytes() / elapsed;
        }
        var style = task.IsFinished || !task.IsStarted ? Style.Plain : new Style(foreground: Color.Green);
        var value = task.IsFinished || !task.IsStarted ? "-" : GlobalUtil.FormatFileSize(_speed) + "ps";
        return new Text(value, style).Centered();
    }
}
