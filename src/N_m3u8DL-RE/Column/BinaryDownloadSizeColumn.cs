using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Util;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column;

internal sealed class BinaryDownloadSizeColumn(ConcurrentDictionary<int, long> sizes, long? totalSize) : ProgressColumn
{
    private readonly ProgressSample<string> _size = new();

    protected override bool NoWrap => true;

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var size = _size.GetValue(task, _ =>
        {
            sizes.TryGetValue(task.Id, out var written);
            var value = GlobalUtil.FormatFileSize(written);
            if (totalSize is >= 0)
            {
                value += "/" + GlobalUtil.FormatFileSize(totalSize.Value);
            }
            return value;
        });
        return new Text(size, new Style(foreground: Color.DarkCyan)).RightJustified();
    }
}
