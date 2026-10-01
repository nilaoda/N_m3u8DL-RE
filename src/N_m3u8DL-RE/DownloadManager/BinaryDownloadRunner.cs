using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using N_m3u8DL_RE.Column;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.DownloadManager;

/// <summary>
/// 处理二进制直链的输出文件名、命令行选项和下载进度。
/// </summary>
internal static class BinaryDownloadRunner
{
    public static async Task RunAsync(MyOption option, WebSourceResult source, Dictionary<string, string> headers)
    {
        if (option.SkipDownload)
        {
            return;
        }
        ValidateOptions(option);

        var fileName = GetFileName(option, source);
        var savePath = Path.Combine(option.SaveDir ?? Environment.CurrentDirectory, fileName);
        Logger.Info(ResString.saveName + savePath);
        if (File.Exists(savePath))
        {
            Logger.Warn($"File already exists, skipping download: {savePath}");
            return;
        }

        var downloader = new BinaryDownloadManager(readTimeout: TimeSpan.FromSeconds(option.HttpRequestTimeout));
        var length = source.Response?.Content.Headers.ContentLength;
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            await DownloadWithLogAsync(downloader, option, source, savePath, headers, length);
        }
        else
        {
            await DownloadWithProgressAsync(downloader, option, source, savePath, headers, length);
        }
        Logger.InfoMarkUp("[white on green]Done[/]");
    }

    private static void ValidateOptions(MyOption option)
    {
        if (option.CustomRange != null || option.MuxAfterDone || option.MuxImports is { Count: > 0 } ||
            option.Keys is { Length: > 0 } || option.KeyTextFile != null || option.SavePattern != null)
        {
            throw new ArgumentException("Range, mux, decryption, and save-pattern options do not apply to binary direct downloads.");
        }
    }

    private static string GetFileName(MyOption option, WebSourceResult source)
    {
        var sourceName = source.Response?.Content.Headers.ContentDisposition?.FileNameStar ??
                         source.Response?.Content.Headers.ContentDisposition?.FileName?.Trim('"') ??
                         Uri.UnescapeDataString(new Uri(source.Url).AbsolutePath).Split('/').LastOrDefault();
        sourceName = Path.GetFileName(sourceName?.Replace('\\', '/') ?? "");
        sourceName = OtherUtil.GetValidFileName(sourceName ?? "");
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = "download.bin";
        }
        var extension = Path.GetExtension(sourceName);
        if (string.IsNullOrEmpty(extension))
        {
            extension = source.Response?.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() switch
            {
                "video/mp4" or "audio/mp4" => ".mp4",
                "video/mp2t" => ".ts",
                _ => ".bin"
            };
            sourceName += extension;
        }
        var fileName = string.IsNullOrWhiteSpace(option.SaveName) ? sourceName :
            OtherUtil.GetValidFileName(option.SaveName) + (Path.HasExtension(option.SaveName) ? "" : extension);
        var fileExtension = Path.GetExtension(fileName);
        if (Encoding.UTF8.GetByteCount(fileExtension) > 32)
        {
            fileName = Path.GetFileNameWithoutExtension(fileName) + ".bin";
            fileExtension = ".bin";
        }
        return OtherUtil.GetSafeFileName(Path.GetFileNameWithoutExtension(fileName), fileExtension, maxBytes: 200);
    }

    private static async Task DownloadWithLogAsync(BinaryDownloadManager downloader, MyOption option,
        WebSourceResult source, string savePath, Dictionary<string, string> headers, long? length)
    {
        var watch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        long intervalBytes = 0;
        long downloadedBytes = 0;
        var reportLock = new object();
        void ReportReceived(int bytes)
        {
            lock (reportLock)
            {
                intervalBytes += bytes;
                var elapsed = watch.Elapsed - lastReport;
                if (elapsed < TimeSpan.FromSeconds(1))
                {
                    return;
                }
                Logger.Info($"{FormatSize(Interlocked.Read(ref downloadedBytes), length)} " +
                            $"{GlobalUtil.FormatFileSize(intervalBytes / elapsed.TotalSeconds)}ps");
                lastReport = watch.Elapsed;
                intervalBytes = 0;
            }
        }

        await downloader.DownloadAsync(source, savePath, headers, option.ThreadCount, option.DownloadRetryCount,
            maxSpeed: option.MaxSpeed, onReceived: ReportReceived,
            onDownloaded: downloaded => Interlocked.Exchange(ref downloadedBytes, downloaded));
        var finalInterval = watch.Elapsed - lastReport;
        var finalSpeed = finalInterval > TimeSpan.Zero ? intervalBytes / finalInterval.TotalSeconds : 0;
        Logger.Info($"{FormatSize(downloadedBytes, length)} {GlobalUtil.FormatFileSize(finalSpeed)}ps");
    }

    private static async Task DownloadWithProgressAsync(BinaryDownloadManager downloader, MyOption option,
        WebSourceResult source, string savePath, Dictionary<string, string> headers, long? length)
    {
        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = option.LogLevel != LogLevel.OFF;
        var sizes = new ConcurrentDictionary<int, long>();
        long receivedBytes = 0;
        var columns = new List<ProgressColumn> { new TaskDescriptionColumn() { Alignment = Justify.Left } };
        if (length is > 0)
        {
            columns.Add(new ProgressBarColumn() { Width = 30 });
            columns.Add(new PercentageColumn());
        }
        columns.Add(new BinaryDownloadSizeColumn(sizes, length));
        columns.Add(new BinaryDownloadSpeedColumn(() => Interlocked.Read(ref receivedBytes)));
        if (!option.NoAnsiColor)
        {
            columns.Add(new SpinnerColumn());
        }
        progress.Columns(columns.ToArray());
        await progress.StartAsync(async context =>
        {
            var task = context.AddTask(Path.GetFileName(savePath), maxValue: length ?? 1);
            sizes[task.Id] = 0;
            await downloader.DownloadAsync(source, savePath, headers, option.ThreadCount, option.DownloadRetryCount,
                maxSpeed: option.MaxSpeed, onReceived: bytes => Interlocked.Add(ref receivedBytes, bytes),
                onDownloaded: downloaded =>
                {
                    sizes[task.Id] = downloaded;
                    if (length is > 0)
                    {
                        task.Value = downloaded;
                    }
                });
            task.StopTask();
        });
    }

    private static string FormatSize(long downloaded, long? length)
    {
        return length is >= 0
            ? $"{GlobalUtil.FormatFileSize(downloaded)}/{GlobalUtil.FormatFileSize(length.Value)}"
            : GlobalUtil.FormatFileSize(downloaded);
    }
}
