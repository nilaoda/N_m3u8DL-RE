using System.CommandLine;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

public class KeyOptionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptsPaddedAndUnpaddedKeysAndKidPairs(bool omitPadding)
    {
        var keyBytes = Enumerable.Range(240, 16).Select(i => (byte)i).ToArray();
        var kidBytes = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var key = Convert.ToBase64String(keyBytes);
        var kid = Convert.ToBase64String(kidBytes);
        if (omitPadding)
        {
            key = key.TrimEnd('=');
            kid = kid.TrimEnd('=');
        }
        var expectedKey = Convert.ToHexString(keyBytes).ToLowerInvariant();
        var expectedKid = Convert.ToHexString(kidBytes).ToLowerInvariant();
        var command = CommandInvoker.CreateRootCommand();
        var result = command.Parse(["https://example.test/live.mpd", "--key", key,
            "--key", $"{kid}:{key}", "--key", expectedKey]);
        Assert.Empty(result.Errors);
        var option = Assert.IsType<Option<string[]?>>(command.Options.Single(o => o.Name == "--key"));
        var parsedKeys = Assert.IsType<string[]>(result.GetValue(option));
        Assert.Equal([expectedKey, $"{expectedKid}:{expectedKey}", expectedKey], parsedKeys);
    }

    [Theory]
    [InlineData(15)]
    [InlineData(17)]
    public void RejectsKeysWithIncorrectDecodedLength(int length)
    {
        var key = Convert.ToBase64String(new byte[length]).TrimEnd('=');
        Assert.NotEmpty(CommandInvoker.CreateRootCommand().Parse(["https://example.test/live.mpd", "--key", key]).Errors);
    }
}
