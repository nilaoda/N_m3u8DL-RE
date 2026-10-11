using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.Column;

internal class RecordingSizeColumn : ProgressColumn
{
    protected override bool NoWrap => true;
    private readonly ConcurrentDictionary<int, long> _recordingSizeDic;
    private readonly ProgressSample<string> _size = new();
    private readonly Func<bool>? _showRealTimeMergeMultiplier;
    private readonly Func<int, bool>? _showApproximate;
    public Style MyStyle { get; set; } = new Style(foreground: Color.DarkCyan);
    public RecordingSizeColumn(ConcurrentDictionary<int, long> recordingSizeDic, Func<bool>? showRealTimeMergeMultiplier = null, Func<int, bool>? showApproximate = null)
    {
        _recordingSizeDic = recordingSizeDic;
        _showRealTimeMergeMultiplier = showRealTimeMergeMultiplier;
        _showApproximate = showApproximate;
    }
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var sizeStr = _size.GetValue(task, _ =>
        {
            _recordingSizeDic.TryGetValue(task.Id, out var size);
            var approximate = _showApproximate?.Invoke(task.Id) == true ? "≈" : "";
            var multiplier = _showRealTimeMergeMultiplier?.Invoke() == true ? "(*2)" : "";
            return approximate + GlobalUtil.FormatFileSize(size) + multiplier;
        });
        return new Text(sizeStr, MyStyle).LeftJustified();
    }
}
