using N_m3u8DL_RE.Common.Log;
using Spectre.Console;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.Tests.Common.Log;

[Collection("Download console")]
public class LoggerOutputTests : IDisposable
{
    private readonly TextWriter originalOutput = Console.Out;
    private readonly IAnsiConsole originalConsole = CustomAnsiConsole.Console;
    private readonly LogLevel originalLevel = Logger.LogLevel;
    private readonly bool originalWriteFile = Logger.IsWriteFile;
    private readonly StringWriter renderedOutput = new();
    private readonly StringWriter directOutput = new();

    public LoggerOutputTests()
    {
        Console.SetOut(directOutput);
        CustomAnsiConsole.Console = CreateConsole(renderedOutput);
        Logger.LogLevel = LogLevel.DEBUG;
        Logger.IsWriteFile = false;
    }

    private static IAnsiConsole CreateConsole(TextWriter output, bool interactive = false)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(output),
            Ansi = interactive ? AnsiSupport.Yes : AnsiSupport.No,
            Interactive = interactive ? InteractionSupport.Yes : InteractionSupport.No
        });
        console.Profile.Width = 117;
        console.Profile.Height = 30;
        return console;
    }

    [Theory]
    [InlineData(LogLevel.INFO, "INFO : ")]
    [InlineData(LogLevel.WARN, "WARN : ")]
    [InlineData(LogLevel.ERROR, "ERROR: ")]
    [InlineData(LogLevel.DEBUG, "DEBUG: ")]
    public void PlainMessagesUseTheConfiguredConsoleWithoutHardWrapping(LogLevel level, string prefix)
    {
        var message = "https://example.test/分片.ts?token=" + new string('a', 300) + " [red]literal[/] :smile:";
        switch (level)
        {
            case LogLevel.INFO:
                Logger.Info(message);
                break;
            case LogLevel.WARN:
                Logger.Warn(message);
                break;
            case LogLevel.ERROR:
                Logger.Error(message);
                break;
            case LogLevel.DEBUG:
                Logger.Debug(message);
                break;
        }

        var output = renderedOutput.ToString();
        Assert.Contains(prefix + message, output);
        Assert.Equal(1, output.Count(c => c == '\n'));
        Assert.Empty(directOutput.ToString());
        Assert.Equal(117, CustomAnsiConsole.Console.Profile.Width);
    }

    [Fact]
    public void ExplicitNewlinesInMessagesArePreserved()
    {
        var message = $"first{Environment.NewLine}{Environment.NewLine}second{Environment.NewLine}";
        Logger.Info(message);
        Assert.EndsWith(message + Environment.NewLine, renderedOutput.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoAnsiWriterPreservesTheWholeMessageAndLineTerminator(bool interactive)
    {
        CustomAnsiConsole.Console = CreateConsole(new NonAnsiWriter(), interactive);
        var message = "[red]literal[/] :smile: " + new string('a', 300);
        Logger.Warn(message);
        var output = directOutput.ToString();
        Assert.Contains("WARN : " + message, output);
        Assert.Equal(1, output.Count(c => c == '\n'));
        Assert.DoesNotContain('\u001b', output);
        Assert.Equal(117, CustomAnsiConsole.Console.Profile.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoAnsiWriterPreservesBlankLinesAndIndentation(bool interactive)
    {
        CustomAnsiConsole.Console = CreateConsole(new NonAnsiWriter(), interactive);
        var newline = Environment.NewLine;
        var message = $"header:{newline}  key: value{newline}{newline}  end{newline}";
        Logger.Debug(message);
        Assert.EndsWith((message + newline).ReplaceLineEndings("\n"), directOutput.ToString().ReplaceLineEndings("\n"));
        Logger.Info("next");
        Assert.Contains("INFO : next\n", directOutput.ToString().ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressRefreshesAndConcurrentLogsDoNotSeparatePrefixesAndMessages(bool noAnsiOutput)
    {
        CustomAnsiConsole.Console = CreateConsole(noAnsiOutput ? new NonAnsiWriter() : renderedOutput, interactive: true);
        var progress = CustomAnsiConsole.Console.Progress().AutoClear(true);
        progress.AutoRefresh = false;
        progress.Columns(new TaskDescriptionColumn());
        const string retry = "直播分片暂时不可用，稍后重试...";
        const string recovered = "直播请求已恢复，继续录制";
        var url = "https://example.test/media.ts?token=" + new string('a', 300);
        const string multiline = "header:\n  key: value\n\n  end\n";
        progress.Start(context =>
        {
            context.AddTask("recording");
            context.Refresh();
            Logger.Warn(retry);
            Logger.Info(recovered);
            Logger.Info(url);
            Logger.Debug(multiline);
            Logger.Warn("   ");
            Parallel.For(0, 16, index =>
            {
                Logger.Info($"message[{index}]");
                context.Refresh();
            });
        });

        var output = noAnsiOutput ? directOutput.ToString() : renderedOutput.ToString();
        if (noAnsiOutput)
        {
            Assert.DoesNotContain('\u001b', output);
            Assert.DoesNotContain('\r', output);
        }
        else
        {
            Assert.Empty(directOutput.ToString());
        }
        output = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", "").ReplaceLineEndings("\n");
        Assert.Contains("WARN : " + retry, output);
        Assert.Contains("INFO : " + recovered, output);
        Assert.Contains("INFO : " + url + "\n", output);
        Assert.Contains("DEBUG: " + multiline + "\n", output);
        Assert.Contains("WARN :    \n", output);
        for (var index = 0; index < 16; index++)
            Assert.Single(Regex.Matches(output, Regex.Escape($"INFO : message[{index}]\n")));
        Assert.Equal(117, CustomAnsiConsole.Console.Profile.Width);
    }

    public void Dispose()
    {
        CustomAnsiConsole.Console = originalConsole;
        Console.SetOut(originalOutput);
        Logger.LogLevel = originalLevel;
        Logger.IsWriteFile = originalWriteFile;
        renderedOutput.Dispose();
        directOutput.Dispose();
    }
}
