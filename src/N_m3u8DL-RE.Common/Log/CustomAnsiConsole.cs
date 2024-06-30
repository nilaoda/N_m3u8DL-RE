using System.Text;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Log;

public class NonAnsiWriter : TextWriter
{
    public override Encoding Encoding => Console.OutputEncoding;

    private string lastOut = "";

    public override void Write(char value)
    {
        Console.Write(value);
    }

    public override void Write(string value)
    {
        if (lastOut == value)
        {
            return;
        }
        lastOut = value;
        RemoveAnsiEscapeSequences(value);
    }

    private void RemoveAnsiEscapeSequences(string input)
    {
        // Use regular expression to remove ANSI escape sequences
        string output = Regex.Replace(input, @"\x1B\[(\d+;?)+m", "");
        output = Regex.Replace(output, @"\[\??\d+[AKlh]", "");
        output = Regex.Replace(output,"[\r\n] +","");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }
        Console.Write(output);
    }
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