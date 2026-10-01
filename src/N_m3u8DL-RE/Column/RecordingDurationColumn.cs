using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Collections.Concurrent;

namespace N_m3u8DL_RE.Column;

internal class RecordingDurationColumn : ProgressColumn
{
    protected override bool NoWrap => true;
    private ConcurrentDictionary<int, TimeSpan> _recodingDurDic;
    private ConcurrentDictionary<int, TimeSpan>? _refreshedDurDic;
    public Style GreyStyle { get; set; } = new Style(foreground: Color.Grey);
    public Style MyStyle { get; set; } = new Style(foreground: Color.DarkGreen);
    public RecordingDurationColumn(ConcurrentDictionary<int, TimeSpan> recodingDurDic)
    {
        _recodingDurDic = recodingDurDic;
    }
    public RecordingDurationColumn(ConcurrentDictionary<int, TimeSpan> recodingDurDic, ConcurrentDictionary<int, TimeSpan> refreshedDurDic)
    {
        _recodingDurDic = recodingDurDic;
        _refreshedDurDic = refreshedDurDic;
    }
    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        if (_refreshedDurDic == null)
            return new Text($"{GlobalUtil.FormatTime(_recodingDurDic[task.Id])}", MyStyle).LeftJustified();
        return new Text($"{GlobalUtil.FormatTime(_recodingDurDic[task.Id])}/{GlobalUtil.FormatTime(_refreshedDurDic[task.Id])}", GreyStyle);
    }
}