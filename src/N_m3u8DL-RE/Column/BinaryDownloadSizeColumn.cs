using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column;

internal sealed class BinaryDownloadSizeColumn(ConcurrentDictionary<int, long> sizes, long? totalSize) : ProgressColumn
{
    protected override bool NoWrap => true;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        sizes.TryGetValue(task.Id, out var written);
        var size = GlobalUtil.FormatFileSize(written);
        if (totalSize is >= 0)
        {
            size += "/" + GlobalUtil.FormatFileSize(totalSize.Value);
        }
        return new Text(size, new Style(foreground: Color.DarkCyan)).RightJustified();
    }
}
