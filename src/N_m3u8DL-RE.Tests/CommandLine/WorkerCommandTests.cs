using System.CommandLine;
using System.Diagnostics;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public class WorkerCommandTests
{
    [Fact]
    public void WorkerIsRecognizedByCommandSyntaxInsteadOfArgumentContents()
    {
        Assert.True(ConfigFile.IsWorkerRequest(["worker", "--stdio"]));
        Assert.True(ConfigFile.IsWorkerRequest(["--ui-language", "en-US", "worker", "--help"]));
        Assert.False(ConfigFile.IsWorkerRequest(["--save-name", "worker", "https://example.test/video.m3u8"]));
        Assert.False(ConfigFile.IsWorkerRequest(["--", "worker"]));
        var parsed = CommandInvoker.CreateRootCommand().Parse(["worker", "--stdio"]);
        Assert.Empty(parsed.Errors);
        Assert.Equal("worker", parsed.CommandResult.Command.Name);
        Assert.True(parsed.GetValue<bool>("--stdio"));
    }

    [Theory]
    [InlineData("worker")]
    [InlineData("worker", "--stdio=false")]
    public void WorkerRequiresEnabledStdio(params string[] arguments)
    {
        Assert.NotEmpty(CommandInvoker.CreateRootCommand().Parse(arguments).Errors);
    }

    [Fact]
    public async Task HelpListsWorkerAndShowsItsOwnOptionsWithoutStartingIt()
    {
        foreach (var arguments in new[] { new[] { "--help" }, new[] { "worker", "--help" } })
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            var parsed = CommandInvoker.ParseArgs(CommandInvoker.CreateRootCommand(), arguments);
            Assert.Equal(0, await parsed.InvokeAsync(new InvocationConfiguration { Output = output, Error = error }));
            Assert.Contains("worker", output.ToString());
            if (arguments.Length > 1)
            {
                Assert.Contains("--stdio", output.ToString());
                Assert.DoesNotContain("<input>", output.ToString());
                Assert.DoesNotContain("--thread-count", output.ToString());
            }
            Assert.DoesNotContain("\"type\":\"ready\"", output.ToString());
            Assert.Equal("", error.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerHelpRunsBeforeLoadingAnExplicitConfigurationFile(bool responseFile)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
        };
        var root = Directory.CreateTempSubdirectory("worker-command-").FullName;
        var responsePath = Path.Combine(root, "worker.args");
        await File.WriteAllTextAsync(responsePath, "worker --help");
        string[] arguments = [typeof(CommandInvoker).Assembly.Location,
            "--config", Path.Combine(root, "missing.conf"),
            .. responseFile ? new[] { "@" + responsePath } : new[] { "worker", "--help" }];
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("--stdio", await output);
            Assert.Equal("", await error);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            Directory.Delete(root, true);
        }
    }
}
