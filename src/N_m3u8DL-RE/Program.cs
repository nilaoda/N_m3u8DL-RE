using System.Globalization;
using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using Spectre.Console;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using System.Text;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Plugin;
using N_m3u8DL_RE.Processor;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.CommandLine;
using System.Net;
using N_m3u8DL_RE.Enum;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;

namespace N_m3u8DL_RE;

internal class Program
{
    static async Task Main(string[] args)
    {
        // 初始化插件系统
        // 由于命名空间问题，直接通过反射调用
        try 
        {
            var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
            if (pluginManagerType != null)
            {
                var loadPluginsMethod = pluginManagerType.GetMethod("LoadPlugins");
                if (loadPluginsMethod != null)
                {
                    loadPluginsMethod.Invoke(null, null);
                    Console.WriteLine("[Plugin] Plugin system initialized");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Plugin] Failed to initialize plugin system: {ex.Message}");
        }
        
        // 处理NT6.0及以下System.CommandLine报错CultureNotFound问题
        if (OperatingSystem.IsWindows()) 
        {
            var osVersion = Environment.OSVersion.Version;
            if (osVersion.Major < 6 || osVersion is { Major: 6, Minor: 0 })
            {
                Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1");
            }
        }
        
        Console.CancelKeyPress += Console_CancelKeyPress;
        ServicePointManager.DefaultConnectionLimit = 1024;
        try { Console.CursorVisible = true; } catch { }

        string loc = ResString.CurrentLoc;
        string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
        if (currLoc is "zh-CN" or "zh-SG") loc = "zh-CN";
        else if (currLoc.StartsWith("zh-")) loc = "zh-TW";

        // 处理用户-h等请求
        var index = -1;
        var list = new List<string>(args);
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
    }

    private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        Logger.WarnMarkUp("Force Exit...");
        try 
        { 
            Console.CursorVisible = true;
            if (!OperatingSystem.IsWindows())
                System.Diagnostics.Process.Start("tput", "cnorm");
        } catch { }
        Environment.Exit(0);
    }

    static int GetOrder(StreamSpec streamSpec)
    {
        if (streamSpec.Channels == null) return 0;
            
        var str = streamSpec.Channels.Split('/')[0];
        return int.TryParse(str, out var order) ? order : 0;
    }

    static async Task DoWorkAsync(MyOption option)
    {
        HTTPUtil.AppHttpClient.Timeout = TimeSpan.FromSeconds(option.HttpRequestTimeout);
        if (Console.IsOutputRedirected || Console.IsErrorRedirected)
        {
            option.ForceAnsiConsole = true;
            option.NoAnsiColor = true;
            Logger.Info(ResString.consoleRedirected);
        }
        CustomAnsiConsole.InitConsole(option.ForceAnsiConsole, option.NoAnsiColor);
        
        // 检测更新
        if (!option.DisableUpdateCheck)
            _ = CheckUpdateAsync();

        Logger.IsWriteFile = !option.NoLog;
        Logger.LogFilePath = option.LogFilePath;
        Logger.InitLogFile();
        Logger.LogLevel = option.LogLevel;
        Logger.Info(CommandInvoker.VERSION_INFO);

        if (option.UseSystemProxy == false)
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
            throw new FileNotFoundException(ResString.ffmpegNotFound);
        }

        Logger.Extra($"ffmpeg => {option.FFmpegBinaryPath}");

        // 预先检查mkvmerge
        if (option is { MuxOptions.UseMkvmerge: true, MuxAfterDone: true })
        {
            option.MkvmergeBinaryPath ??= GlobalUtil.FindExecutable("mkvmerge");
            if (string.IsNullOrEmpty(option.MkvmergeBinaryPath) || !File.Exists(option.MkvmergeBinaryPath))
            {
                throw new FileNotFoundException(ResString.mkvmergeNotFound);
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
                    var file = GlobalUtil.FindExecutable("shaka-packager");
                    var file2 = GlobalUtil.FindExecutable("packager-linux-x64");
                    var file3 = GlobalUtil.FindExecutable("packager-osx-x64");
                    var file4 = GlobalUtil.FindExecutable("packager-win-x64");
                    if (file == null && file2 == null && file3 == null && file4 == null)
                        throw new FileNotFoundException(ResString.shakaPackagerNotFound);
                    option.DecryptionBinaryPath = file ?? file2 ?? file3 ?? file4;
                    Logger.Extra($"shaka-packager => {option.DecryptionBinaryPath}");
                    break;
                }
                case DecryptEngine.MP4DECRYPT:
                {
                    var file = GlobalUtil.FindExecutable("mp4decrypt");
                    if (file == null) throw new FileNotFoundException(ResString.mp4decryptNotFound);
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
        var headers = new Dictionary<string, string>()
        {
            ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
        };
        // 添加或替换用户输入的headers
        foreach (var item in option.Headers)
        {
            headers[item.Key] = item.Value;
            Logger.Extra($"User-Defined Header => {item.Key}: {item.Value}");
        }

        var parserConfig = new ParserConfig()
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
            Logger.InfoMarkUp(ResString.taskStartAt + option.TaskStartAt);
            while (option.TaskStartAt > DateTime.Now)
            {
                await Task.Delay(1000);
            }
        }

        // 检查是否启用批量下载插件
        bool batchDownloadEnabled = false;
        dynamic? batchPlugin = null;
        
        try 
        {
            var pluginManagerType = Type.GetType("N_m3u8DL_RE.Plugin.PluginManager, N_m3u8DL-RE");
            if (pluginManagerType != null)
            {
                // 使用新的配置提取方法获取批处理下载启用状态
                var extractEnabledMethod = pluginManagerType.GetMethod("ExtractBatchDownloadEnabledFromConfig", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (extractEnabledMethod != null)
                {
                    batchDownloadEnabled = (bool?)extractEnabledMethod.Invoke(null, null) ?? false;
                }
                
                // 获取批量下载插件实例（无论是否启用，因为用户可能显式使用--batch参数）
                try
                {
                    var getPluginsMethod = pluginManagerType.GetMethod("GetPlugins");
                    if (getPluginsMethod != null)
                    {
                        var plugins = getPluginsMethod.Invoke(null, null) as List<IPlugin>;
                        if (plugins != null)
                        {
                            foreach (var plugin in plugins)
                            {
                                if (plugin.GetType().Name == "BatchDownloadPlugin")
                                {
                                    batchPlugin = plugin;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BatchDownload] Failed to get plugins: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BatchDownload] Failed to check batch download status: {ex.Message}");
        }

        // 如果启用批量下载且有URL列表，或者用户显式指定了批量模式，则执行批量下载
        if (option.BatchMode || (batchDownloadEnabled && batchPlugin != null))
        {
            Console.WriteLine($"[BatchDownload] Detecting batch mode... BatchMode={option.BatchMode}, PluginEnabled={batchDownloadEnabled}, PluginInstance={batchPlugin != null}");
            
            // 如果用户显式指定了批量模式，则执行批量下载
            if (option.BatchMode)
            {
                Console.WriteLine("[BatchDownload] Batch mode explicitly enabled by user");
                await ExecuteBatchDownload(batchPlugin, option);
                return;
            }
            else if (batchPlugin != null)
            {
                var hasUrlsMethod = batchPlugin.GetType().GetMethod("HasUrls");
                if (hasUrlsMethod?.Invoke(batchPlugin, null) as bool? == true)
                {
                    Console.WriteLine("[BatchDownload] Batch download plugin detected and has URLs");
                    await ExecuteBatchDownload(batchPlugin, option);
                    return;
                }
                else
                {
                    Console.WriteLine("[BatchDownload] Plugin detected but no URLs available");
                }
            }
            else
            {
                Console.WriteLine("[BatchDownload] Batch download configuration found but plugin instance not available");
            }
        }

        // 如果没有输入URL且没有启用批量下载，显示帮助信息
        if (string.IsNullOrEmpty(option.Input) && !option.BatchMode)
        {
            Console.WriteLine("Error: No input URL provided and batch download is not enabled.");
            Console.WriteLine("Please provide a URL with --input or use --batch to enable batch download mode");
            return;
        }

        var url = option.Input;

        // 流提取器配置
        var extractor = new StreamExtractor(parserConfig);
        // 从链接加载内容
        await RetryUtil.WebRequestRetryAsync(async () =>
        {
            await extractor.LoadSourceFromUrlAsync(url);
            return true;
        });
        // 解析流信息
        var streams = await extractor.ExtractStreamsAsync();


        // 全部媒体
        var lists = streams.OrderBy(p => p.MediaType).ThenByDescending(p => p.Bandwidth).ThenByDescending(GetOrder).ToList();
        // 基本流
        var basicStreams = lists.Where(x => x.MediaType is null or MediaType.VIDEO).ToList();
        // 可选音频轨道
        var audios = lists.Where(x => x.MediaType == MediaType.AUDIO).ToList();
        // 可选字幕轨道
        var subs = lists.Where(x => x.MediaType == MediaType.SUBTITLES).ToList();

        // 尝试从URL或文件读取文件名
        if (string.IsNullOrEmpty(option.SaveName))
        {
            option.SaveName = OtherUtil.GetFileNameFromInput(option.Input);
        }

        // 生成文件夹
        var baseDir = option.SaveDir ?? option.TmpDir ?? Environment.CurrentDirectory;
        var tmpDir = Path.Combine(baseDir, $"{option.SaveName ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}");
        // 记录文件
        if (option.WriteMetaJson)
        {
            extractor.RawFiles["meta.json"] = GlobalUtil.ConvertToJson(lists);
        }
        // 写出文件
        await WriteRawFilesAsync(option, extractor, tmpDir);

        Logger.Info(ResString.streamsInfo, lists.Count, basicStreams.Count, audios.Count, subs.Count);

        foreach (var item in lists)
        {
            Logger.InfoMarkUp(item.ToString());
        }

        var selectedStreams = new List<StreamSpec>();
        if (option.DropVideoFilter != null || option.DropAudioFilter != null || option.DropSubtitleFilter != null)
        {
            basicStreams = FilterUtil.DoFilterDrop(basicStreams, option.DropVideoFilter);
            audios = FilterUtil.DoFilterDrop(audios, option.DropAudioFilter);
            subs = FilterUtil.DoFilterDrop(subs, option.DropSubtitleFilter);
            lists = basicStreams.Concat(audios).Concat(subs).ToList();
        }

        if (option.DropVideoFilter != null) Logger.Extra($"DropVideoFilter => {option.DropVideoFilter}");
        if (option.DropAudioFilter != null) Logger.Extra($"DropAudioFilter => {option.DropAudioFilter}");
        if (option.DropSubtitleFilter != null) Logger.Extra($"DropSubtitleFilter => {option.DropSubtitleFilter}");
        if (option.VideoFilter != null) Logger.Extra($"VideoFilter => {option.VideoFilter}");
        if (option.AudioFilter != null) Logger.Extra($"AudioFilter => {option.AudioFilter}");
        if (option.SubtitleFilter != null) Logger.Extra($"SubtitleFilter => {option.SubtitleFilter}");

        if (option.AutoSelect)
        {
            if (basicStreams.Count != 0)
                selectedStreams.Add(basicStreams.First());
            var langs = audios.DistinctBy(a => a.Language).Select(a => a.Language);
            foreach (var lang in langs)
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
            selectedStreams = basicStreams.Concat(audios).Concat(subs).ToList();
        }
        else
        {
            // 展示交互式选择框
            selectedStreams = FilterUtil.SelectStreams(lists);
        }

        if (selectedStreams.Count == 0)
            throw new Exception(ResString.noStreamsToDownload);

        // HLS: 选中流中若有没加载出playlist的，加载playlist
        // DASH/MSS: 加载playlist (调用url预处理器)
        if (selectedStreams.Any(s => s.Playlist == null) || extractor.ExtractorType == ExtractorType.MPEG_DASH || extractor.ExtractorType == ExtractorType.MSS)
            await extractor.FetchPlayListAsync(selectedStreams);

        // 直播检测
        var livingFlag = selectedStreams.Any(s => s.Playlist?.IsLive == true) && !option.LivePerformAsVod;
        if (livingFlag)
        {
            Logger.WarnMarkUp($"[white on darkorange3_1]{ResString.liveFound}[/]");
        }

        // 无法识别的加密方式，自动开启二进制合并
        if (selectedStreams.Any(s => s.Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.EncryptInfo.Method == EncryptMethod.UNKNOWN))))
        {
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge3}[/]");
            option.BinaryMerge = true;
        }

        // 应用用户自定义的分片范围
        if (!livingFlag)
            FilterUtil.ApplyCustomRange(selectedStreams, option.CustomRange);

        // 应用用户自定义的广告分片关键字
        FilterUtil.CleanAd(selectedStreams, option.AdKeywords);

        // 记录文件
        if (option.WriteMetaJson)
        {
            extractor.RawFiles["meta_selected.json"] = GlobalUtil.ConvertToJson(selectedStreams);
        }

        Logger.Info(ResString.selectedStream);
        foreach (var item in selectedStreams)
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

        Logger.InfoMarkUp(ResString.saveName + $"[deepskyblue1]{option.SaveName.EscapeMarkup()}[/]");

        // 开始MuxAfterDone后自动使用二进制版
        if (option is { BinaryMerge: false, MuxAfterDone: true })
        {
            option.BinaryMerge = true;
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge6}[/]");
        }

        // 下载配置
        var downloadConfig = new DownloaderConfig()
        {
            MyOptions = option,
            DirPrefix = tmpDir,
            Headers = parserConfig.Headers, // 使用命令行解析得到的Headers
        };

        var result = false;

        if (extractor.ExtractorType == ExtractorType.HTTP_LIVE)
        {
            var sldm = new HTTPLiveRecordManager(downloadConfig, selectedStreams, extractor);
            result = await sldm.StartRecordAsync();
        }
        else if (!livingFlag)
        {
            // 开始下载
            var sdm = new SimpleDownloadManager(downloadConfig, selectedStreams, extractor);
            result = await sdm.StartDownloadAsync();
        }
        else
        {
            var sldm = new SimpleLiveRecordManager2(downloadConfig, selectedStreams, extractor);
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

    private static async Task WriteRawFilesAsync(MyOption option, StreamExtractor extractor, string tmpDir)
    {
        // 写出json文件
        if (option.WriteMetaJson)
        {
            if (!Directory.Exists(tmpDir)) Directory.CreateDirectory(tmpDir);
            Logger.Warn(ResString.writeJson);
            foreach (var item in extractor.RawFiles)
            {
                var file = Path.Combine(tmpDir, item.Key);
                if (!File.Exists(file)) await File.WriteAllTextAsync(file, item.Value, Encoding.UTF8);
            }
        }
    }

    static async Task ExecuteBatchDownload(dynamic batchPlugin, MyOption option)
    {
        List<string> urls = new List<string>();
        bool createSubdirectories = false;
        
        try
        {
            // 如果有batchPlugin，从插件获取URL列表和配置
            if (batchPlugin != null)
            {
                Logger.Info($"[BatchDebug] batchPlugin type: {batchPlugin.GetType().Name}");
                
                try
                {
                    if (batchPlugin is N_m3u8DL_RE.Plugin.BatchDownloadPlugin realPlugin)
                    {
                        urls = realPlugin.GetUrlList();
                        var config = realPlugin.GetConfig();
                        
                        // 从匿名对象中提取CreateSubdirectories配置
                        var createSubdirsProperty = config?.GetType().GetProperty("CreateSubdirectories");
                        createSubdirectories = createSubdirsProperty?.GetValue(config) as bool? == true;
                        
                        // 获取输出目录配置并设置到option中
                        var outputDirectory = realPlugin.GetOutputDirectory();
                        if (!string.IsNullOrEmpty(outputDirectory) && Directory.Exists(outputDirectory))
                        {
                            // 如果用户没有显式指定输出目录，使用配置文件中的目录
                            if (string.IsNullOrEmpty(option.SaveDir))
                            {
                                option.SaveDir = outputDirectory;
                                Logger.Info($"[BatchDownload] Using configured output directory: {outputDirectory}");
                            }
                            else
                            {
                                Logger.Info($"[BatchDownload] Using user-specified output directory: {option.SaveDir}");
                            }
                        }
                        
                        Logger.Info($"[BatchDebug] Direct method calls successful. URLs: {urls.Count}, CreateSubdirectories: {createSubdirectories}");
                    }
                    else
                    {
                        // 备用反射调用方法
                        var getUrlListMethod = batchPlugin.GetType().GetMethod("GetUrlList");
                        var getConfigMethod = batchPlugin.GetType().GetMethod("GetConfig");
                        
                        if (getUrlListMethod != null)
                        {
                            urls = getUrlListMethod.Invoke(batchPlugin, null) as List<string> ?? new List<string>();
                        }
                        
                        if (getConfigMethod != null)
                        {
                            var config = getConfigMethod.Invoke(batchPlugin, null);
                            var createSubdirsProperty = config?.GetType().GetProperty("CreateSubdirectories");
                            createSubdirectories = createSubdirsProperty?.GetValue(config) as bool? == true;
                        }
                        
                        Logger.Info($"[BatchDebug] Reflection method calls. URLs: {urls.Count}, CreateSubdirectories: {createSubdirectories}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[BatchDebug] Failed to call plugin methods: {ex.Message}");
                }
            }
            
            // 如果没有从插件获取到URL列表，批量下载无法继续
            if (urls.Count == 0)
            {
                Logger.Error("[BatchDownload] No URLs available. Please check plugin configuration and batch file.");
                return;
            }
            
            Logger.InfoMarkUp($"[BatchDownload] Starting batch download with {urls.Count} URLs");
            
            int successCount = 0;
            int failCount = 0;
            
            for (int i = 0; i < urls.Count; i++)
            {
                var url = urls[i];
                Logger.InfoMarkUp($"[BatchDownload] Processing URL {i + 1}/{urls.Count}: {url}");
                
                try
                {
                    // 重置SaveName，确保每个URL都能生成唯一文件名
                    option.SaveName = null;
                    
                    // 创建子目录（如果配置允许）
                    var originalSaveDir = option.SaveDir;
                    if (createSubdirectories)
                    {
                        var subDir = Path.Combine(originalSaveDir ?? ".", $"batch_item_{i + 1}");
                        Directory.CreateDirectory(subDir);
                        option.SaveDir = subDir;
                    }
                    
                    // 执行单个URL下载，传递批量下载索引和URL信息
                    await ExecuteSingleDownload(url, option, i + 1, urls.Count, batchDownload: true);
                    successCount++;
                    
                    // 恢复原始保存目录
                    option.SaveDir = originalSaveDir;
                }
                catch (Exception ex)
                {
                    Logger.ErrorMarkUp($"[BatchDownload] Failed to download URL {i + 1}: {ex.Message}");
                    failCount++;
                }
            }
            
            Logger.InfoMarkUp($"[BatchDownload] Batch download completed. Success: {successCount}, Failed: {failCount}");
        }
        catch (Exception ex)
        {
            Logger.ErrorMarkUp($"[BatchDownload] Error during batch download: {ex.Message}");
        }
    }

    static async Task ExecuteSingleDownload(string url, MyOption option, int batchIndex = 0, int totalBatches = 0, bool batchDownload = false)
    {
        bool livingFlag = false; // 声明livingFlag变量
        
        // 创建解析器配置
        var headers = new Dictionary<string, string>()
        {
            ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
        };
        
        foreach (var item in option.Headers)
        {
            headers[item.Key] = item.Value;
        }

        var parserConfig = new ParserConfig()
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

        // 流提取器配置
        var extractor = new StreamExtractor(parserConfig);
        
        // 从链接加载内容
        await RetryUtil.WebRequestRetryAsync(async () =>
        {
            await extractor.LoadSourceFromUrlAsync(url);
            return true;
        });
        
        // 解析流信息
        var streams = await extractor.ExtractStreamsAsync();

        // 设置保存名称
        if (string.IsNullOrEmpty(option.SaveName))
        {
            if (batchDownload && batchIndex > 0)
            {
                // 批量下载模式下生成唯一文件名
                var baseName = GetUniqueFileNameFromUrl(url, batchIndex, totalBatches);
                option.SaveName = baseName;
            }
            else
            {
                option.SaveName = OtherUtil.GetFileNameFromInput(url);
            }
        }

        // 生成文件夹
        var baseDir = option.SaveDir ?? option.TmpDir ?? Environment.CurrentDirectory;
        var tmpDir = Path.Combine(baseDir, $"{option.SaveName ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")}");
        
        // 记录文件
        if (option.WriteMetaJson)
        {
            extractor.RawFiles["meta.json"] = GlobalUtil.ConvertToJson(streams);
        }

        // 写出文件
        await WriteRawFilesAsync(option, extractor, tmpDir);

        Logger.Info($"Streams info: {streams.Count} streams found");

        // 选择流（简化逻辑，使用自动选择）
        var selectedStreams = new List<StreamSpec>();
        var basicStreams = streams.Where(x => x.MediaType is null or MediaType.VIDEO).ToList();
        var audios = streams.Where(x => x.MediaType == MediaType.AUDIO).ToList();
        var subs = streams.Where(x => x.MediaType == MediaType.SUBTITLES).ToList();

        if (basicStreams.Count != 0)
            selectedStreams.Add(basicStreams.First());
        
        var langs = audios.DistinctBy(a => a.Language).Select(a => a.Language);
        foreach (var lang in langs)
        {
            selectedStreams.Add(audios.Where(a => a.Language == lang).OrderByDescending(a => a.Bandwidth).ThenByDescending(GetOrder).First());
        }
        selectedStreams.AddRange(subs);

        if (selectedStreams.Count == 0)
            throw new Exception("No streams to download");

        // 加载播放列表
        if (selectedStreams.Any(s => s.Playlist == null) || extractor.ExtractorType == ExtractorType.MPEG_DASH || extractor.ExtractorType == ExtractorType.MSS)
            await extractor.FetchPlayListAsync(selectedStreams);

        // 创建下载管理器并开始下载
        var downloadConfig = new DownloaderConfig()
        {
            MyOptions = option,
            DirPrefix = tmpDir,
            Headers = parserConfig.Headers, // 使用命令行解析得到的Headers
        };

        var result = false;

        if (extractor.ExtractorType == ExtractorType.HTTP_LIVE)
        {
            var sldm = new HTTPLiveRecordManager(downloadConfig, selectedStreams, extractor);
            result = await sldm.StartRecordAsync();
        }
        else if (!livingFlag)
        {
            // 开始下载
            var sdm = new SimpleDownloadManager(downloadConfig, selectedStreams, extractor);
            result = await sdm.StartDownloadAsync();
        }
        else
        {
            var sldm = new SimpleLiveRecordManager2(downloadConfig, selectedStreams, extractor);
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

    /// <summary>
    /// 为批量下载生成唯一的文件名
    /// </summary>
    /// <param name="url">源URL</param>
    /// <param name="batchIndex">批量下载索引（从1开始）</param>
    /// <param name="totalBatches">总批量数</param>
    /// <returns>唯一文件名</returns>
    static string GetUniqueFileNameFromUrl(string url, int batchIndex, int totalBatches)
    {
        try
        {
            // 从URL提取基础名称
            var uri = new Uri(url.Split('?').First());
            var baseName = Path.GetFileNameWithoutExtension(uri.LocalPath);
            
            // 清理文件名，移除特殊字符
            baseName = GetValidFileName(baseName);
            
            // 如果基础名称为空或只有扩展名，使用URL主机名
            if (string.IsNullOrWhiteSpace(baseName) || baseName == ".m3u8" || baseName == ".mpd")
            {
                baseName = uri.Host.Replace(".", "_");
            }
            
            // 生成时间戳（精确到秒，避免同一秒内的文件名冲突）
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            
            // 生成序号部分
            var indexInfo = totalBatches > 1 ? $"_batch{batchIndex:00}_of_{totalBatches:00}" : "_batch";
            
            // 生成最终文件名
            var finalName = $"{baseName}{indexInfo}_{timestamp}";
            
            return finalName;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[BatchDownload] Failed to generate unique filename for URL: {ex.Message}");
            // 如果解析失败，使用简单的备用名称
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            return $"batch_item_{batchIndex:00}_{timestamp}";
        }
    }
    
    /// <summary>
    /// 清理文件名中的无效字符
    /// </summary>
    static string GetValidFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";
            
        var invalidChars = Path.GetInvalidFileNameChars();
        var validName = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
        
        // 移除连续的下划线和空格
        validName = System.Text.RegularExpressions.Regex.Replace(validName, @"_{2,}", "_");
        validName = System.Text.RegularExpressions.Regex.Replace(validName, @"\s+", "_");
        
        // 限制长度
        if (validName.Length > 100)
            validName = validName.Substring(0, 100);
            
        return validName.Trim('_');
    }

    static async Task CheckUpdateAsync()
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
                Logger.InfoMarkUp($"[cyan]{ResString.newVersionFound}[/] [red]{latestVer}[/]");
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
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
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
