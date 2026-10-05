using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Tests.Common.Log;

[Collection("Download console")]
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
    public void Write_MidLinePadding_IsPreserved()
    {
        // Spectre 的 RightJustified() 会把单元格补齐到列宽，填充是纯空格写入。
        // 丢弃它会让重定向后各列挤在一起（如 82.68MB236.92KBps00:00:00），
        // 按列解析速度的调用方就会取错字段。
        Assert.Equal("82.68MB   236.92KBps", Capture("82.68MB", "   ", "236.92KBps"));
    }

    [Fact]
    public void Write_LeadingBlankAtLineStart_IsDropped()
    {
        // 行首的空白仍然丢弃，避免输出被缩进或产生整行空白
        Assert.Equal("sample.ts", Capture("\n", "    ", "sample.ts"));
    }

    [Fact]
    public void Write_MultilineText_PreservesBlankLinesAndIndentation()
    {
        const string message = "header:\n  key: value\n\n  end\n";
        Assert.Equal(message, Capture(message));
    }

    [Fact]
    public void Write_RepeatedText_IsPreserved()
    {
        Assert.Equal("sample.ts\nsample.ts\n", Capture("sample.ts\n", "sample.ts\n"));
    }

    [Fact]
    public void Write_EscapeSequences_AreStripped()
    {
        Assert.Equal("sample.ts", Capture($"{(char)27}[31msample.ts{(char)27}[0m"));
    }
}