using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Crypto;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Downloader
{
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

        public async Task<DownloadResult?> DownloadSegmentAsync(MediaSegment segment, string savePath, SpeedContainer speedContainer, Dictionary<string, string>? headers = null)
        {
            var url = segment.Url;
            var dResult = await DownClipAsync(url, savePath, speedContainer, segment.StartRange, segment.StopRange, headers, DownloaderConfig.MyOptions.DownloadRetryCount);
            if (dResult != null && dResult.Success && segment.EncryptInfo != null)
            {
                if (segment.EncryptInfo.Method == EncryptMethod.AES_128)
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!);
                }
                else if (segment.EncryptInfo.Method == EncryptMethod.AES_128_ECB)
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!, System.Security.Cryptography.CipherMode.ECB);
                }
                else if (segment.EncryptInfo.Method == EncryptMethod.CHACHA20)
                {
                    var key = segment.EncryptInfo.Key;
                    var nonce = segment.EncryptInfo.IV;

                    var fileBytes = File.ReadAllBytes(dResult.ActualFilePath);
                    var decrypted = ChaCha20Util.DecryptPer1024Bytes(fileBytes, key!, nonce!);
                    await File.WriteAllBytesAsync(dResult.ActualFilePath, decrypted);
                }
                else if (segment.EncryptInfo.Method == EncryptMethod.SAMPLE_AES_CTR)
                {
                    //throw new NotSupportedException("SAMPLE-AES-CTR");
                }

                //Image头处理
                if (dResult.ImageHeader)
                {
                    await ImageHeaderUtil.ProcessAsync(dResult.ActualFilePath);
                }
                //Gzip解压
                if (dResult.GzipHeader)
                {
                    await OtherUtil.DeGzipFileAsync(dResult.ActualFilePath);
                }
            }
            return dResult;
        }

        private async Task<DownloadResult?> DownClipAsync(string url, string path, SpeedContainer speedContainer, long? fromPosition, long? toPosition, Dictionary<string, string>? headers = null, int retryCount = 3)
        {
            CancellationTokenSource? cancellationTokenSource = null;
        retry:
            try
            {
                cancellationTokenSource = new();
                var des = Path.ChangeExtension(path, null);

                //已下载跳过
                if (File.Exists(des))
                {
                    speedContainer.Add(new FileInfo(des).Length);
                    return new DownloadResult() { ActualContentLength = 0, ActualFilePath = des };
                }

                //已解密跳过
                var dec = Path.Combine(Path.GetDirectoryName(des)!, Path.GetFileNameWithoutExtension(des) + "_dec" + Path.GetExtension(des));
                if (File.Exists(dec))
                {
                    speedContainer.Add(new FileInfo(dec).Length);
                    return new DownloadResult() { ActualContentLength = 0, ActualFilePath = dec };
                }

                //另起线程进行监控
                using var watcher = Task.Factory.StartNew(async () =>
                {
                    while (true)
                    {
                        if (cancellationTokenSource == null || cancellationTokenSource.IsCancellationRequested) break;
                        if (speedContainer.ShouldStop)
                        {
                            cancellationTokenSource.Cancel();
                            Logger.DebugMarkUp("Cancel...");
                            break;
                        }
                        await Task.Delay(500);
                    }
                });

                //调用下载
                var result = await DownloadUtil.DownloadToFileAsync(url, path, speedContainer, cancellationTokenSource, headers, fromPosition, toPosition);
                
                //下载完成后改名
                if (result.Success || !DownloaderConfig.CheckContentLength)
                {
                    File.Move(path, des);
                    result.ActualFilePath = des;
                    return result;
                }
                throw new Exception("please retry");
            }
            catch (Exception ex)
            {
                Logger.DebugMarkUp($"[grey]{ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
                Logger.Debug(url + " " + ex.ToString());
                Logger.Extra($"Ah oh!{Environment.NewLine}RetryCount => {retryCount}{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
                if (retryCount-- > 0)
                {
                    await Task.Delay(1000);
                    goto retry;
                }
                else
                {
                    Logger.Extra($"The retry attempts have been exhausted and the download of this segment has failed.{Environment.NewLine}Exception  => {ex.Message}{Environment.NewLine}Url        => {url}");
                    Logger.WarnMarkUp($"[grey]{ex.Message.EscapeMarkup()}[/]");
                }
                //throw new Exception("download failed", ex);
                return null;
            }
            finally
            {
                if (cancellationTokenSource != null)
                {
                    //调用后销毁
                    cancellationTokenSource.Dispose();
                    cancellationTokenSource = null;
                }
            }
        }
    }
}
