using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Config;

namespace N_m3u8DL_RE.Parser.Processor;

public abstract class KeyProcessor
{
    public abstract bool CanProcess(ExtractorType extractorType, string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
    public abstract EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig);
    // 直播刷新可传入取消信号；自定义处理器继续兼容原有入口。
    public virtual EncryptInfo Process(string keyLine, string m3u8Url, string m3u8Content, ParserConfig parserConfig,
        CancellationToken cancellationToken, TimeSpan? requestTimeout = null) => Process(keyLine, m3u8Url, m3u8Content, parserConfig);
}