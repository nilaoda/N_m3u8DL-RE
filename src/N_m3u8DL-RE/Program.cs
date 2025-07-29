using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;

using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.CommonEnumerations;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Enumerations;
using N_m3u8DL_RE.StreamParser;
using N_m3u8DL_RE.StreamParser.Config;
using N_m3u8DL_RE.Processor;
using N_m3u8DL_RE.Util;

using Spectre.Console;

// 处理NT6.0及以下System.CommandLine报错CultureNotFound问题
if (OperatingSystem.IsWindows())
{
    Version osVersion = Environment.OSVersion.Version;
    if (osVersion.Major < 6 || osVersion is { Major: 6, Minor: 0 })
    {
        Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1");
    }
}

Console.CancelKeyPress += Console_CancelKeyPress;
try { Console.CursorVisible = true; } catch { }

string loc = ResString.CurrentLoc;
string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
if (currLoc is "zh-CN" or "zh-SG")
{
    loc = "zh-CN";
}
else if (currLoc.StartsWith("zh-"))
{
    loc = "zh-TW";
}

// 处理用户-h等请求
int index = -1;
List<string> list = [.. args];
if ((index = list.IndexOf("--ui-language")) != -1 && list.Count > index + 1 && new List<string> { "en-US", "zh-CN", "zh-TW" }.Contains(list[index + 1]))
{
    loc = list[index + 1];
}

ResString.CurrentLoc = loc;

try
{
    CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(loc);
    Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(loc);
    Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(loc);
}
catch
{
    // Culture not work on NT6.0, so catch the exception
}

await CommandInvoker.InvokeArgs(args, DoWorkAsync);

void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
{
    Logger.WarnMarkUp("Force Exit...");
    try
    {
        Console.CursorVisible = true;
        if (!OperatingSystem.IsWindows())
        {
            Process.Start("tput", "cnorm");
        }
    }
    catch { }
    Environment.Exit(0);
}

int GetOrder(StreamSpec streamSpec)
{
    if (streamSpec.Channels == null)
    {
        return 0;
    }

    string str = streamSpec.Channels.Split('/')[0];
    return int.TryParse(str, out int order) ? order : 0;
}

async Task DoWorkAsync(MyOption option)
{
    HTTPUtil.AppHttpClient.Timeout = TimeSpan.FromSeconds(option.HttpRequestTimeout);
    if (Console.IsOutputRedirected || Console.IsErrorRedirected)
    {
        option.ForceAnsiConsole = true;
        option.NoAnsiColor = true;
        Logger.Info(ResString.ConsoleRedirected);
    }
    CustomAnsiConsole.InitConsole(option.ForceAnsiConsole, option.NoAnsiColor);

    // 检测更新
    if (!option.DisableUpdateCheck)
    {
        _ = CheckUpdateAsync();
    }

    Logger.IsWriteFile = !option.NoLog;
    Logger.LogFilePath = option.LogFilePath;
    Logger.InitLogFile();
    Logger.LogLevel = option.LogLevel;
    Logger.Info(CommandInvoker.VERSION_INFO);

    if (!option.UseSystemProxy)
    {
        HTTPUtil.HttpClientHandler.UseProxy = false;
    }

    if (option.CustomProxy != null)
    {
        HTTPUtil.HttpClientHandler.Proxy = option.CustomProxy;
        HTTPUtil.HttpClientHandler.UseProxy = true;
    }

    // 检查互斥的选项
    if (option is { MuxAfterDone: false, MuxImports.Count: > 0 })
    {
        throw new ArgumentException("MuxAfterDone disabled, MuxImports not allowed!");
    }

    if (option.UseShakaPackager)
    {
        option.DecryptionEngine = DecryptEngine.SHAKA_PACKAGER;
    }

    // LivePipeMux开启时 LiveRealTimeMerge必须开启
    if (option is { LivePipeMux: true, LiveRealTimeMerge: false })
    {
        Logger.WarnMarkUp("LivePipeMux detected, forced enable LiveRealTimeMerge");
        option.LiveRealTimeMerge = true;
    }

    // 预先检查ffmpeg
    option.FFmpegBinaryPath ??= GlobalUtil.FindExecutable("ffmpeg");

    if (string.IsNullOrEmpty(option.FFmpegBinaryPath) || !File.Exists(option.FFmpegBinaryPath))
    {
        throw new FileNotFoundException(ResString.FfmpegNotFound);
    }

    Logger.Extra($"ffmpeg => {option.FFmpegBinaryPath}");

    // 预先检查mkvmerge
    if (option is { MuxOptions.UseMkvmerge: true, MuxAfterDone: true })
    {
        option.MkvmergeBinaryPath ??= GlobalUtil.FindExecutable("mkvmerge");
        if (string.IsNullOrEmpty(option.MkvmergeBinaryPath) || !File.Exists(option.MkvmergeBinaryPath))
        {
            throw new FileNotFoundException(ResString.MkvmergeNotFound);
        }
        Logger.Extra($"mkvmerge => {option.MkvmergeBinaryPath}");
    }

    // 预先检查
    if (option.Keys is { Length: > 0 } || option.KeyTextFile != null)
    {
        if (!string.IsNullOrEmpty(option.DecryptionBinaryPath) && !File.Exists(option.DecryptionBinaryPath))
        {
            throw new FileNotFoundException(option.DecryptionBinaryPath);
        }
        switch (option.DecryptionEngine)
        {
            case DecryptEngine.SHAKA_PACKAGER:
                {
                    string? file = GlobalUtil.FindExecutable("shaka-packager");
                    string? file2 = GlobalUtil.FindExecutable("packager-linux-x64");
                    string? file3 = GlobalUtil.FindExecutable("packager-osx-x64");
                    string? file4 = GlobalUtil.FindExecutable("packager-win-x64");
                    if (file == null && file2 == null && file3 == null && file4 == null)
                    {
                        throw new FileNotFoundException(ResString.ShakaPackagerNotFound);
                    }

                    option.DecryptionBinaryPath = file ?? file2 ?? file3 ?? file4;
                    Logger.Extra($"shaka-packager => {option.DecryptionBinaryPath}");
                    break;
                }
            case DecryptEngine.MP4DECRYPT:
                {
                    string? file = GlobalUtil.FindExecutable("mp4decrypt") ?? throw new FileNotFoundException(ResString.Mp4decryptNotFound);
                    option.DecryptionBinaryPath = file;
                    Logger.Extra($"mp4decrypt => {option.DecryptionBinaryPath}");
                    break;
                }
            case DecryptEngine.FFMPEG:
            default:
                option.DecryptionBinaryPath = option.FFmpegBinaryPath;
                break;
        }
    }

    // 默认的headers
    Dictionary<string, string> headers = new()
    {
        ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
    };
    // 添加或替换用户输入的headers
    foreach (KeyValuePair<string, string> item in option.Headers)
    {
        headers[item.Key] = item.Value;
        Logger.Extra($"User-Defined Header => {item.Key}: {item.Value}");
    }

    ParserConfig parserConfig = new()
    {
        AppendUrlParams = option.AppendUrlParams,
        UrlProcessorArgs = option.UrlProcessorArgs,
        BaseUrl = option.BaseUrl!,
        Headers = headers,
        CustomMethod = option.CustomHLSMethod,
        CustomeKey = option.CustomHLSKey,
        CustomeIV = option.CustomHLSIv,
    };

    if (option.AllowHlsMultiExtMap)
    {
        parserConfig.CustomParserArgs.Add("AllowHlsMultiExtMap", "true");
    }

    // demo1
    parserConfig.ContentProcessors.Insert(0, new DemoProcessor());
    // demo2
    parserConfig.KeyProcessors.Insert(0, new DemoProcessor2());
    // for www.nowehoryzonty.pl
    parserConfig.UrlProcessors.Insert(0, new NowehoryzontyUrlProcessor());

    // 等待任务开始时间
    if (option.TaskStartAt != null && option.TaskStartAt > DateTime.Now)
    {
        Logger.InfoMarkUp(ResString.TaskStartAt + option.TaskStartAt);
        while (option.TaskStartAt > DateTime.Now)
        {
            await Task.Delay(1000);
        }
    }

    string url = option.Input;

    // 流提取器配置
    StreamExtractor extractor = new(parserConfig);
    // 从链接加载内容
    await RetryUtil.WebRequestRetryAsync(async () =>
    {
        await extractor.LoadSourceFromUrlAsync(url);
        return true;
    });
    // 解析流信息
    List<StreamSpec> streams = await extractor.ExtractStreamsAsync();


    // 全部媒体
    List<StreamSpec> lists = [.. streams.OrderBy(p => p.MediaType).ThenByDescending(p => p.Bandwidth).ThenByDescending(GetOrder)];
    // 基本流
    List<StreamSpec> basicStreams = [.. lists.Where(x => x.MediaType is null or MediaType.VIDEO)];
    // 可选音频轨道
    List<StreamSpec> audios = [.. lists.Where(x => x.MediaType == MediaType.AUDIO)];
    // 可选字幕轨道
    List<StreamSpec> subs = [.. lists.Where(x => x.MediaType == MediaType.SUBTITLES)];

    // 尝试从URL或文件读取文件名
    if (string.IsNullOrEmpty(option.SaveName))
    {
        option.SaveName = OtherUtil.GetFileNameFromInput(option.Input);
    }

    // 生成文件夹
    string tmpDir = Path.Combine(option.TmpDir ?? Environment.CurrentDirectory, $"{option.SaveName ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}");
    // 记录文件
    extractor.RawFiles["meta.json"] = GlobalUtil.ConvertToJson(lists);
    // 写出文件
    await WriteRawFilesAsync(option, extractor, tmpDir);

    Logger.Info(ResString.StreamsInfo, lists.Count, basicStreams.Count, audios.Count, subs.Count);

    foreach (StreamSpec? item in lists)
    {
        Logger.InfoMarkUp(item.ToString());
    }

    List<StreamSpec> selectedStreams = [];
    if (option.DropVideoFilter != null || option.DropAudioFilter != null || option.DropSubtitleFilter != null)
    {
        basicStreams = FilterUtil.DoFilterDrop(basicStreams, option.DropVideoFilter);
        audios = FilterUtil.DoFilterDrop(audios, option.DropAudioFilter);
        subs = FilterUtil.DoFilterDrop(subs, option.DropSubtitleFilter);
        lists = [.. basicStreams, .. audios, .. subs];
    }

    if (option.DropVideoFilter != null)
    {
        Logger.Extra($"DropVideoFilter => {option.DropVideoFilter}");
    }

    if (option.DropAudioFilter != null)
    {
        Logger.Extra($"DropAudioFilter => {option.DropAudioFilter}");
    }

    if (option.DropSubtitleFilter != null)
    {
        Logger.Extra($"DropSubtitleFilter => {option.DropSubtitleFilter}");
    }

    if (option.VideoFilter != null)
    {
        Logger.Extra($"VideoFilter => {option.VideoFilter}");
    }

    if (option.AudioFilter != null)
    {
        Logger.Extra($"AudioFilter => {option.AudioFilter}");
    }

    if (option.SubtitleFilter != null)
    {
        Logger.Extra($"SubtitleFilter => {option.SubtitleFilter}");
    }

    if (option.AutoSelect)
    {
        if (basicStreams.Count != 0)
        {
            selectedStreams.Add(basicStreams.First());
        }

        IEnumerable<string?> langs = audios.DistinctBy(a => a.Language).Select(a => a.Language);
        foreach (string? lang in langs)
        {
            selectedStreams.Add(audios.Where(a => a.Language == lang).OrderByDescending(a => a.Bandwidth).ThenByDescending(GetOrder).First());
        }
        selectedStreams.AddRange(subs);
    }
    else if (option.SubOnly)
    {
        selectedStreams.AddRange(subs);
    }
    else if (option.VideoFilter != null || option.AudioFilter != null || option.SubtitleFilter != null)
    {
        basicStreams = FilterUtil.DoFilterKeep(basicStreams, option.VideoFilter);
        audios = FilterUtil.DoFilterKeep(audios, option.AudioFilter);
        subs = FilterUtil.DoFilterKeep(subs, option.SubtitleFilter);
        selectedStreams = [.. basicStreams, .. audios, .. subs];
    }
    else
    {
        // 展示交互式选择框
        selectedStreams = FilterUtil.SelectStreams(lists);
    }

    if (selectedStreams.Count == 0)
    {
        throw new InvalidOperationException(ResString.NoStreamsToDownload);
    }

    // HLS: 选中流中若有没加载出playlist的，加载playlist
    // DASH/MSS: 加载playlist (调用url预处理器)
    if (selectedStreams.Any(s => s.Playlist == null) || extractor.ExtractorType == ExtractorType.MPEGDASH || extractor.ExtractorType == ExtractorType.MSS)
    {
        await extractor.FetchPlayListAsync(selectedStreams);
    }

    // 直播检测
    bool livingFlag = selectedStreams.Any(s => s.Playlist?.IsLive == true) && !option.LivePerformAsVod;
    if (livingFlag)
    {
        Logger.WarnMarkUp($"[white on darkorange3_1]{ResString.LiveFound}[/]");
    }

    // 无法识别的加密方式，自动开启二进制合并
    if (selectedStreams.Any(s => s.Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.EncryptInfo.Method == EncryptMethod.UNKNOWN))))
    {
        Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge3}[/]");
        option.BinaryMerge = true;
    }

    // 应用用户自定义的分片范围
    if (!livingFlag)
    {
        FilterUtil.ApplyCustomRange(selectedStreams, option.CustomRange);
    }

    // 应用用户自定义的广告分片关键字
    FilterUtil.CleanAd(selectedStreams, option.AdKeywords);

    // 记录文件
    extractor.RawFiles["meta_selected.json"] = GlobalUtil.ConvertToJson(selectedStreams);

    Logger.Info(ResString.SelectedStream);
    foreach (StreamSpec item in selectedStreams)
    {
        Logger.InfoMarkUp(item.ToString());
    }

    // 写出文件
    await WriteRawFilesAsync(option, extractor, tmpDir);

    if (option.SkipDownload)
    {
        return;
    }

#if DEBUG
    Console.WriteLine("Press any key to continue...");
    Console.ReadKey();
#endif

    Logger.InfoMarkUp(ResString.SaveName + $"[deepskyblue1]{option.SaveName.EscapeMarkup()}[/]");

    // 开始MuxAfterDone后自动使用二进制版
    if (option is { BinaryMerge: false, MuxAfterDone: true })
    {
        option.BinaryMerge = true;
        Logger.WarnMarkUp($"[darkorange3_1]{ResString.AutoBinaryMerge6}[/]");
    }

    // 下载配置
    DownloaderConfig downloadConfig = new()
    {
        MyOptions = option,
        DirPrefix = tmpDir,
        Headers = parserConfig.Headers, // 使用命令行解析得到的Headers
    };

    bool result = false;

    if (extractor.ExtractorType == ExtractorType.HTTPLIVE)
    {
        HTTPLiveRecordManager sldm = new(downloadConfig, selectedStreams, extractor);
        result = await sldm.StartRecordAsync();
    }
    else if (!livingFlag)
    {
        // 开始下载
        SimpleDownloadManager sdm = new(downloadConfig, selectedStreams, extractor);
        result = await sdm.StartDownloadAsync();
    }
    else
    {
        SimpleLiveRecordManager2 sldm = new(downloadConfig, selectedStreams, extractor);
        result = await sldm.StartRecordAsync();
    }

    if (result)
    {
        Logger.InfoMarkUp("[white on green]Done[/]");
    }
    else
    {
        Logger.ErrorMarkUp("[white on red]Failed[/]");
        Environment.ExitCode = 1;
    }
}

async Task WriteRawFilesAsync(MyOption option, StreamExtractor extractor, string tmpDir)
{
    // 写出json文件
    if (option.WriteMetaJson)
    {
        if (!Directory.Exists(tmpDir))
        {
            Directory.CreateDirectory(tmpDir);
        }

        Logger.Warn(ResString.WriteJson);
        foreach (KeyValuePair<string, string> item in extractor.RawFiles)
        {
            string file = Path.Combine(tmpDir, item.Key);
            if (!File.Exists(file))
            {
                await File.WriteAllTextAsync(file, item.Value, Encoding.UTF8);
            }
        }
    }
}

async Task CheckUpdateAsync()
{
    try
    {
        Version ver = Assembly.GetExecutingAssembly().GetName().Version!;
        string nowVer = $"v{ver.Major}.{ver.Minor}.{ver.Build}";
        string redirctUrl = await Get302Async("https://github.com/nilaoda/N_m3u8DL-RE/releases/latest");
        string latestVer = redirctUrl.Replace("https://github.com/nilaoda/N_m3u8DL-RE/releases/tag/", "");
        if (!latestVer.StartsWith(nowVer) && !latestVer.StartsWith("https"))
        {
            Console.Title = $"{ResString.NewVersionFound} {latestVer}";
            Logger.InfoMarkUp($"[cyan]{ResString.NewVersionFound}[/] [red]{latestVer}[/]");
        }
    }
    catch (Exception)
    {
        ;
    }
}

// 重定向
async Task<string> Get302Async(string url)
{
    // this allows you to set the settings so that we can get the redirect url
    HttpClientHandler handler = new()
    {
        AllowAutoRedirect = false
    };
    string redirectedUrl = "";
    using HttpClient client = new(handler);
    using HttpResponseMessage response = await client.GetAsync(url);
    using HttpContent content = response.Content;
    // ... Read the response to see if we have the redirected url
    if (response.StatusCode != HttpStatusCode.Found)
    {
        return redirectedUrl;
    }

    HttpResponseHeaders headers = response.Headers;
    if (headers.Location != null)
    {
        redirectedUrl = headers.Location.AbsoluteUri;
    }

    return redirectedUrl;
}
