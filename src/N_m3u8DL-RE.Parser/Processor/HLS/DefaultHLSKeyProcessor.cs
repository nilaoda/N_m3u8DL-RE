using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Util;

using Spectre.Console;

namespace N_m3u8DL_RE.Parser.Processor.HLS
{
    public class DefaultHLSKeyProcessor : KeyProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string m3u8Url, string keyLine, string m3u8Content, ParserConfig paserConfig)
        {
            return extractorType == ExtractorType.HLS;
        }

        public override EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig)
        {
            string iv = ParserUtil.GetAttribute(keyLine, "IV");
            string method = ParserUtil.GetAttribute(keyLine, "METHOD");
            string uri = ParserUtil.GetAttribute(keyLine, "URI");

            Logger.Debug("METHOD:{},URI:{},IV:{}", method, uri, iv);

            EncryptInfo encryptInfo = new(method);

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
                else if (uri.StartsWith("base64:", StringComparison.CurrentCultureIgnoreCase))
                {
                    encryptInfo.Key = Convert.FromBase64String(uri[7..]);
                }
                else if (uri.StartsWith("data:;base64,", StringComparison.CurrentCultureIgnoreCase))
                {
                    encryptInfo.Key = Convert.FromBase64String(uri[13..]);
                }
                else if (uri.StartsWith("data:text/plain;base64,", StringComparison.CurrentCultureIgnoreCase))
                {
                    encryptInfo.Key = Convert.FromBase64String(uri[23..]);
                }
                else if (File.Exists(uri))
                {
                    encryptInfo.Key = File.ReadAllBytes(uri);
                }
                else if (!string.IsNullOrEmpty(uri))
                {
                    int retryCount = parserConfig.KeyRetryCount;
                    string segUrl = PreProcessUrl(ParserUtil.CombineURL(m3u8Url, uri), parserConfig);
                getHttpKey:
                    try
                    {
                        byte[] bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers).Result;
                        encryptInfo.Key = bytes;
                    }
                    catch (Exception _ex) when (!_ex.Message.Contains("scheme is not supported."))
                    {
                        Logger.WarnMarkUp($"[grey]{_ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
                        Thread.Sleep(1000);
                        if (retryCount-- > 0)
                        {
                            goto getHttpKey;
                        }

                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ResString.Cmd_loadKeyFailed + ": " + ex.Message);
                encryptInfo.Method = EncryptMethod.UNKNOWN;
            }

            if (parserConfig.CustomMethod == null)
            {
                return encryptInfo;
            }

            // 处理自定义加密方式
            encryptInfo.Method = parserConfig.CustomMethod.Value;
            Logger.Warn("METHOD changed from {} to {}", method, encryptInfo.Method);

            return encryptInfo;
        }

        /// <summary>
        /// 预处理URL
        /// </summary>
        private static string PreProcessUrl(string url, ParserConfig parserConfig)
        {
            foreach (UrlProcessor p in parserConfig.UrlProcessors)
            {
                if (p.CanProcess(ExtractorType.HLS, url, parserConfig))
                {
                    url = p.Process(url, parserConfig);
                }
            }

            return url;
        }
    }
}