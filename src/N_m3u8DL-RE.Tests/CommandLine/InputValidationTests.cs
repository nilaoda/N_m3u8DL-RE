using System.CommandLine;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public sealed class InputValidationTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-input-").FullName;

    [Theory]
    [InlineData("docto", "doctor")]
    [InlineData("doctorr", "doctor")]
    [InlineData("doctpr", "doctor")]
    [InlineData("docotr", "doctor")]
    [InlineData("mx", "mux")]
    [InlineData("dctr", "doctor")]
    public async Task MisspelledCommandsSuggestACommandAndDoNotInvokeDownload(string input, string expected)
    {
        var command = CommandInvoker.CreateRootCommand();
        var invoked = false;
        command.SetAction(_ => { invoked = true; });
        var result = CommandInvoker.ParseArgs(command, ["--ui-language", "en-US", input]);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(1, await result.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }));
        Assert.Contains(input, stdout.ToString());
        Assert.Contains(expected + Environment.NewLine, stdout.ToString());
        Assert.Contains(input, stderr.ToString());
        Assert.DoesNotContain("Usage:", stdout.ToString());
        Assert.False(invoked);
    }

    [Fact]
    public void SupportedUrlsAndExistingLocalFilesRemainValidInputs()
    {
        var path = Path.Combine(directory, "docto");
        File.WriteAllText(path, "#EXTM3U");
        foreach (var input in new[] { "http://example.test/live", "https://example.test/vod.m3u8", path,
            Path.GetRelativePath(Environment.CurrentDirectory, path), new Uri(path).AbsoluteUri })
            Assert.Empty(CommandInvoker.CreateRootCommand().Parse([input]).Errors);
    }

    [Fact]
    public void MissingPathsAndUnknownWordsHaveAppropriateErrors()
    {
        var path = Path.Combine(directory, "doctor.m3u8");
        Assert.Contains(CommandInvoker.CreateRootCommand().Parse([path]).Errors,
            error => error.Message == $"{ResString.toolsInputMissing}: {path}");
        Assert.Contains(CommandInvoker.CreateRootCommand().Parse([new Uri(path).AbsoluteUri]).Errors,
            error => error.Message.Contains(ResString.toolsInputMissing));
        Assert.Contains(CommandInvoker.CreateRootCommand().Parse(["unrelatedword"]).Errors,
            error => error.Message == string.Format(ResString.inputInvalid, "unrelatedword"));
    }

    [Fact]
    public async Task SuggestionsUseRegisteredCommandsAndLeaveToolsAndCompletionUsable()
    {
        var command = CommandInvoker.CreateRootCommand();
        var inspect = new Command("inspect");
        inspect.Aliases.Add("examine");
        command.Subcommands.Add(inspect);
        command.Subcommands.Add(new Command("hidden") { Hidden = true });
        foreach (var (input, expected) in new[] { ("inspec", "inspect"), ("examin", "examine"), ("hidde", "") })
        {
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            var result = CommandInvoker.ParseArgs(command, [input]);
            Assert.Equal(1, await result.InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr }));
            if (expected != "")
                Assert.Contains(expected + Environment.NewLine, stdout.ToString());
            Assert.DoesNotContain("hidden", stdout.ToString());
        }
        Assert.Empty(command.Parse(["doctor"]).Errors);
        Assert.Empty(command.Parse(["mux", "-i", "missing.mp4", "-o", "out.mp4", "--dry-run"]).Errors);
        Assert.Empty(command.Parse(["--generate-completion", "powershell"]).Errors);
    }

    [Fact]
    public void OtherArgumentErrorsArePreservedAlongsideInvalidInput()
    {
        var result = CommandInvoker.ParseArgs(CommandInvoker.CreateRootCommand(), ["docto", "--thread-count", "invalid"]);
        Assert.Contains(result.Errors, error => error.Message.Contains("docto"));
        Assert.Contains(result.Errors, error => error.Message.Contains("invalid"));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
