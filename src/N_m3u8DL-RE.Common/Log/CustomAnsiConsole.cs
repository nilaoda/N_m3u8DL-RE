using Spectre.Console;

namespace N_m3u8DL_RE.Common.Log;

/// <summary>
/// A console capable of writing ANSI escape sequences.
/// </summary>
public static class CustomAnsiConsole
{
    // var ansiConsoleSettings = new AnsiConsoleSettings();
    // ansiConsoleSettings.Ansi = AnsiSupport.Yes;
    public static IAnsiConsole Console { get; set; }

    public static void InitConsole(bool forceAnsi)
    {
        if (forceAnsi)

        {
            var ansiConsoleSettings = new AnsiConsoleSettings();
            ansiConsoleSettings.Interactive = InteractionSupport.Yes;
            ansiConsoleSettings.Ansi = AnsiSupport.Yes;
            // ansiConsoleSettings.Ansi = AnsiSupport.Yes;
            Console = AnsiConsole.Create(ansiConsoleSettings);
        }
        else
        {
            Console = AnsiConsole.Console;
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