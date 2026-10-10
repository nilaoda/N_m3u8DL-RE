using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace N_m3u8DL_RE.Column;

// 下载时沿用原列；处理时在同一行呈现当前阶段的数据。
internal sealed class MediaProcessingColumn(ProgressColumn downloadColumn) : ProgressColumn
{
    protected override bool NoWrap => downloadColumn is not ProgressBarColumn;

    // PercentageColumn 原本显示整数百分比，固定 4 字符不足以容纳处理阶段的 100.00%。
    public override int? GetColumnWidth(RenderOptions options) => downloadColumn is PercentageColumn ? 7 : downloadColumn.GetColumnWidth(options);

    public override IRenderable Render(RenderOptions options, ProgressTask task, TimeSpan deltaTime)
    {
        var processing = MediaProcessingProgress.Get(task);
        if (processing == null)
            return downloadColumn.Render(options, task, deltaTime);
        var display = processing.DisplayTask;
        var value = processing.Progress;
        if (downloadColumn is MyPercentageColumn or PercentageColumn)
            return new Text(processing.Failed ? "-" : processing.Finished ? "100.00%" : value.Fraction == null ? "-" : $"{display.Percentage:F2}%",
                new Style(processing.Failed ? Color.Red : processing.Finished ? Color.Green : Color.Default)).RightJustified();
        if (downloadColumn is DownloadStatusColumn or DownloadedColumn)
            return new Text(value.Bytes is { } bytes ? GlobalUtil.FormatFileSize(bytes) +
                (value.TotalBytes is { } total ? "/" + GlobalUtil.FormatFileSize(total) : "") :
                value.Seconds is { } seconds ? TimeSpan.FromSeconds(seconds).ToString(@"hh\:mm\:ss") : "-", new Style(Color.DarkCyan)).RightJustified();
        if (downloadColumn is DownloadSpeedColumn or TransferSpeedColumn)
            return new Text(!processing.Finished && value.TotalBytes != null && value.Bytes is { } written && processing.Elapsed.TotalSeconds >= 1
                ? GlobalUtil.FormatFileSize((long)(written / processing.Elapsed.TotalSeconds)) + "ps" : "-").Centered();
        if (downloadColumn is RemainingTimeColumn)
        {
            // 未知总量时显示耗时；已知总量时才估算剩余时间。
            var time = !processing.Finished && value.Fraction is > 0 and < 1
                ? TimeSpan.FromSeconds(Math.Min(86400 * 365, processing.Elapsed.TotalSeconds * (1 - value.Fraction.Value) / value.Fraction.Value))
                : processing.Elapsed;
            return new Text(time.ToString(@"hh\:mm\:ss")).RightJustified();
        }
        if (processing.Failed && downloadColumn is ProgressBarColumn)
            return new Text("");
        return downloadColumn.Render(options, display, deltaTime);
    }
}
