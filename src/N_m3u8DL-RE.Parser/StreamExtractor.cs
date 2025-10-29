﻿using System.Diagnostics.CodeAnalysis;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.Parser;

public class StreamExtractor
{
    public ExtractorType ExtractorType => extractor.ExtractorType;
    private IExtractor extractor;
    private ParserConfig parserConfig = new();
    private string rawText;
    private static SemaphoreSlim semaphore = new(1, 1);

    public Dictionary<string, string> RawFiles { get; set; } = new(); // 存储（文件名,文件内容）

    public StreamExtractor(ParserConfig parserConfig)
    {
        this.parserConfig = parserConfig;
    }

    public async Task LoadSourceFromUrlAsync(string url)
    {
        Logger.Info(ResString.loadingUrl + url);
        if (url.StartsWith("file:"))
        {
            var uri = new Uri(url);
            this.rawText = await File.ReadAllTextAsync(uri.LocalPath);
            parserConfig.OriginalUrl = parserConfig.Url = url;
        }
        else if (url.StartsWith("http"))
        {
            parserConfig.OriginalUrl = url;
            (this.rawText, url) = await HTTPUtil.GetWebSourceAndNewUrlAsync(url, parserConfig.Headers);
            parserConfig.Url = url;
        }
        else if (File.Exists(url))
        {
            url = Path.GetFullPath(url);
            this.rawText = await File.ReadAllTextAsync(url);
            parserConfig.OriginalUrl = parserConfig.Url = new Uri(url).AbsoluteUri;
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new Exception(ResString.loadUrlFailed);
        }
        
        this.rawText = rawText.Trim();
        LoadSourceFromText(this.rawText);
    }

    [MemberNotNull(nameof(this.rawText), nameof(this.extractor))]
    private void LoadSourceFromText(string rawText)
    {
        var rawType = "txt";
        rawText = rawText.Trim();
        this.rawText = rawText;
        if (rawText.StartsWith(HLSTags.ext_m3u))
        {
            Logger.InfoMarkUp(ResString.matchHLS);
            extractor = new HLSExtractor(parserConfig);
            rawType = "m3u8";
        }
        else if (rawText.Contains("</MPD>") && rawText.Contains("<MPD"))
        {
            Logger.InfoMarkUp(ResString.matchDASH);
            // extractor = new DASHExtractor(parserConfig);
            extractor = new DASHExtractor2(parserConfig);
            rawType = "mpd";
        }
        else if (rawText.Contains("</SmoothStreamingMedia>") && rawText.Contains("<SmoothStreamingMedia"))
        {
            Logger.InfoMarkUp(ResString.matchMSS);
            // extractor = new DASHExtractor(parserConfig);
            extractor = new MSSExtractor(parserConfig);
            rawType = "ism";
        }
        else if (rawText == ResString.ReLiveTs)
        {
            Logger.InfoMarkUp(ResString.matchTS);
            extractor = new LiveTSExtractor(parserConfig);
        }
        else if (rawText == ResString.ReBinaryData)
        {
            Logger.InfoMarkUp(ResString.matchBinaryData);
            throw new NotSupportedException(ResString.notSupported);
        }
        else
        {
            throw new NotSupportedException(ResString.notSupported);
        }

        RawFiles[$"raw.{rawType}"] = rawText;
    }

    /// <summary>
    /// 开始解析流媒体信息
    /// </summary>
    /// <returns></returns>
    public async Task<List<StreamSpec>> ExtractStreamsAsync()
    {
        try
        {
            await semaphore.WaitAsync();
            Logger.Info(ResString.parsingStream);
            return await extractor.ExtractStreamsAsync(rawText);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    /// 根据规格说明填充媒体播放列表信息
    /// </summary>
    /// <param name="streamSpecs"></param>
    public async Task FetchPlayListAsync(List<StreamSpec> streamSpecs)
    {
        try
        {
            await semaphore.WaitAsync();
            Logger.Info(ResString.parsingStream);
            await extractor.FetchPlayListAsync(streamSpecs);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs)
    {
        try
        {
            await semaphore.WaitAsync();
            await RetryUtil.WebRequestRetryAsync(async () =>
            {
                await extractor.RefreshPlayListAsync(streamSpecs);
                return true;
            }, retryDelayMilliseconds: 1000, maxRetries: 5);
        }
        finally
        {
            semaphore.Release();
        }
    }
}