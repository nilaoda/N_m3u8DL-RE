using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Util;
using Spectre.Console;

namespace N_m3u8DL_RE.Parser.Processor.HLS;

public class DefaultHLSKeyProcessor : KeyProcessor
{
    private readonly ConcurrentDictionary<(string Url, EncryptMethod Method), byte[]> KeyCache = new();

    public override bool CanProcess(ExtractorType extractorType, string m3u8Url, string keyLine, string m3u8Content, ParserConfig paserConfig) => extractorType == ExtractorType.HLS;


    public override EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig)
        => Process(keyLine, m3u8Url, m3u8Content, parserConfig, default, liveRefresh: false);

    public override EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig,
        CancellationToken cancellationToken, TimeSpan? requestTimeout = null)
        => Process(keyLine, m3u8Url, m3u8Content, parserConfig, cancellationToken, liveRefresh: true, requestTimeout: requestTimeout);

    private EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig,
        CancellationToken cancellationToken, bool liveRefresh, TimeSpan? requestTimeout = null)
    {
        var iv = ParserUtil.GetAttribute(keyLine, "IV");
        var method = ParserUtil.GetAttribute(keyLine, "METHOD");
        var uri = ParserUtil.GetAttribute(keyLine, "URI");

        Logger.Debug("METHOD:{},URI:{},IV:{}", method, uri, iv);

        var encryptInfo = new EncryptInfo(method);

        if (encryptInfo.Method == EncryptMethod.NONE && parserConfig.CustomMethod == null)
            return encryptInfo;

        // IV
        if (!string.IsNullOrEmpty(iv))
        {
            encryptInfo.IV = HexUtil.HexToBytes(iv);
        }
        // 自定义IV
        if (parserConfig.CustomeIV is { Length: > 0 }) 
        {
            encryptInfo.IV = parserConfig.CustomeIV;
        }

        // KEY
        try
        {
            if (parserConfig.CustomeKey is { Length: > 0 })
            {
                encryptInfo.Key = parserConfig.CustomeKey;
            }
            else if (uri.ToLower().StartsWith("base64:"))
            {
                encryptInfo.Key = Convert.FromBase64String(uri[7..]);
            }
            else if (uri.ToLower().StartsWith("data:;base64,"))
            {
                encryptInfo.Key = Convert.FromBase64String(uri[13..]);
            }
            else if (uri.ToLower().StartsWith("data:text/plain;base64,"))
            {
                encryptInfo.Key = Convert.FromBase64String(uri[23..]);
            }
            else if (File.Exists(uri))
            {
                encryptInfo.Key = File.ReadAllBytes(uri);
            }
            else if (!string.IsNullOrEmpty(uri))
            {
                var segUrl = PreProcessUrl(ParserUtil.CombineURL(m3u8Url, uri), parserConfig);
                var cacheKey = (Url: segUrl, Method: parserConfig.CustomMethod ?? encryptInfo.Method);
                if (KeyCache.TryGetValue(cacheKey, out var cachedKey))
                {
                    encryptInfo.Key = cachedKey;
                    goto keyDone;
                }

                // 直播 key 失败后重刷最新清单，避免在一个旧 key 上重试而阻塞其他轨道刷新。
                var retryCount = liveRefresh ? 0 : parserConfig.KeyRetryCount;
                getHttpKey:
                try
                {
                    var bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers, cancellationToken, requestTimeout).GetAwaiter().GetResult();
                    KeyCache[cacheKey] = bytes;
                    encryptInfo.Key = bytes;
                }
                catch (Exception _ex) when (!_ex.Message.Contains("scheme is not supported."))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 直播由录制器统一提示断网和恢复，避免持续重试时每次都刷屏。
                    if (!liveRefresh)
                        Logger.WarnMarkUp($"[grey]{_ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
                    if (retryCount-- > 0)
                    {
                        Task.Delay(1000, cancellationToken).GetAwaiter().GetResult();
                        goto getHttpKey;
                    }
                    throw;
                }
            }
            keyDone:;
        }
        // 直播刷新保留网络异常交给录制器重试，避免把新 key 下载失败误判为未知加密。
        catch (Exception ex) when (!liveRefresh ||
            (!RetryUtil.IsTransientNetworkError(ex) && ex is not HttpRequestException))
        {
            Logger.Error(ResString.cmd_loadKeyFailed + ": " + ex.Message);
            encryptInfo.Method = EncryptMethod.UNKNOWN;
        }

        if (parserConfig.CustomMethod == null) return encryptInfo;
        
        // 处理自定义加密方式
        encryptInfo.Method = parserConfig.CustomMethod.Value;
        Logger.Warn("METHOD changed from {} to {}", method, encryptInfo.Method);

        return encryptInfo;
    }

    /// <summary>
    /// 预处理URL
    /// </summary>
    private string PreProcessUrl(string url, ParserConfig parserConfig)
    {
        foreach (var p in parserConfig.UrlProcessors)
        {
            if (p.CanProcess(ExtractorType.HLS, url, parserConfig))
            {
                url = p.Process(url, parserConfig);
            }
        }

        return url;
    }
}
