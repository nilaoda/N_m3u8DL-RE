using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.CommandLine;

[Collection("Download console")]
public class ConfigFileTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("re-config-").FullName;
    private string ConfigPath => Path.Combine(directory, "config.conf");

    [Fact]
    public void DefaultPathsUseUserDirectories()
    {
        Assert.Equal(Path.Combine(directory, ".config", "N_m3u8DL-RE", "config.conf"),
            ConfigFile.GetDefaultPath(false, directory, "unused", null));
        Assert.Equal(Path.Combine(directory, ".config", "N_m3u8DL-RE", "config.conf"),
            ConfigFile.GetDefaultPath(false, directory, "unused", "relative-xdg"));
        Assert.Equal(Path.Combine(directory, "N_m3u8DL-RE", "config.conf"),
            ConfigFile.GetDefaultPath(false, "unused", "unused", directory));
        Assert.Equal(Path.Combine(directory, "N_m3u8DL-RE", "config.conf"),
            ConfigFile.GetDefaultPath(true, "unused", directory, "ignored"));
        Assert.Empty(ConfigFile.GetDefaultPath(false, "", "unused", null));
        Assert.Empty(ConfigFile.GetDefaultPath(true, "unused", "", null));
    }

    [Fact]
    public void MissingDefaultIsOptionalButExplicitFileIsRequired()
    {
        Assert.Empty(ConfigFile.Load(["input.m3u8"], ConfigPath).Defaults);
        Assert.Throws<FileNotFoundException>(() => ConfigFile.Load(["--config", ConfigPath]));
    }

    [Fact]
    public void DirectoryAtDefaultConfigurationPathIsAnError()
    {
        Directory.CreateDirectory(ConfigPath);
        Assert.Throws<UnauthorizedAccessException>(() => ConfigFile.Load(["input.m3u8"], ConfigPath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyclicParameterFileReferencesAreRejected(bool indirect)
    {
        var otherPath = Path.Combine(directory, "other.conf");
        File.WriteAllText(ConfigPath, "@" + (indirect ? otherPath : ConfigPath));
        if (indirect)
            File.WriteAllText(otherPath, "@" + ConfigPath);
        Assert.Throws<ArgumentException>(() => ConfigFile.Load(["input.m3u8", "--config", ConfigPath]));
        Assert.Throws<ArgumentException>(() => ConfigFile.Load(["@" + ConfigPath]));
    }

    [Fact]
    public void ParameterFileDepthIsLimitedAndRepeatedInclusionIsAllowed()
    {
        for (var i = 0; i < 33; i++)
            File.WriteAllText(Path.Combine(directory, $"{i}.conf"), i == 32 ? "--no-log" :
                "@" + Path.Combine(directory, $"{i + 1}.conf"));
        Assert.Throws<ArgumentException>(() => ConfigFile.Load(["@" + Path.Combine(directory, "0.conf")]));
        File.WriteAllText(ConfigPath, "-H \"X-Test: value\"");
        var args = ConfigFile.Load(["@" + ConfigPath, "@" + ConfigPath], Path.Combine(directory, "missing.conf")).Arguments;
        Assert.Equal(["-H", "X-Test: value", "-H", "X-Test: value"], args);
    }

    [Fact]
    public void ExpansionPreservesCommandNamesAndStopsAfterDoubleDash()
    {
        var input = new RootCommand().Name;
        Assert.Equal([input], ConfigFile.Load([input], ConfigPath).Arguments);
        Assert.Equal(["@"], ConfigFile.Load(["@"], ConfigPath).Arguments);
        var responsePath = Path.Combine(directory, "args.txt");
        File.WriteAllText(responsePath, "--\n@missing.conf");
        Assert.Equal(["--", "@missing.conf", "@also-missing.conf"],
            ConfigFile.Load(["@" + responsePath, "@also-missing.conf"], ConfigPath).Arguments);
    }

    [Fact]
    public void ExplicitFileReplacesDefaultAndSupportsBomCommentsAndQuotedPaths()
    {
        File.WriteAllText(ConfigPath, "--thread-count 2");
        var explicitPath = Path.Combine(directory, "my config.conf");
        File.WriteAllText(explicitPath, "# defaults\n-mt\n--thread-count 7\n--tmp-dir \"路径 with spaces\"\n--auto-select", new UTF8Encoding(true));
        var options = Parse(ConfigFile.Load(["input.m3u8", "--config=" + explicitPath], ConfigPath));
        Assert.Equal(7, options.ThreadCount);
        Assert.Equal("路径 with spaces", options.TmpDir);
        Assert.True(options.ConcurrentDownload);
        Assert.True(options.AutoSelect);
    }

    [Fact]
    public void CommandLineOverridesScalarsAndMergesRepeatedOptions()
    {
        File.WriteAllText(ConfigPath, """
            --thread-count 2
            -mt
            --no-log
            --tmp-dir "old directory"
            -H "Referer: https://old.example"
            -H "User-Agent: old"
            --key 11111111111111111111111111111111
            --key 22222222222222222222222222222222
            --http-request-timeout 30
            --vod-select-parts true
            """);
        var options = Parse(ConfigFile.Load(["input.m3u8", "--thread-count=9",
            "--concurrent-download", "false", "--no-log", "false", "--tmp-dir", "new directory",
            "--header", "User-Agent: new", "--key", "33333333333333333333333333333333",
            "--vod-select-parts", "false"], ConfigPath));
        Assert.Equal(9, options.ThreadCount);
        Assert.False(options.ConcurrentDownload);
        Assert.False(options.NoLog);
        Assert.Equal("new directory", options.TmpDir);
        Assert.Equal(2, options.Headers.Count);
        Assert.Equal("new", options.Headers["user-agent"]);
        Assert.Equal("https://old.example", options.Headers["referer"]);
        Assert.Equal(["11111111111111111111111111111111", "22222222222222222222222222222222",
            "33333333333333333333333333333333"], options.Keys!);
        Assert.True(options.HttpRequestTimeoutSpecified);
        Assert.Equal(30, options.HttpRequestTimeout);
        Assert.False(options.VodSelectParts);
    }

    [Fact]
    public void OverriddenValuesAreNotConvertedOrRead()
    {
        var importedPath = Path.Combine(directory, "new.mp4");
        File.WriteAllBytes(importedPath, [1, 2, 3]);
        File.WriteAllText(ConfigPath, """
            --thread-count invalid
            --no-log true
            --custom-hls-key /missing/old.key
            --custom-hls-iv /missing/old.iv
            """);
        var options = Parse(ConfigFile.Load(["input.m3u8", "--thread-count", "8", "--no-log", "false",
            "--custom-hls-key", "11111111111111111111111111111111",
            "--custom-hls-iv", "22222222222222222222222222222222", "--mux-import", importedPath], ConfigPath));
        Assert.Equal(8, options.ThreadCount);
        Assert.False(options.NoLog);
        Assert.Equal(Enumerable.Repeat((byte)0x11, 16), options.CustomHLSKey);
        Assert.Equal(Enumerable.Repeat((byte)0x22, 16), options.CustomHLSIv);
        Assert.Equal(importedPath, Assert.Single(options.MuxImports!).FilePath);
    }

    [Fact]
    public void HeadersMergeByNameIgnoringCaseAndCurrentCulture()
    {
        File.WriteAllText(ConfigPath, """
            -H "Referer: https://example.test"
            --header "User-Agent: configured agent"
            -H "X-ID: configured id"
            """);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // 土耳其语的 I 小写规则不同，请求头仍应按 HTTP 规则匹配 ASCII 名称。
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var options = Parse(ConfigFile.Load(["input.m3u8", "--header", "USER-AGENT: command agent",
                "-H", "x-id: command id", "-H", "Authorization: Bearer example"], ConfigPath));
            Assert.Equal(4, options.Headers.Count);
            Assert.Equal("https://example.test", options.Headers["REFERER"]);
            Assert.Equal("command agent", options.Headers["user-agent"]);
            Assert.Equal("command id", options.Headers["X-ID"]);
            Assert.Equal("Bearer example", options.Headers["authorization"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void RepeatedOptionsAppendConfigurationAndCommandLineValues()
    {
        var configuredPath = Path.Combine(directory, "configured.mp4");
        var commandPath = Path.Combine(directory, "command.mp4");
        File.WriteAllBytes(configuredPath, [1]);
        File.WriteAllBytes(commandPath, [2]);
        File.WriteAllText(ConfigPath, $"--ad-keyword intro\n--mux-import \"{configuredPath}\"");
        var options = Parse(ConfigFile.Load(["input.m3u8", "--ad-keyword", "outro",
            "--mux-import", commandPath], ConfigPath));
        Assert.Equal(["intro", "outro"], options.AdKeywords!);
        Assert.Equal([configuredPath, commandPath], options.MuxImports!.Select(file => file.FilePath));
    }

    [Fact]
    public void ConfiguredAdKeywordsDoNotConsumeTheCommandLineInput()
    {
        File.WriteAllText(ConfigPath, "--ad-keyword intro\n--ad-keyword outro");
        var options = Parse(ConfigFile.Load(["input.m3u8"], ConfigPath));
        Assert.Equal("input.m3u8", options.Input);
        Assert.Equal(["intro", "outro"], options.AdKeywords!);
    }

    [Fact]
    public void ResponseFileOptionsAlsoOverrideDefaults()
    {
        File.WriteAllText(ConfigPath, "--thread-count 2\n--http-request-timeout 30");
        var responsePath = Path.Combine(directory, "args.txt");
        File.WriteAllText(responsePath, "--thread-count 9\n--http-request-timeout=10");
        var options = Parse(ConfigFile.Load(["input.m3u8", "@" + responsePath], ConfigPath));
        Assert.Equal(9, options.ThreadCount);
        Assert.Equal(10, options.HttpRequestTimeout);
        Assert.True(options.HttpRequestTimeoutSpecified);
    }

    [Fact]
    public void RepeatedAndCompoundConfiguredOptionsKeepTheirValues()
    {
        File.WriteAllText(ConfigPath, """
            -H "Referer: https://example.test"
            -H "User-Agent: default agent"
            --ad-keyword intro
            --ad-keyword outro
            --key 11111111111111111111111111111111
            --key 22222222222222222222222222222222
            -M format=mp4:keep=true
            """);
        var options = Parse(ConfigFile.Load(["input.m3u8"], ConfigPath));
        Assert.Equal(2, options.Headers.Count);
        Assert.Equal("default agent", options.Headers["user-agent"]);
        Assert.Equal(["intro", "outro"], options.AdKeywords!);
        Assert.Equal(2, options.Keys!.Length);
        Assert.True(options.MuxAfterDone);
        Assert.True(options.MuxOptions!.KeepFiles);
    }

    [Fact]
    public void NoConfigSkipsDefaultAndConflictsWithExplicitConfig()
    {
        File.WriteAllText(ConfigPath, "--unknown-option");
        Assert.Empty(ConfigFile.Load(["--no-config"], ConfigPath).Defaults);
        Assert.Throws<ArgumentException>(() => ConfigFile.Load(["--no-config", "--config", ConfigPath]));
        Assert.Throws<ArgumentException>(() => ConfigFile.Load(["--config"]));
    }

    [Theory]
    [InlineData("--no-config")]
    [InlineData("--config")]
    [InlineData("--generate-completion")]
    [InlineData("--ui-language")]
    public void ControlOptionNamesUsedAsValuesDoNotChangeConfigurationLoading(string saveName)
    {
        File.WriteAllText(ConfigPath, "--thread-count 4\n--ui-language zh-CN");
        var config = ConfigFile.Load(["input.m3u8", "--save-name", saveName], ConfigPath);
        Assert.NotEmpty(config.Defaults);
        Assert.Equal("zh-CN", config.GetLanguage());
        Assert.Equal(saveName, Parse(config).SaveName);
    }

    [Theory]
    [InlineData("https://example.test/video.m3u8")]
    [InlineData("--unknown-option")]
    [InlineData("--thread-count invalid")]
    [InlineData("--custom-range")]
    [InlineData("--custom-hls-key")]
    [InlineData("--key")]
    [InlineData("--mux-import")]
    [InlineData("--save-name")]
    [InlineData("--config other.conf")]
    [InlineData("--no-config")]
    [InlineData("--help")]
    [InlineData("--version")]
    [InlineData("--morehelp save-pattern")]
    [InlineData("--generate-completion powershell")]
    [InlineData("[suggest]")]
    public void InvalidConfigurationCannotChangeTheRequestedOperation(string content)
    {
        File.WriteAllText(ConfigPath, content);
        var config = ConfigFile.Load(["input.m3u8"], ConfigPath);
        Assert.Throws<ArgumentException>(() => config.Merge(CommandInvoker.CreateRootCommand()));
    }

    [Theory]
    [InlineData("--ui-language")]
    [InlineData("--ui-language invalid")]
    [InlineData("--ui-language zh-CN\n--ui-language en-US")]
    public void InvalidLanguageDefaultsAreReportedAfterLanguageDetection(string content)
    {
        File.WriteAllText(ConfigPath, content);
        var config = ConfigFile.Load(["input.m3u8"], ConfigPath);
        Assert.Null(config.GetLanguage());
        Assert.Throws<ArgumentException>(() => config.Merge(CommandInvoker.CreateRootCommand()));

        var overridden = ConfigFile.Load(["input.m3u8", "--ui-language", "zh-TW"], ConfigPath);
        Assert.Equal("zh-TW", overridden.GetLanguage());
        Assert.Equal("zh-TW", Parse(overridden).UILanguage);
    }

    [Theory]
    [InlineData("--ui-language")]
    [InlineData("--ui-language invalid")]
    [InlineData("--ui-language zh-CN --ui-language en-US")]
    public void InvalidCommandLineLanguageDoesNotThrowDuringStartup(string content)
    {
        var args = CommandLineParser.SplitCommandLine(content).ToArray();
        Assert.Null(new ConfigFile(args, ["--ui-language", "zh-CN"]).GetLanguage());
    }

    [Theory]
    [InlineData("--http-request-timeout")]
    [InlineData("--vod-select-parts")]
    [InlineData("--")]
    public void OptionLookingValuesAndDoubleDashKeepTheirMeaning(string saveName)
    {
        File.WriteAllText(ConfigPath, $"--save-name={saveName}\n--thread-count 4");
        var options = Parse(ConfigFile.Load(["--", "--no-config"], ConfigPath));
        Assert.Equal("--no-config", options.Input);
        Assert.Equal(saveName, options.SaveName);
        Assert.False(options.HttpRequestTimeoutSpecified);
        Assert.Null(options.VodSelectParts);
        Assert.Equal(4, options.ThreadCount);
    }

    [Theory]
    [InlineData("--generate-completion", "powershell")]
    [InlineData("[suggest:10]", "N_m3u8DL-RE --config")]
    public void CompletionIgnoresConfiguration(string operation, string value)
    {
        File.WriteAllText(ConfigPath, "@missing.conf");
        var config = ConfigFile.Load([operation, value, "--config", "missing.conf"], ConfigPath);
        Assert.Empty(config.Defaults);
        Assert.Equal(operation, config.Arguments[0]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HelpUsesConfigurationLanguageWithCommandLinePrecedence(bool overrideLanguage, bool useDefaultPath)
    {
        if (useDefaultPath && OperatingSystem.IsWindows())
            return;
        File.WriteAllText(ConfigPath, "--ui-language zh-CN\n--auto-select");
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (useDefaultPath)
        {
            var defaultDirectory = Directory.CreateDirectory(Path.Combine(directory, "N_m3u8DL-RE")).FullName;
            File.Copy(ConfigPath, Path.Combine(defaultDirectory, "config.conf"));
            // 仅设置子进程环境，避免测试污染其它用例或用户真实配置。
            start.Environment["XDG_CONFIG_HOME"] = directory;
        }
        string[] args = useDefaultPath ? [typeof(CommandInvoker).Assembly.Location, "--help"] :
            [typeof(CommandInvoker).Assembly.Location, "--config", ConfigPath, "--help"];
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        if (overrideLanguage)
        {
            start.ArgumentList.Add("--ui-language=en-US");
        }
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var text = await output + await error;
            Assert.True(process.ExitCode == 0, text);
            Assert.Contains(overrideLanguage ? "Read a configuration file" : "读取指定配置文件", text);
            Assert.Contains(overrideLanguage ? "Disable log file output [default: False]" :
                "关闭日志文件输出 [default: False]", text);
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    private static MyOption Parse(ConfigFile config)
    {
        var command = CommandInvoker.CreateRootCommand();
        // 这里只验证配置合并，下载输入的有效性由 InputValidationTests 单独覆盖。
        var input = command.Arguments.Single();
        var validators = input.Validators.ToArray();
        ParseResult result;
        try
        {
            input.Validators.Clear();
            result = command.Parse(config.Merge(command), new ParserConfiguration
            {
                EnablePosixBundling = false,
                ResponseFileTokenReplacer = null
            });
        }
        finally
        {
            foreach (var validator in validators)
                input.Validators.Add(validator);
        }
        Assert.Empty(result.Errors);
        var getOptions = typeof(CommandInvoker).GetMethod("GetOptions", BindingFlags.Static | BindingFlags.NonPublic)!;
        return Assert.IsType<MyOption>(getOptions.Invoke(null, [result]));
    }

    public void Dispose() => Directory.Delete(directory, true);
}
