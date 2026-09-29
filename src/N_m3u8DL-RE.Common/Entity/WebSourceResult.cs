namespace N_m3u8DL_RE.Common.Entity;

/// <summary>
/// 加载 URL 的结果。二进制内容和 HTTP 直播 TS 保留首次请求的响应流及已读取的前缀。
/// </summary>
public sealed class WebSourceResult(string source, string url, HttpResponseMessage? response = null, Stream? stream = null, byte[]? prefix = null) : IDisposable
{
    public string Source { get; } = source;
    public string Url { get; } = url;
    public HttpResponseMessage? Response { get; } = response;
    public Stream? Stream { get; } = stream;
    public byte[] Prefix { get; } = prefix ?? [];

    public void Dispose() => Response?.Dispose();
}
