using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using Spectre.Console;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using System.Text;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Processor;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.CommandLine;
using System.Net;
using N_m3u8DL_RE.Enum;


namespace N_m3u8DL_RE.Runtime;

internal static class DownloadTaskRunner
{
    internal static Task<ExecutionResult> RunAsync(MyOption options, TaskExecutionContext context) =>
        TaskExecution.RunAsync(context, () => RunCoreAsync(options, context), options.LogLevel == LogLevel.DEBUG);

    private static void ConfigureNetwork(MyOption option)
    {
        ServicePointManager.DefaultConnectionLimit = 1024;
        HTTPUtil.AppHttpClient.Timeout = TimeSpan.FromSeconds(option.HttpRequestTimeout);
        HTTPUtil.AppHttpClient.DefaultRequestVersion = option.HttpVersion;
        if (!option.UseSystemProxy)
            HTTPUtil.HttpHandler.UseProxy = false;
        if (option.CustomProxy != null)
        {
            HTTPUtil.HttpHandler.Proxy = option.CustomProxy;
            HTTPUtil.HttpHandler.UseProxy = true;
        }
        HTTPUtil.ConfigureNetworkInterface(option.NetworkInterface);
        HTTPUtil.ConfigureCookies(option.Cookies);
    }

    static int GetOrder(StreamSpec streamSpec)
    {
        if (streamSpec.Channels == null)
            return 0;

        var str = streamSpec.Channels.Split('/')[0];
        return int.TryParse(str, out var order) ? order : 0;
    }

    internal static async Task RunCoreAsync(MyOption option, TaskExecutionContext context)
    {
        ConfigureNetwork(option);
        if (!context.IsWorker && !option.DisableUpdateCheck)
            _ = Program.CheckUpdateAsync();
        context.State("analyzing");
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

        if (option.LiveCatchup != null && (option.LivePerformAsVod || option.CustomRange != null))
        {
            throw new InvalidOperationException(ResString.liveCatchupConflict);
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
            CustomHLSScope = option.CustomHLSScope,
        };

        if (option.AllowHlsMultiExtMap)
        {
            parserConfig.CustomParserArgs.Add("AllowHlsMultiExtMap", "true");
        }

        // 内容处理器示例
        parserConfig.ContentProcessors.Insert(0, new DemoProcessor());
        // 密钥处理器示例
        parserConfig.KeyProcessors.Insert(0, new DemoProcessor2());

        // 等待任务开始时间
        if (option.TaskStartAt != null && option.TaskStartAt > DateTime.Now)
        {
            Logger.InfoMarkUp(ResString.taskStartAt + option.TaskStartAt);
            while (option.TaskStartAt > DateTime.Now)
            {
                await Task.Delay(1000, context.Control.CancellationToken);
            }
        }

        try
        {
            option.LiveCatchupStart = option.LiveCatchup?.Resolve(DateTimeOffset.Now);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException(ResString.liveCatchupInvalid);
        }
        var url = option.Input;

        // 流提取器配置
        using var extractor = new StreamExtractor(parserConfig);
        // 从链接加载内容
        await RetryUtil.WebRequestRetryAsync(async () =>
        {
            await extractor.LoadSourceFromUrlAsync(url);
            return true;
        });
        if (option.LiveCatchup != null && extractor.ExtractorType is not (ExtractorType.MPEG_DASH or ExtractorType.HLS))
        {
            throw new InvalidOperationException(ResString.liveCatchupRequireLive);
        }
        if (extractor.ExtractorType == ExtractorType.BINARY)
        {
            var binarySource = extractor.DirectSource ??
                               throw new InvalidDataException("Binary input requires an HTTP response.");
            context.State("downloading");
            await BinaryDownloadRunner.RunAsync(option, binarySource, headers);
            return;
        }
        if (extractor.ExtractorType is ExtractorType.HTTP_LIVE or ExtractorType.IPTV_M3U)
        {
            var liveStreams = await extractor.ExtractStreamsAsync();
            if (extractor.ExtractorType == ExtractorType.IPTV_M3U)
            {
                Logger.Info(ResString.iptvChannels, liveStreams.Count);
                liveStreams = FilterUtil.DoFilterDrop(liveStreams, option.DropVideoFilter);
                if (option.SubOnly || option.AudioFilter != null || option.SubtitleFilter != null)
                    throw new ArgumentException(ResString.iptvVideoFilterRequired);
                if (option.AutoSelect)
                    liveStreams = liveStreams.Take(1).ToList();
                else if (option.VideoFilter != null)
                    liveStreams = FilterUtil.DoFilterKeep(liveStreams, option.VideoFilter);
                else if (liveStreams.Count > 0)
                    liveStreams = await context.Interaction.SelectStreamsAsync(liveStreams, context.Control.CancellationToken);
                if (liveStreams.Count == 0)
                    throw new ArgumentException(ResString.noStreamsToDownload);
                Logger.Info(ResString.selectedStream);
                foreach (var stream in liveStreams)
                    Logger.InfoMarkUp(stream.ToString());
            }
            else
            {
                option.SaveName ??= OtherUtil.GetFileNameFromInput(option.Input);
            }
            context.SetTracks(liveStreams);
            if (option.SkipDownload)
            {
                return;
            }
            var liveConfig = new DownloaderConfig
            {
                MyOptions = option,
                DirPrefix = string.Empty,
                Headers = parserConfig.Headers
            };
            context.IsLive = true;
            context.State("downloading");
            var liveRecorder = new HTTPLiveRecordManager(liveConfig, liveStreams, extractor);
            if (await liveRecorder.StartRecordAsync())
            {
                Logger.InfoMarkUp("[white on green]Done[/]");
            }
            else
            {
                Logger.ErrorMarkUp("[white on red]Failed[/]");
                throw new InvalidOperationException("Download or recording failed; see diagnostic log.");
            }
            return;
        }

        CheckMediaTools(option);
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
        var tmpDir = Path.Combine(option.TmpDir ?? Environment.CurrentDirectory, OtherUtil.GetSafeFileName(option.SaveName ?? DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss")));
        // 记录文件
        if (option.WriteMetaJson)
        {
            extractor.RawFiles["meta.json"] = GlobalUtil.ConvertToJson(lists);
        }
        // 写出文件
        var createdMetadataFiles = await WriteRawFilesAsync(option, extractor, tmpDir);

        // 在 drop 筛选前记录多 Period 身份；只留下一个 Period 时仍需按 PTO 裁剪。
        var multiPeriodVod = extractor.ExtractorType == ExtractorType.MPEG_DASH &&
            lists.Where(s => s.Playlist?.IsLive == false).SelectMany(s => s.Playlist!.MediaParts)
                .Select(p => p.PeriodIndex).Distinct().Count() > 1;
        Logger.Info(ResString.streamsInfo, lists.Count, basicStreams.Count, audios.Count, subs.Count);
        foreach (var item in multiPeriodVod ? VodPartSelector.QualityChoices(lists) : lists)
        {
            Logger.InfoMarkUp(item.ToString());
        }

        // 保存源分片列表，配置预览可以排除广告，但范围下载不能使用过滤后的编号/时间。
        var dashSourceStreams = extractor.ExtractorType == ExtractorType.MPEG_DASH && lists.All(s => s.Playlist?.IsLive == false)
            ? VodPartSelector.SnapshotStreams(lists) : null;
        if (extractor.ExtractorType == ExtractorType.MPEG_DASH && lists.All(s => s.Playlist?.IsLive == false))
        {
            if (option.VodListParts)
            {
                await VodPartSelector.ListAsync(lists, option);
                return;
            }
            // 必须先删除广告 Period，再匹配兼容编码；否则广告的 HEVC/AAC 配置会阻止正文规划。
            VodPartSelector.Apply(lists, option.VodDropParts);
            if (option.AdKeywords is { Length: > 0 })
                FilterUtil.CleanAd(lists, option.AdKeywords);
            lists.RemoveAll(s => s.SegmentsCount == 0);
            basicStreams = lists.Where(s => s.MediaType is null or MediaType.VIDEO).ToList();
            audios = lists.Where(s => s.MediaType == MediaType.AUDIO).ToList();
            subs = lists.Where(s => s.MediaType == MediaType.SUBTITLES).ToList();
        }
        var selectedStreams = new List<StreamSpec>();
        if (option.DropVideoFilter != null || option.DropAudioFilter != null || option.DropSubtitleFilter != null)
        {
            basicStreams = FilterUtil.DoFilterDrop(basicStreams, option.DropVideoFilter);
            audios = FilterUtil.DoFilterDrop(audios, option.DropAudioFilter);
            subs = FilterUtil.DoFilterDrop(subs, option.DropSubtitleFilter);
            lists = basicStreams.Concat(audios).Concat(subs).ToList();
        }

        if (extractor.ExtractorType == ExtractorType.MPEG_DASH && lists.All(s => s.Playlist?.IsLive == false) &&
            VodPartSelector.ShouldPrompt(option))
        {
            // 先选保留的配置，再选画质/匹配 Period，允许用户直接排除不兼容广告。
            await VodPartSelector.SelectAsync(lists, option);
            lists.RemoveAll(s => s.SegmentsCount == 0);
            basicStreams = lists.Where(s => s.MediaType is null or MediaType.VIDEO).ToList();
            audios = lists.Where(s => s.MediaType == MediaType.AUDIO).ToList();
            subs = lists.Where(s => s.MediaType == MediaType.SUBTITLES).ToList();
        }

        // 筛选后没有候选流时直接结束，避免交互选择访问空列表。
        if (lists.Count == 0)
        {
            throw new InvalidOperationException(ResString.noStreamsToDownload);
        }

        if (option.DropVideoFilter != null)
            Logger.Extra($"DropVideoFilter => {option.DropVideoFilter}");
        if (option.DropAudioFilter != null)
            Logger.Extra($"DropAudioFilter => {option.DropAudioFilter}");
        if (option.DropSubtitleFilter != null)
            Logger.Extra($"DropSubtitleFilter => {option.DropSubtitleFilter}");
        if (option.VideoFilter != null)
            Logger.Extra($"VideoFilter => {option.VideoFilter}");
        if (option.AudioFilter != null)
            Logger.Extra($"AudioFilter => {option.AudioFilter}");
        if (option.SubtitleFilter != null)
            Logger.Extra($"SubtitleFilter => {option.SubtitleFilter}");

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
            // 点播各 Period 中重复的画质只展示一次，种子优先采用时长最长的正文轨道。
            selectedStreams = await context.Interaction.SelectStreamsAsync(multiPeriodVod ? VodPartSelector.QualityChoices(lists, dashSourceStreams) : lists, context.Control.CancellationToken);
        }

        if (selectedStreams.Count == 0)
        {
            throw new InvalidOperationException(ResString.noStreamsToDownload);
        }

        if (extractor.ExtractorType == ExtractorType.MPEG_DASH && selectedStreams.All(s => s.Playlist?.IsLive == false))
            selectedStreams = VodStreamPlanner.Build(lists, selectedStreams,
                option.AutoSelect ? null : option.VideoFilter,
                option.AutoSelect ? null : option.AudioFilter,
                option.AutoSelect ? null : option.SubtitleFilter, dashSourceStreams);

        // HLS: 选中流中若有没加载出playlist的，加载playlist
        // DASH/MSS: 加载playlist (调用url预处理器)
        if (selectedStreams.Any(s => s.Playlist == null) || extractor.ExtractorType == ExtractorType.MPEG_DASH || extractor.ExtractorType == ExtractorType.MSS)
            await extractor.FetchPlayListAsync(selectedStreams);

        // 直播检测
        var livingFlag = selectedStreams.Any(s => s.Playlist?.IsLive == true) && !option.LivePerformAsVod;
        if (option.LiveCatchupStart is { } catchupStart)
        {
            LiveCatchupWindow window;
            try
            {
                window = new LiveCatchupWindow(catchupStart, option.LiveRecordLimit);
                window.Validate(selectedStreams, DateTimeOffset.Now);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(ex.Message);
            }
            Logger.Info(ResString.liveCatchupRange, catchupStart.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz"),
                window.End?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? ResString.liveCatchupUntilStopped);
        }
        if (option.VodSelectParts == true && selectedStreams.Any(s => s.Playlist?.IsLive == true))
            throw new NotSupportedException(ResString.vodPartsRequireVod);
        if (livingFlag)
        {
            Logger.WarnMarkUp($"[white on darkorange3_1]{ResString.liveFound}[/]");
            if (option.VodDropParts != null || option.VodSelectParts == true)
                throw new NotSupportedException(ResString.vodPartsRequireVod);
        }
        if (option.VodListParts)
        {
            await VodPartSelector.ListAsync(selectedStreams, option, inspectInit: extractor.ExtractorType == ExtractorType.HLS);
            return;
        }
        if (!livingFlag && extractor.ExtractorType == ExtractorType.HLS)
            VodStreamPlanner.CaptureHlsTimeline(selectedStreams);
        if (extractor.ExtractorType != ExtractorType.MPEG_DASH)
            VodPartSelector.Apply(selectedStreams, option.VodDropParts);

        // 无法识别的加密方式，自动开启二进制合并
        if (selectedStreams.Any(s => s.Playlist!.MediaParts.Any(p => p.MediaSegments.Any(m => m.EncryptInfo.Method == EncryptMethod.UNKNOWN))))
        {
            Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge3}[/]");
            option.BinaryMerge = true;
        }

        // 按完整段时长选择，不能先截取几秒正文再把它归入短段；预览会忽略 URL 广告。
        if (extractor.ExtractorType != ExtractorType.MPEG_DASH && !livingFlag &&
            selectedStreams.All(s => s.Playlist?.IsLive == false) && VodPartSelector.ShouldPrompt(option))
            await VodPartSelector.SelectAsync(selectedStreams, option, inspectInit: extractor.ExtractorType == ExtractorType.HLS);

        // 保持原有顺序：先应用范围，再按 URL 去广告，时间范围仍以过滤广告前的轨道为准。
        if (!livingFlag)
            FilterUtil.ApplyCustomRange(selectedStreams, option.CustomRange);
        FilterUtil.CleanAd(selectedStreams, option.AdKeywords);
        // 直播可能暂时没有媒体，保留轨道等待刷新。
        if (!livingFlag)
            selectedStreams.RemoveAll(stream => stream.SegmentsCount == 0);
        if (selectedStreams.Count == 0)
        {
            throw new InvalidOperationException(ResString.noStreamsToDownload);
        }

        if (!livingFlag && extractor.ExtractorType == ExtractorType.MPEG_DASH &&
            (multiPeriodVod || selectedStreams.Any(s => s.Playlist!.MediaParts.Count > 1)))
            VodStreamPlanner.AlignPeriods(selectedStreams);
        if (!livingFlag && extractor.ExtractorType == ExtractorType.HLS &&
            selectedStreams.Any(s => s.Playlist!.MediaParts.Count > 1 || s.MediaType == MediaType.SUBTITLES))
            VodStreamPlanner.AlignHlsDiscontinuities(selectedStreams);

        if (!livingFlag)
            selectedStreams.RemoveAll(s => s.SegmentsCount == 0);
        if (selectedStreams.Count == 0)
        {
            throw new InvalidOperationException(ResString.noStreamsToDownload);
        }

        // 记录文件
        if (option.WriteMetaJson)
        {
            extractor.RawFiles["meta_selected.json"] = GlobalUtil.ConvertToJson(selectedStreams);
        }

        context.SetTracks(selectedStreams);
        Logger.Info(ResString.selectedStream);
        foreach (var item in selectedStreams)
        {
            Logger.InfoMarkUp(item.ToString());
        }

        // 写出文件
        createdMetadataFiles.AddRange(await WriteRawFilesAsync(option, extractor, tmpDir));

        if (option.SkipDownload)
        {
            return;
        }

#if DEBUG
        if (!context.IsWorker)
        {
            Console.WriteLine("Press any key to continue...");
            Console.ReadKey();
        }
#endif

        Logger.InfoMarkUp(ResString.saveName + $"[deepskyblue3]{option.SaveName.EscapeMarkup()}[/]");

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
            CreatedMetadataFiles = createdMetadataFiles,
            Headers = parserConfig.Headers, // 使用命令行解析得到的Headers
        };

        context.ThrowIfCancelled();
        context.IsLive = livingFlag;
        context.State("downloading");
        var result = false;

        if (!livingFlag)
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
            throw new InvalidOperationException("Download or recording failed; see diagnostic log.");
        }
    }

    internal static void CheckMediaTools(MyOption option)
    {
        option.FFmpegBinaryPath ??= GlobalUtil.FindExecutable("ffmpeg");
        if (string.IsNullOrEmpty(option.FFmpegBinaryPath) || !File.Exists(option.FFmpegBinaryPath))
        {
            throw new FileNotFoundException(ResString.ffmpegNotFound);
        }
        Logger.Extra($"ffmpeg => {option.FFmpegBinaryPath}");

        if (option is { MuxOptions.UseMkvmerge: true, MuxAfterDone: true })
        {
            option.MkvmergeBinaryPath ??= GlobalUtil.FindExecutable("mkvmerge");
            if (string.IsNullOrEmpty(option.MkvmergeBinaryPath) || !File.Exists(option.MkvmergeBinaryPath))
            {
                throw new FileNotFoundException(ResString.mkvmergeNotFound);
            }
            Logger.Extra($"mkvmerge => {option.MkvmergeBinaryPath}");
        }

        if (option.Keys is { Length: > 0 } || option.KeyTextFile != null)
        {
            if (!string.IsNullOrEmpty(option.DecryptionBinaryPath))
            {
                if (!File.Exists(option.DecryptionBinaryPath))
                    throw new FileNotFoundException(option.DecryptionBinaryPath);
                // 解密时可能切换工作目录，手动指定的相对路径须先转为绝对路径。
                option.DecryptionBinaryPath = Path.GetFullPath(option.DecryptionBinaryPath);
            }
            switch (option.DecryptionEngine)
            {
                case DecryptEngine.SHAKA_PACKAGER:
                {
                    var file = option.DecryptionBinaryPath;
                    if (string.IsNullOrEmpty(file))
                        file = BinaryToolUtil.FindShakaPackager();
                    if (file == null)
                    {
                        throw new FileNotFoundException(ResString.shakaPackagerNotFound);
                    }
                    option.DecryptionBinaryPath = file;
                    Logger.Extra($"shaka-packager => {option.DecryptionBinaryPath}");
                    break;
                }
                case DecryptEngine.MP4DECRYPT:
                {
                    var file = option.DecryptionBinaryPath;
                    if (string.IsNullOrEmpty(file))
                        file = GlobalUtil.FindExecutable("mp4decrypt");
                    if (file == null)
                    {
                        throw new FileNotFoundException(ResString.mp4decryptNotFound);
                    }
                    option.DecryptionBinaryPath = file;
                    Logger.Extra($"mp4decrypt => {option.DecryptionBinaryPath}");
                    break;
                }
                case DecryptEngine.FFMPEG:
                default:
                    if (string.IsNullOrEmpty(option.DecryptionBinaryPath))
                        option.DecryptionBinaryPath = option.FFmpegBinaryPath;
                    break;
            }
        }
    }

    internal static async Task<List<string>> WriteRawFilesAsync(MyOption option, StreamExtractor extractor, string tmpDir)
    {
        List<string> createdFiles = [];
        // 写出json文件，并记录本次新建的文件，保留已有清单和元数据。
        if (option.WriteMetaJson)
        {
            if (!Directory.Exists(tmpDir))
                Directory.CreateDirectory(tmpDir);
            Logger.Warn(ResString.writeJson);
            foreach (var item in extractor.RawFiles)
            {
                var file = Path.Combine(tmpDir, item.Key);
                if (!Path.Exists(file))
                {
                    await using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    await using var writer = new StreamWriter(stream, Encoding.UTF8);
                    await writer.WriteAsync(item.Value);
                    createdFiles.Add(file);
                }
            }
        }
        return createdFiles;
    }

}
