using System.Net;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.ConsoleUI;
using N_m3u8DL_RE.Runtime;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE;

internal class Program
{
    static async Task Main(string[] args)
    {
        // 处理NT6.0及以下System.CommandLine报错CultureNotFound问题
        if (OperatingSystem.IsWindows()) 
        {
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major < 6 || osVersion is { Major: 6, Minor: 0 })
            {
                Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1");
            }
        }
        
        var loc = new ConfigFile(args, []).GetLanguage() ?? CultureUtil.GetCurrentCultureName();
        ResString.CurrentLoc = loc;
        CultureUtil.ChangeCurrentCultureName(loc);

        ConfigFile configFile;
        try
        {
            // 展开后的参数同时用于运行模式识别与配置加载，避免响应文件内的 Worker 走入下载配置路径。
            var preparedArgs = ConfigFile.PrepareArguments(args);
            loc = new ConfigFile(preparedArgs, []).GetLanguage() ?? loc;
            ResString.CurrentLoc = loc;
            CultureUtil.ChangeCurrentCultureName(loc);
            if (await CommandInvoker.TryInvokeWorkerAsync(preparedArgs) is { } workerExitCode)
            {
                Environment.ExitCode = workerExitCode;
                return;
            }
            configFile = ConfigFile.LoadPrepared(preparedArgs);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"{ResString.configFileLoadFailed}: {ex.Message}");
            Environment.ExitCode = 1;
            return;
        }

        // 在生成帮助文本前选定配置中的语言，使帮助与实际运行语言一致。
        loc = configFile.GetLanguage() ?? loc;
        ResString.CurrentLoc = loc;
        CultureUtil.ChangeCurrentCultureName(loc);

        await CommandInvoker.InvokeArgs(configFile, DoWorkAsync);
    }

    static void RestoreTerminal()
    {
        try
        {
            Logger.Extra("Program Exit...");
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows())
            {
                System.Diagnostics.Process.Start("tput", "cnorm");
            }
        }
        catch { }
    }

    static async Task DoWorkAsync(MyOption option)
    {
        Console.CancelKeyPress += (_, _) => RestoreTerminal();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreTerminal();
        try { Console.CursorVisible = true; } catch { }
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            option.ForceAnsiConsole = true;
            option.NoAnsiColor = true;
            Logger.Info(ResString.consoleRedirected);
        }
        CustomAnsiConsole.InitConsole(option.ForceAnsiConsole, option.NoAnsiColor);
        Logger.IsWriteFile = !option.NoLog;
        Logger.LogFilePath = option.LogFilePath;
        Logger.InitLogFile();
        Logger.LogLevel = option.LogLevel;
        Logger.Info(CommandInvoker.VERSION_INFO);
        using var control = new TaskControl();
        var context = new TaskExecutionContext(new NullTaskObserver(), new ConsoleUserInteraction(), control);
        var result = await DownloadTaskRunner.RunAsync(option, context);
        if (result.Status != "succeeded")
        {
            Environment.ExitCode = 1;
            if (result.ErrorMessage != null)
                Logger.Error(result.ErrorMessage);
        }
    }

    internal static async Task CheckUpdateAsync()
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
            string nowVer = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            string redirctUrl = await Get302Async("https://github.com/nilaoda/N_m3u8DL-RE/releases/latest");
            string latestVer = redirctUrl.Replace("https://github.com/nilaoda/N_m3u8DL-RE/releases/tag/", "");
            if (!latestVer.StartsWith(nowVer) && !latestVer.StartsWith("https"))
            {
                Console.Title = $"{ResString.newVersionFound} {latestVer}";
                Logger.InfoMarkUp($"[turquoise4]{ResString.newVersionFound}[/] [red]{latestVer}[/]");
            }
        }
        catch (Exception)
        {
            ;
        }
    }

    // 重定向
    static async Task<string> Get302Async(string url)
    {
        // this allows you to set the settings so that we can get the redirect url
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectCallback = HTTPUtil.HttpHandler.ConnectCallback,
        };
        var redirectedUrl = "";
        using var client = new HttpClient(handler);
        using var response = await client.GetAsync(url);
        using var content = response.Content;
        // ... Read the response to see if we have the redirected url
        if (response.StatusCode != HttpStatusCode.Found) return redirectedUrl;
        
        var headers = response.Headers;
        if (headers.Location != null)
        {
            redirectedUrl = headers.Location.AbsoluteUri;
        }

        return redirectedUrl;
    }
}
