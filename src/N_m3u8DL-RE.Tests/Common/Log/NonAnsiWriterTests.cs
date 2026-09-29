using N_m3u8DL_RE.Common.Log;

namespace N_m3u8DL_RE.Tests.Common.Log;

public class NonAnsiWriterTests
{
    private static string Capture(Action<NonAnsiWriter> write)
    {
        var original = Console.Out;
        var sw = new StringWriter();
        try
        {
            Console.SetOut(sw);
            write(new NonAnsiWriter());
            return sw.ToString();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Write_LineFeedOnly_IsPreserved()
    {
        // 渲染管线写出的纯换行（Spectre.Console 0.57 起换行是独立写出）不能被丢弃，
        // 否则重定向时 stdout 全程没有行分隔，按行读取输出的调用方要等到进程退出才拿到数据
        var output = Capture(w => w.Write("\n"));

        Assert.Equal("\n", output);
    }

    [Fact]
    public void Write_WindowsNewLine_IsNormalizedToLineFeed()
    {
        var output = Capture(w => w.Write("\r\n"));

        Assert.Equal("\n", output);
    }

    [Fact]
    public void Write_BlankOnly_IsDropped()
    {
        var output = Capture(w => w.Write("    "));

        Assert.Equal("", output);
    }

    [Fact]
    public void Write_EscapeSequences_AreStripped()
    {
        var output = Capture(w => w.Write($"{(char)27}[31msample.ts{(char)27}[0m"));

        Assert.Equal("sample.ts", output);
    }
}