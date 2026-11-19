using N_m3u8DL_RE.CommandLine;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace N_m3u8DL_RE.Runtime;

internal static class OptionNormalizer
{
    internal static MyOption Parse(string input, IEnumerable<string> arguments)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input is required.");
        var root = CommandInvoker.CreateRootCommand();
        root.Subcommands.Clear();
        var parsed = CommandInvoker.ParseArgs(root, [..arguments, "--", input], new ParserConfiguration
        {
            EnablePosixBundling = false, ResponseFileTokenReplacer = null
        });
        if (parsed.Errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, parsed.Errors.Select(e => e.Message)));
        return CommandInvoker.GetOptions(parsed);
    }
}
