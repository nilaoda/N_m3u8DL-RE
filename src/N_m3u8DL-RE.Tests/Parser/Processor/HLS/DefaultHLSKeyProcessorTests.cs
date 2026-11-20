using N_m3u8DL_RE.Common.Enum;
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

    private static readonly byte[] Aes128Key = Enumerable.Range(0xA0, 16).Select(i => (byte)i).ToArray();

    public static TheoryData<string> TextKeyResponses => new()
    {
        "a0a1a2a3a4a5a6a7a8a9aaabacadaeaf",
        "A0A1A2A3A4A5A6A7A8A9AAABACADAEAF\n",
        "0xa0a1a2a3a4a5a6a7a8a9aaabacadaeaf",
        "oKGio6SlpqeoqaqrrK2urw==",
        "oKGio6SlpqeoqaqrrK2urw\r\n",
    };

    [Theory]
    [MemberData(nameof(TextKeyResponses))]
    public void Process_DecodesAes128KeyReturnedAsText(string response)
    {
        WithKeyFile(System.Text.Encoding.ASCII.GetBytes(response), keyUri =>
        {
            var info = Process(new DefaultHLSKeyProcessor(), new ParserConfig(), keyUri, "AES-128", "0x01");
            info.Method.ShouldBe(EncryptMethod.AES_128);
            info.Key.ShouldBe(Aes128Key);
        });
    }

    [Fact]
    public void Process_KeepsRaw16ByteKeyEvenWhenPrintable()
    {
        // 16 字节即为原始密钥，即使恰好全是可打印字符也不能再按文本解码。
        var key = System.Text.Encoding.ASCII.GetBytes("0123456789abcdef");
        WithKeyFile(key, keyUri =>
            Process(new DefaultHLSKeyProcessor(), new ParserConfig(), keyUri, "AES-128", "0x01").Key.ShouldBe(key));
    }

    [Theory]
    [InlineData(86)]
    [InlineData(32)]
    [InlineData(15)]
    public void Process_RejectsAes128KeyWithInvalidLength(int length)
    {
        // 无效 key 必须在解析清单时终止，不能当作未知加密继续下载。
        WithKeyFile(InvalidKey(length), keyUri =>
            Should.Throw<InvalidHLSKeyException>(() =>
                Process(new DefaultHLSKeyProcessor(), new ParserConfig(), keyUri, "AES-128", "0x01")));
    }

    [Fact]
    public void Process_RejectsInvalidKeyWithCustomAes128Method()
    {
        WithKeyFile(InvalidKey(86), keyUri =>
            Should.Throw<InvalidHLSKeyException>(() =>
                Process(new DefaultHLSKeyProcessor(), new ParserConfig { CustomMethod = EncryptMethod.AES_128 },
                    keyUri, "SAMPLE-AES", "0x01")));
    }

    [Fact]
    public void Process_DoesNotCacheInvalidKey()
    {
        var keyPath = Path.GetTempFileName();
        try
        {
            var keyUri = new Uri(keyPath).AbsoluteUri;
            var processor = new DefaultHLSKeyProcessor();
            var config = new ParserConfig();
            File.WriteAllBytes(keyPath, InvalidKey(86));
            Should.Throw<InvalidHLSKeyException>(() => Process(processor, config, keyUri, "AES-128", "0x01"));

            // 同一处理器再次请求时应重新获取，而不是复用被拒绝的 key。
            File.WriteAllBytes(keyPath, Aes128Key);
            Process(processor, config, keyUri, "AES-128", "0x01").Key.ShouldBe(Aes128Key);
        }
        finally
        {
            File.Delete(keyPath);
        }
    }

    [Theory]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,IV=0x01", false)]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,IV=0x01", true)]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,URI=\"base64:!\",IV=0x01", false)]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,URI=\"base64:!\",IV=0x01", true)]
    [InlineData("#EXT-X-KEY:METHOD=AES-128,URI=\"data:;base64,!\",IV=0x01", false)]
    public void Process_RejectsMissingOrMalformedInlineAes128Key(string keyLine, bool customMethod)
    {
        // 缺少 URI 或内联 Base64 无效时同样无法解密，必须在解析清单时终止。
        var config = new ParserConfig { CustomMethod = customMethod ? EncryptMethod.AES_128 : null };
        Should.Throw<InvalidHLSKeyException>(() =>
            new DefaultHLSKeyProcessor().Process(keyLine, "https://example.com/playlist.m3u8", string.Empty, config));
    }

    [Fact]
    public void Process_DecodesValidInlineBase64Key()
    {
        var info = new DefaultHLSKeyProcessor().Process(
            "#EXT-X-KEY:METHOD=AES-128,URI=\"base64:oKGio6SlpqeoqaqrrK2urw==\",IV=0x01",
            "https://example.com/playlist.m3u8", string.Empty, new ParserConfig());
        info.Key.ShouldBe(Aes128Key);
    }

    private static byte[] InvalidKey(int length) =>
        Enumerable.Range(0, length).Select(i => (byte)(i * 37 % 256)).ToArray();

    [Fact]
    public void Process_DoesNotNormalizeNonAesKeys()
    {
        // ChaCha20 等算法使用 32 字节密钥，不能套用 AES-128 的长度校验。
        var key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        WithKeyFile(key, keyUri =>
        {
            var config = new ParserConfig { CustomMethod = EncryptMethod.CHACHA20 };
            var info = Process(new DefaultHLSKeyProcessor(), config, keyUri, "AES-128", "0x01");
            info.Method.ShouldBe(EncryptMethod.CHACHA20);
            info.Key.ShouldBe(key);
        });
    }

    private static void WithKeyFile(byte[] content, Action<string> action)
    {
        var keyPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(keyPath, content);
            action(new Uri(keyPath).AbsoluteUri);
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
