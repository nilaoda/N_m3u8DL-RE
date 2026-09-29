using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Log;

public partial class NonAnsiWriter : TextWriter
{
    public override Encoding Encoding => Console.OutputEncoding;

    private string? _lastOut = "";
    private bool _atLineStart = true;

    public override void Write(char value)
    {
        Console.Write(value);
        _atLineStart = value == '\n';
    }

    public override void Write(string? value)
    {
        if (_lastOut == value)
        {
            return;
        }
        _lastOut = value;
        RemoveAnsiEscapeSequences(value);
    }

    private void RemoveAnsiEscapeSequences(string? input)
    {
        // Use regular expression to remove ANSI escape sequences
        var output = MyRegex().Replace(input ?? "", "");
        output = MyRegex1().Replace(output, "");
        output = MyRegex2().Replace(output, "");
        if (string.IsNullOrWhiteSpace(output))
        {
            // 只剩空白通常是被剥离的控制序列残渣；含换行则是行分隔（Spectre.Console 0.57 起
            // 换行独立写出），丢弃它会让重定向后的 stdout 全程没有换行。渲染帧上下各有留白，
            // 连续换行只落一次，避免把成片的空白行写进按行读取的输出。
            if (output.Contains('\n') && !_atLineStart)
            {
                Console.Write('\n');
                _atLineStart = true;
            }
            return;
        }
        Console.Write(output);
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