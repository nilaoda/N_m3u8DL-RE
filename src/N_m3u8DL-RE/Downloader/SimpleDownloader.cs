using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;
using Spectre.Console;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
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
            var dResult = await DownClipAsync(url, savePath, speedContainer, segment.StartRange, segment.StopRange, headers, DownloaderConfig.DownloadRetryCount);
            if (dResult != null && dResult.Success && segment.EncryptInfo != null)
            {
                if (segment.EncryptInfo.Method == EncryptMethod.AES_128)
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    Crypto.AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!);
                }
                else if (segment.EncryptInfo.Method == EncryptMethod.AES_128_ECB)
                {
                    var key = segment.EncryptInfo.Key;
                    var iv = segment.EncryptInfo.IV;
                    Crypto.AESUtil.AES128Decrypt(dResult.ActualFilePath, key!, iv!, System.Security.Cryptography.CipherMode.ECB);
                }
                else if (segment.EncryptInfo.Method == EncryptMethod.SAMPLE_AES_CTR)
                {
                    //throw new NotSupportedException("SAMPLE-AES-CTR");
                }
            }
            return dResult;
        }

        private async Task<DownloadResult?> DownClipAsync(string url, string path, SpeedContainer speedContainer, long? fromPosition, long? toPosition, Dictionary<string, string>? headers = null, int retryCount = 3)
        {
        retry:
            try
            {
                var des = Path.ChangeExtension(path, null);
                //已下载过跳过
                if (File.Exists(des))
                {
                    return new DownloadResult() { ActualContentLength = 0, ActualFilePath = des };
                }
                var result = await DownloadUtil.DownloadToFileAsync(url, path, speedContainer, headers, fromPosition, toPosition);
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
                Logger.WarnMarkUp($"{ex.Message} retryCount: {retryCount}");
                Logger.Debug(ex.ToString());
                if (retryCount-- > 0)
                {
                    await Task.Delay(200);
                    goto retry;
                }
                //throw new Exception("download failed", ex);
                return null;
            }
        }
    }
}
