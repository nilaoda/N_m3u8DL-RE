using System.CommandLine;
using System.Reflection;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class PowerShellCompletionActionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bash")]
    public void MissingOrUnsupportedShellIsRejected(string? shell)
    {
        var field = typeof(CommandInvoker).GetField("GenerateCompletion", BindingFlags.Static | BindingFlags.NonPublic)!;
        var option = Assert.IsType<Option<string?>>(field.GetValue(null));
        var command = new RootCommand { option };
        string[] args = shell == null ? ["--generate-completion"] : ["--generate-completion", shell];
        var result = command.Parse(args);
        var action = Assert.IsType<PowerShellCompletionAction>(option.Action);
        Assert.Equal(1, action.Invoke(result));
    }
}
