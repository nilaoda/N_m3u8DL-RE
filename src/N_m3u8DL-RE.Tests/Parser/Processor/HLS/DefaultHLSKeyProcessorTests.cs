using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Parser.Processor.HLS;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Parser.Processor.HLS;

public class DefaultHLSKeyProcessorTests
{
    [Fact]
    public void Process_CachesByUrlAndMethodWithinProcessor()
    {
        var keyPath = Path.GetTempFileName();
        try
        {
            var firstKey = Enumerable.Repeat((byte)1, 16).ToArray();
            var secondKey = Enumerable.Repeat((byte)2, 16).ToArray();
            File.WriteAllBytes(keyPath, firstKey);

            var keyUri = new Uri(keyPath).AbsoluteUri;
            var processor = new DefaultHLSKeyProcessor();
            var config = new ParserConfig();

            Process(processor, config, keyUri, "AES-128", "0x01").Key.ShouldBe(firstKey);

            File.WriteAllBytes(keyPath, secondKey);

            // URL 和加密方式相同时，更换 IV 应复用密钥。
            Process(processor, config, keyUri, "AES-128", "0x02").Key.ShouldBe(firstKey);
            Process(processor, config, keyUri, "SAMPLE-AES", "0x02").Key.ShouldBe(secondKey);

            // 新的解析任务应重新读取当前密钥。
            Process(new DefaultHLSKeyProcessor(), new ParserConfig(), keyUri, "AES-128", "0x02")
                .Key.ShouldBe(secondKey);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    private static N_m3u8DL_RE.Common.Entity.EncryptInfo Process(
        DefaultHLSKeyProcessor processor, ParserConfig config, string keyUri, string method, string iv)
    {
        var keyLine = $"#EXT-X-KEY:METHOD={method},URI=\"{keyUri}\",IV={iv}";
        return processor.Process(keyLine, "https://example.com/playlist.m3u8", string.Empty, config);
    }
}
