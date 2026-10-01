using System.Diagnostics.CodeAnalysis;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Parser.Constants;
using N_m3u8DL_RE.Parser.Extractor;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Common.Enum;

namespace N_m3u8DL_RE.Parser;

public class StreamExtractor : IDisposable
{
    public ExtractorType ExtractorType => extractor.ExtractorType;
    public WebSourceResult? DirectSource { get; private set; }
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
        DirectSource?.Dispose();
        DirectSource = null;
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
            var result = await HTTPUtil.GetWebSourceResultAsync(url, parserConfig.Headers);
            this.rawText = result.Source;
            if (this.rawText == ResString.ReLiveTs && result.Response?.Content.Headers.ContentLength is not null)
            {
                this.rawText = ResString.ReBinaryData;
            }
            url = result.Url;
            if ((this.rawText == ResString.ReBinaryData || this.rawText == ResString.ReLiveTs) &&
                result.Response != null)
            {
                DirectSource = result;
            }
            else
            {
                result.Dispose();
            }
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

    [MemberNotNull(nameof(rawText), nameof(extractor))]
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
            extractor = new BinaryExtractor(parserConfig);
        }
        else
        {
            throw new NotSupportedException(ResString.notSupported);
        }

        RawFiles[$"raw.{rawType}"] = rawText;
    }

    public void Dispose() => DirectSource?.Dispose();

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

    public async Task RefreshPlayListAsync(List<StreamSpec> streamSpecs, CancellationToken cancellationToken = default, TimeSpan? requestTimeout = null)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            // 直播录制器负责持续重试和停止条件；这里保留原异常，避免网络故障被包装后无法识别。
            await extractor.RefreshPlayListAsync(streamSpecs, cancellationToken, requestTimeout);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
