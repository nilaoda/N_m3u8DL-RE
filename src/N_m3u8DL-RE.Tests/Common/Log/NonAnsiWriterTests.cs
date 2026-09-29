using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Tests.Common.Log;

public class NonAnsiWriterTests
{
    private static string Capture(params string[] values)
    {
        var original = Console.Out;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            var writer = new NonAnsiWriter();
            foreach (var value in values)
            {
                writer.Write(value);
            }
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Write_LineFeed_AfterContent_TerminatesLine()
    {
        // 渲染管线写出的换行（Spectre.Console 0.57 起换行是独立写出）不能被丢弃，
        // 否则重定向时 stdout 全程没有行分隔，按行读取输出的调用方要等到进程退出才拿到数据
        Assert.Equal("sample.ts\n", Capture("sample.ts", "\n"));
    }

    [Fact]
    public void Write_LineFeed_AtLineStart_IsSkipped()
    {
        Assert.Equal("", Capture("\n"));
    }

    [Fact]
    public void Write_ConsecutiveLineFeeds_AreCollapsed()
    {
        // 渲染帧上下各有留白，连续换行只落一次，避免输出成片的空白行
        Assert.Equal("sample.ts\n", Capture("sample.ts", "\n", "\r\n"));
    }

    [Fact]
    public void Write_WindowsNewLine_IsNormalizedToLineFeed()
    {
        Assert.Equal("sample.ts\n", Capture("sample.ts", "\r\n"));
    }

    [Fact]
    public void Write_BlankOnly_IsDropped()
    {
        Assert.Equal("sample.ts", Capture("sample.ts", "    "));
    }

    [Fact]
    public void Write_EscapeSequences_AreStripped()
    {
        Assert.Equal("sample.ts", Capture($"{(char)27}[31msample.ts{(char)27}[0m"));
    }
}