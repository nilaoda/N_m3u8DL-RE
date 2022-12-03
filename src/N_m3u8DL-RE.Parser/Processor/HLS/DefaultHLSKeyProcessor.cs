using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Util;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Parser.Processor.HLS
{
    public class DefaultHLSKeyProcessor : KeyProcessor
    {
        public override bool CanProcess(ExtractorType extractorType, string m3u8Url, string keyLine, string m3u8Content, ParserConfig paserConfig) => extractorType == ExtractorType.HLS;


        public override EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig)
        {
            var iv = ParserUtil.GetAttribute(keyLine, "IV");
            var method = ParserUtil.GetAttribute(keyLine, "METHOD");
            var uri = ParserUtil.GetAttribute(keyLine, "URI");

            Logger.Debug("METHOD:{},URI:{},IV:{}", method, uri, iv);

            var encryptInfo = new EncryptInfo(method);

            //IV
            if (!string.IsNullOrEmpty(iv))
            {
                encryptInfo.IV = HexUtil.HexToBytes(iv);
            }
            //自定义IV
            if (parserConfig.CustomeIV != null && parserConfig.CustomeIV.Length > 0) 
            {
                encryptInfo.IV = parserConfig.CustomeIV;
            }

            //KEY
            try
            {
                if (parserConfig.CustomeKey != null && parserConfig.CustomeKey.Length > 0)
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
                    var retryCount = parserConfig.KeyRetryCount;
                    var segUrl = PreProcessUrl(ParserUtil.CombineURL(m3u8Url, uri), parserConfig);
                getHttpKey:
                    try
                    {
                        var bytes = HTTPUtil.GetBytesAsync(segUrl, parserConfig.Headers).Result;
                        encryptInfo.Key = bytes;
                    }
                    catch (Exception _ex) when (!_ex.Message.Contains("scheme is not supported."))
                    {
                        Logger.WarnMarkUp($"[grey]{_ex.Message.EscapeMarkup()} retryCount: {retryCount}[/]");
                        Thread.Sleep(1000);
                        if (retryCount-- > 0) goto getHttpKey;
                        else throw;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ResString.cmd_loadKeyFailed + ": " + ex.Message);
                encryptInfo.Method = EncryptMethod.UNKNOWN;
            }

            //处理自定义加密方式
            if (parserConfig.CustomMethod != null)
            {
                encryptInfo.Method = parserConfig.CustomMethod.Value;
                Logger.Warn("METHOD changed from {} to {}", method, encryptInfo.Method);
            }

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
}
