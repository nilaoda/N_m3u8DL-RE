using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Crypto;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Downloader;

/// <summary>
/// 简单下载器
/// </summary>
internal class SimpleDownloader : IDownloader
{
    DownloaderConfig DownloaderConfig;

    public SimpleDownloader(DownloaderConfig config)
    {
        DownloaderConfig = config;
    }

    public async Task<DownloadResult?> DownloadSegmentAsync(MediaSegment segment, string savePath, SpeedContainer speedContainer, Dictionary<string, string>? headers = null, bool singleFile = false, CancellationToken cancellationToken = default, bool throwOnFailure = false, TimeSpan? networkTimeout = null)
    {
        var url = segment.Url;
        var (des, dResult) = await DownClipAsync(url, savePath, speedContainer, segment.StartRange, segment.StopRange, headers, DownloaderConfig.MyOptions.DownloadRetryCount, singleFile, cancellationToken, throwOnFailure, networkTimeout);
        if (dResult is { Success: true } && dResult.ActualFilePath != des)
        {
            switch (segment.EncryptInfo.Method)
            {
                case EncryptMethod.AES_128:
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!);
                    break;
                }
                case EncryptMethod.AES_128_ECB:
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!, System.Security.Cryptography.CipherMode.ECB);
                    break;
                }
                case EncryptMethod.CHACHA20:
                {
                    var key = segment.EncryptInfo.Key;
                    var nonce = segment.EncryptInfo.IV;

                    var fileBytes = File.ReadAllBytes(dResult.ActualFilePath);
                    var decrypted = ChaCha20Util.DecryptPer1024Bytes(fileBytes, key!, nonce!);
                    await File.WriteAllBytesAsync(dResult.ActualFilePath, decrypted);
                    break;
                }
                case EncryptMethod.SAMPLE_AES_CTR:
                    // throw new NotSupportedException("SAMPLE-AES-CTR");
                    break;
            }

            // Image头处理
            if (dResult.ImageHeader)
            {
                await ImageHeaderUtil.ProcessAsync(dResult.ActualFilePath);
            }
            // Gzip解压
            if (dResult.GzipHeader)
            {
                await OtherUtil.DeGzipFileAsync(dResult.ActualFilePath);
            }

            // 处理完成后改名
            File.Move(dResult.ActualFilePath, des);
            dResult.ActualFilePath = des;
        }
        return dResult;
    }

    private async Task<(string des, DownloadResult? dResult)> DownClipAsync(string url, string path, SpeedContainer speedContainer, long? fromPosition, long? toPosition, Dictionary<string, string>? headers = null, int retryCount = 3, bool singleFile = false, CancellationToken cancellationToken = default, bool throwOnFailure = false, TimeSpan? networkTimeout = null)
    {
        CancellationTokenSource? cancellationTokenSource = null;
        Task? watcher = null;
        var binaryStarted = false;
        retry:
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var des = Path.ChangeExtension(path, null);

            // 已下载跳过
            if (File.Exists(des))
            {
                speedContainer.Add(new FileInfo(des).Length);
                return (des, new DownloadResult() { ActualContentLength = 0, ActualFilePath = des });
            }

            // 已解密跳过
            var dec = Path.Combine(Path.GetDirectoryName(des)!, Path.GetFileNameWithoutExtension(des) + "_dec" + Path.GetExtension(des));
            if (File.Exists(dec))
            {
                speedContainer.Add(new FileInfo(dec).Length);
                return (dec, new DownloadResult() { ActualContentLength = 0, ActualFilePath = dec });
            }

            if (singleFile)
            {
                // .tmp 可能来自旧流程的失败下载；新下载器只从有身份校验的 .downloading 续传。
                File.Delete(path);
                var timeout = DownloaderConfig.MyOptions.HttpRequestTimeout;
                var downloader = new BinaryDownloadManager(readTimeout: timeout > 0 ? TimeSpan.FromSeconds(timeout) : null);
                var baseDownloaded = speedContainer.RDownloaded;
                long credited = 0;
                long? length = null;
                await downloader.DownloadAsync(url, path, headers ?? [], DownloaderConfig.MyOptions.ThreadCount, retryCount,
                    onLength: value =>
                    {
                        binaryStarted = true;
                        length = value;
                        speedContainer.ResponseLength = value == null ? null : baseDownloaded + value;
                    },
                    onReceived: bytes => speedContainer.AddReceived(bytes),
                    onDownloaded: bytes =>
                    {
                        // Range 块失败及顺序下载重启都会回退有效字节，不把重传累计为进度。
                        speedContainer.AddDownloaded(bytes - credited);
                        credited = bytes;
                    },
                    cancellationToken: cancellationTokenSource.Token, maxSpeed: DownloaderConfig.MyOptions.MaxSpeed);
                using var input = File.OpenRead(path);
                var prefix = new byte[Math.Min(16 * 1024, input.Length)];
                await input.ReadExactlyAsync(prefix);
                return (des, new DownloadResult
                {
                    ActualFilePath = path, ActualContentLength = input.Length, RespContentLength = length,
                    ImageHeader = ImageHeaderUtil.IsImageHeader(prefix),
                    GzipHeader = prefix.Length > 2 && prefix[0] == 0x1f && prefix[1] == 0x8b,
                });
            }

            // 直播按每片读取独立检测超时，点播保留原有的整条流零速监控。
            if (networkTimeout == null)
            {
                var cts = cancellationTokenSource;
                watcher = Task.Run(async () =>
                {
                    try
                    {
                        while (!cts.IsCancellationRequested)
                        {
                            if (speedContainer.ShouldStop)
                            {
                                cts.Cancel();
                                Logger.DebugMarkUp(ResString.downloadCancelled);
                                break;
                            }
                            await Task.Delay(500, cts.Token);
                        }
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                });
            }

            // 调用下载
            var result = await DownloadUtil.DownloadToFileAsync(url, path, speedContainer, cancellationTokenSource, headers, fromPosition, toPosition, networkTimeout);
            return (des, result);

            throw new Exception("please retry");
        }
        catch (Exception ex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Logger.DebugMarkUp($"[grey]{ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
            Logger.Debug(url + " " + ex);
            Logger.Extra($"Ah oh!{Environment.NewLine}RetryCount => {retryCount}{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
            // 整文件的正文重试由 BinaryDownloadManager 负责，不能在外层再次重下。
            if (!binaryStarted && retryCount-- > 0 && (!throwOnFailure || RetryUtil.IsTransientNetworkError(ex)))
            {
                await Task.Delay(1000, cancellationToken);
                goto retry;
            }
            else
            {
                Logger.Extra($"The retry attempts have been exhausted and the download of this segment has failed.{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
                // 直播的临时故障由外层统一提示，诊断信息仍保留在详细日志中。
                if (!throwOnFailure || !RetryUtil.IsTransientNetworkError(ex))
                    Logger.WarnMarkUp($"[grey]{ex.Message.EscapeMarkup()}[/]");
            }
            // 直播需要原始异常区分临时网络故障；点播仍沿用失败时返回空结果的行为。
            if (throwOnFailure)
                throw;
            return default;
        }
        finally
        {
            if (cancellationTokenSource != null)
            {
                // 快速下载可能在监控任务启动前完成，不能 Dispose 尚未完成的 Task。
                // 先取消并等待监控退出，再销毁 CTS，避免竞态导致成功下载被误判失败。
                cancellationTokenSource.Cancel();
                if (watcher != null)
                    await watcher;
                watcher = null;
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
            }
        }
    }
}