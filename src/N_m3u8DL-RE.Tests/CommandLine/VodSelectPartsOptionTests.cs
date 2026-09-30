using System.CommandLine;
using System.Reflection;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class VodSelectPartsOptionTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void CommandLinePreservesAutomaticEnabledAndDisabledStates(string? value, bool? expected)
    {
        // 直接解析实际命令行符号并读取运行选项，避免 InvokeArgs 的 Environment.Exit。
        var command = new RootCommand();
        foreach (var field in typeof(CommandInvoker).GetFields(BindingFlags.Static | BindingFlags.NonPublic))
        {
            if (field.GetValue(null) is Option option)
                command.Options.Add(option);
            else if (field.GetValue(null) is Argument argument)
                command.Arguments.Add(argument);
        }

        var args = new List<string> { "https://example.test/vod.mpd" };
        if (value != null)
        {
            args.Add("--vod-select-parts");
            if (value.Length > 0)
                args.Add(value);
        }
        var result = command.Parse(args.ToArray());
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(expected, options.VodSelectParts);
    }
}
