using System.CommandLine;
using System.Reflection;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class NetworkInterfaceOptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("eth1")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("Wi-Fi")]
    public void CommandLinePreservesInterfaceNameOrAddress(string? value)
    {
        var command = new RootCommand();
        foreach (var field in typeof(CommandInvoker).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (field.GetValue(null) is Option option)
                command.Options.Add(option);
            else if (field.GetValue(null) is Argument argument)
                command.Arguments.Add(argument);
        }
        string[] args = value == null ? ["https://example.test/vod.mpd"]
            : ["https://example.test/vod.mpd", "--interface", value];
        var result = command.Parse(args);
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(value, options.NetworkInterface);
    }
}
