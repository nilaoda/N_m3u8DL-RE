using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.Column;

internal class RecordingSizeColumn : ProgressColumn
{
    protected override bool NoWrap => true;
    private readonly ConcurrentDictionary<int, long> _recordingSizeDic;
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
        _recordingSizeDic.TryGetValue(task.Id, out var size);
        var approximate = _showApproximate?.Invoke(task.Id) == true ? "≈" : "";
        var multiplier = _showRealTimeMergeMultiplier?.Invoke() == true ? "(*2)" : "";
        return new Text(approximate + GlobalUtil.FormatFileSize(size) + multiplier, MyStyle).LeftJustified();
    }
}
