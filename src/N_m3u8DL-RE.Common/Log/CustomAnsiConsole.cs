using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Log;

public partial class NonAnsiWriter(TextWriter? output = null) : TextWriter
{
    private TextWriter Output => output ?? Console.Out;
    public override Encoding Encoding => output?.Encoding ?? Console.OutputEncoding;

    private string? _lastOut = "";
    private bool _atLineStart = true;

    public override void Write(char value)
    {
        Output.Write(value);
        _atLineStart = value == '\n';
    }

    public override void Write(string? value)
    {
        RemoveAnsiEscapeSequences(value);
    }

    private void RemoveAnsiEscapeSequences(string? input)
    {
        // Use regular expression to remove ANSI escape sequences
        var output = MyRegex().Replace(input ?? "", "");
        output = MyRegex1().Replace(output, "");
        // 进度条的回行控制可能和后面的日志合并写出，剥离后不能残留在正文开头。
        output = output.TrimStart('\r');
        var whitespaceOnly = string.IsNullOrWhiteSpace(output);
        // 只去重控制序列和留白；连续相同的正文也可能是有效日志，不能丢弃。
        if (whitespaceOnly && _lastOut == input)
            return;
        _lastOut = input;
        if (whitespaceOnly)
        {
            // 进度条留白才需要清理，正文中的换行、空行和缩进必须原样保留。
            output = MyRegex2().Replace(output, "");
            // 只剩空白通常是被剥离的控制序列残渣，但有两类必须保留：
            //  1) 含换行的行分隔（Spectre.Console 0.57 起换行独立写出），
            //     丢弃会让重定向后的 stdout 全程没有换行。渲染帧上下各有留白，
            //     连续换行只落一次，避免把成片的空白行写进按行读取的输出。
            //  2) 行中的空格填充：Spectre 的 RightJustified() 会把单元格补齐到列宽，
            //     填充是纯空格写入；丢弃会让重定向后各列挤在一起
            //     （如 82.68MB236.92KBps00:00:00），按列解析速度的调用方会取错字段。
            // 行首的空白仍然丢弃，避免输出被缩进或产生整行空白。
            if (output.Contains('\n') && !_atLineStart)
            {
                Output.Write('\n');
                _atLineStart = true;
            }
            else if (!_atLineStart && output.Contains(' '))
            {
                Output.Write(output);
            }
            return;
        }
        Output.Write(output);
        _atLineStart = output.EndsWith('\n');
    }

    [GeneratedRegex(@"\x1B\[(\d+;?)+m")]
    private static partial Regex MyRegex();
    [GeneratedRegex(@"\[\??\d+[AKlh]")]
    private static partial Regex MyRegex1();
    [GeneratedRegex("[\r\n] +")]
    private static partial Regex MyRegex2();
}

/// <summary>
/// A console capable of writing ANSI escape sequences.
/// </summary>
public static class CustomAnsiConsole
{
    public static IAnsiConsole Console { get; set; } = AnsiConsole.Console;

    public static void InitConsole(bool forceAnsi, bool noAnsiColor)
    {
        if (forceAnsi)
        {
            var ansiConsoleSettings = new AnsiConsoleSettings();
            if (noAnsiColor)
            {
                ansiConsoleSettings.Out = new AnsiConsoleOutput(new NonAnsiWriter());
            }

            ansiConsoleSettings.Interactive = InteractionSupport.Yes;
            ansiConsoleSettings.Ansi = AnsiSupport.Yes;
            Console = AnsiConsole.Create(ansiConsoleSettings);
            Console.Profile.Width = int.MaxValue;
            // 重定向输出没有屏幕高度；某些平台会返回 -1，导致进度渲染裁剪行时越界。
            if (System.Console.IsOutputRedirected || System.Console.IsErrorRedirected || Console.Profile.Height < 1)
            {
                Console.Profile.Height = int.MaxValue;
            }
        }
        else
        {
            var ansiConsoleSettings = new AnsiConsoleSettings();
            if (noAnsiColor)
            {
                ansiConsoleSettings.Out = new AnsiConsoleOutput(new NonAnsiWriter());
            }
            Console = AnsiConsole.Create(ansiConsoleSettings);
        }
    }

    /// <summary>
    /// Writes the specified markup to the console.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public static void Markup(string value)
    {
        Console.Markup(value);
    }

    /// <summary>
    /// Writes the specified markup, followed by the current line terminator, to the console.
    /// </summary>
    /// <param name="value">The value to write.</param>
    public static void MarkupLine(string value)
    {
        Console.MarkupLine(value);
    }
}
