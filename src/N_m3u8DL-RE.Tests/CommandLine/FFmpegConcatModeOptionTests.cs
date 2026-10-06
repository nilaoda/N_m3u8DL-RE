using System.Reflection;
using System.CommandLine;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public class FFmpegConcatModeOptionTests
{
    [Theory]
    [InlineData(null, FFmpegConcatMode.LOCAL_HTTP)]
    [InlineData("local_http", FFmpegConcatMode.LOCAL_HTTP)]
    [InlineData("LOCAL_HTTP", FFmpegConcatMode.LOCAL_HTTP)]
    [InlineData("protocol", FFmpegConcatMode.PROTOCOL)]
    [InlineData("demuxer", FFmpegConcatMode.DEMUXER)]
    public void DefaultsToLocalHttpAndSupportsExplicitModes(string? mode, FFmpegConcatMode expected)
    {
        var command = CommandInvoker.CreateRootCommand();
        string[] args = mode == null ? ["https://example.test/vod.m3u8"]
            : ["https://example.test/vod.m3u8", "--ffmpeg-concat-mode", mode];
        var result = command.Parse(args);
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(expected, options.FFmpegConcatMode);
    }

    [Theory]
    [InlineData("filelist")]
    public void InvalidModeIsRejected(string mode)
    {
        var result = CommandInvoker.CreateRootCommand().Parse(["https://example.test/vod.m3u8", "--ffmpeg-concat-mode", mode]);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData("--use-ffmpeg-concat-demuxer", "true", "--ffmpeg-concat-mode", "protocol", FFmpegConcatMode.PROTOCOL)]
    [InlineData("--use-ffmpeg-concat-demuxer", "true", "--ffmpeg-concat-mode", "local_http", FFmpegConcatMode.LOCAL_HTTP)]
    [InlineData("--ffmpeg-concat-mode", "protocol", "--use-ffmpeg-concat-demuxer", "true", FFmpegConcatMode.DEMUXER)]
    [InlineData("--ffmpeg-concat-mode", "protocol", "--use-ffmpeg-concat-demuxer", "false", FFmpegConcatMode.PROTOCOL)]
    [InlineData("--ffmpeg-concat-mode", "invalid", "--use-ffmpeg-concat-demuxer", "true", FFmpegConcatMode.DEMUXER)]
    public void CommandLineModeOverridesConfigurationAcrossOldAndNewOptions(string configuredKey, string configuredValue,
        string suppliedKey, string suppliedValue, FFmpegConcatMode expected)
    {
        var command = CommandInvoker.CreateRootCommand();
        var config = new ConfigFile(["https://example.test/vod.m3u8", suppliedKey, suppliedValue], [configuredKey, configuredValue]);
        var result = command.Parse(config.Merge(command), new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null
        });
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(expected, options.EffectiveConcatMode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnabledLegacyOptionWinsWhenBothFormsAreSuppliedAtSameLevel(bool inConfiguration)
    {
        string[] modes = ["--ffmpeg-concat-mode", "protocol", "--use-ffmpeg-concat-demuxer"];
        var config = inConfiguration
            ? new ConfigFile(["https://example.test/vod.m3u8"], modes)
            : new ConfigFile(["https://example.test/vod.m3u8", .. modes], []);
        var command = CommandInvoker.CreateRootCommand();
        var result = command.Parse(config.Merge(command));
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        var options = Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
        Assert.Equal(FFmpegConcatMode.DEMUXER, options.EffectiveConcatMode);
    }
}
