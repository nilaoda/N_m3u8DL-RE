using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.CommandLine;

internal static partial class CommandInvoker
{
    private static readonly List<(Symbol Symbol, Func<string> Text)> Descriptions = [];

    private static readonly Assembly AppAssembly = typeof(CommandInvoker).Assembly;
    private static readonly string AppVersion = AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+', 2)[0] ?? AppAssembly.GetName().Version?.ToString(3) ?? "unknown";
    public static readonly string VERSION_INFO = $"N_m3u8DL-RE {AppVersion} 20260628";

    [GeneratedRegex("((best|worst)\\d*|all)")]
    private static partial Regex ForStrRegex();
    [GeneratedRegex(@"(\d*)-(\d*)")]
    private static partial Regex RangeRegex();
    [GeneratedRegex(@"([\d\\.]+)(M|K)")]
    private static partial Regex SpeedStrRegex();
    [GeneratedRegex("^[0-9a-fA-f]{32}:[0-9a-fA-f]{32}$")]
    private static partial Regex PairKeyRegex();
    [GeneratedRegex("^[0-9]{1,}:[0-9a-fA-f]{32}$")]
    private static partial Regex IdHexKeyRegex();
    [GeneratedRegex("^[0-9a-fA-f]{32}$")]
    private static partial Regex SingleHexKeyRegex();

    private static readonly Argument<string> Input = CreateInputArgument();
    internal static readonly Option<string?> Config = new Option<string?>("--config") { HelpName = "FILE", Arity = ArgumentArity.ExactlyOne, Recursive = true }.WithDescription(() => ResString.cmd_config);
    internal static readonly Option<bool> NoConfig = new Option<bool>("--no-config") { Recursive = true }.WithDescription(() => ResString.cmd_noConfig);
    private static readonly Option<string?> TmpDir = new Option<string?>("--tmp-dir").WithDescription(() => ResString.cmd_tmpDir);
    private static readonly Option<string?> SaveDir = new Option<string?>("--save-dir").WithDescription(() => ResString.cmd_saveDir);
    private static readonly Option<string?> SaveName = new Option<string?>("--save-name") { CustomParser = ParseSaveName }.WithDescription(() => ResString.cmd_saveName);
    private static readonly Option<string?> SavePattern = new Option<string?>("--save-pattern").WithDescription(() => ResString.cmd_savePattern);
    private static readonly Option<string?> LogFilePath = new Option<string?>("--log-file-path") { CustomParser = ParseFilePath }.WithDescription(() => ResString.cmd_logFilePath);
    internal static readonly Option<string?> UILanguage = new Option<string?>("--ui-language") { Recursive = true }.AcceptOnlyFromAmong("en-US", "zh-CN", "zh-TW").WithDescription(() => ResString.cmd_uiLanguage);
    private static readonly Option<string?> UrlProcessorArgs = new Option<string?>("--urlprocessor-args").WithDescription(() => ResString.cmd_urlProcessorArgs);
    private static readonly Option<string> KeyTextFile = new Option<string>("--key-text-file").WithDescription(() => ResString.cmd_keyText);
    private static readonly Option<Dictionary<string, string>> Headers = new Option<Dictionary<string, string>>("-H", "--header") { HelpName = "header", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, CustomParser = ParseHeaders }.WithDescription(() => ResString.cmd_header);
    private static readonly Option<string?> Cookies = new Option<string?>("--cookies") { HelpName = "FILE", Arity = ArgumentArity.ExactlyOne }.WithDescription(() => ResString.cmd_cookies);
    private static readonly Option<LogLevel> LogLevel = new Option<LogLevel>("--log-level") { DefaultValueFactory = _ => Common.Log.LogLevel.INFO }.WithDescription(() => ResString.cmd_logLevel);
    private static readonly Option<SubtitleFormat> SubtitleFormat = new Option<SubtitleFormat>("--sub-format") { DefaultValueFactory = _ => Enum.SubtitleFormat.SRT }.WithDescription(() => ResString.cmd_subFormat);
    private static readonly Option<bool> DisableUpdateCheck = new Option<bool>("--disable-update-check").WithDefault(false).WithDescription(() => ResString.cmd_disableUpdateCheck);
    private static readonly Option<bool> AutoSelect = new Option<bool>("--auto-select").WithDefault(false).WithDescription(() => ResString.cmd_autoSelect);
    private static readonly Option<bool> SubOnly = new Option<bool>("--sub-only").WithDefault(false).WithDescription(() => ResString.cmd_subOnly);
    private static readonly Option<int> ThreadCount = new Option<int>("--thread-count") { HelpName = "number", DefaultValueFactory = _ => Environment.ProcessorCount }.WithDescription(() => ResString.cmd_threadCount);
    private static readonly Option<int> DownloadRetryCount = new Option<int>("--download-retry-count") { HelpName = "number", DefaultValueFactory = _ => 7 }.WithDescription(() => ResString.cmd_downloadRetryCount);
    private static readonly Option<double> HttpRequestTimeout = new Option<double>("--http-request-timeout") { HelpName = "seconds", DefaultValueFactory = _ => 100 }.WithDescription(() => ResString.cmd_httpRequestTimeout);
    private static readonly Option<bool> SkipMerge = new Option<bool>("--skip-merge").WithDefault(false).WithDescription(() => ResString.cmd_skipMerge);
    private static readonly Option<bool> SkipDownload = new Option<bool>("--skip-download").WithDefault(false).WithDescription(() => ResString.cmd_skipDownload);
    private static readonly Option<bool> NoDateInfo = new Option<bool>("--no-date-info").WithDefault(false).WithDescription(() => ResString.cmd_noDateInfo);
    private static readonly Option<bool> BinaryMerge = new Option<bool>("--binary-merge").WithDefault(false).WithDescription(() => ResString.cmd_binaryMerge);
    internal static readonly Option<FFmpegConcatMode> FFmpegConcatMode = new Option<FFmpegConcatMode>("--ffmpeg-concat-mode") { DefaultValueFactory = _ => Enum.FFmpegConcatMode.LOCAL_HTTP }.WithDescription(() => ResString.cmd_ffmpegConcatMode);
    internal static readonly Option<bool> UseFFmpegConcatDemuxer = new Option<bool>("--use-ffmpeg-concat-demuxer").WithDefault(false).WithDescription(() => ResString.cmd_useFFmpegConcatDemuxer);
    private static readonly Option<bool> DelAfterDone = new Option<bool>("--del-after-done").WithDefault(true).WithDescription(() => ResString.cmd_delAfterDone);
    private static readonly Option<bool> AutoSubtitleFix = CreateAutoSubtitleFixOption().WithDescription(() => ResString.cmd_subtitleFix);
    private static readonly Option<bool> CheckSegmentsCount = new Option<bool>("--check-segments-count").WithDefault(true).WithDescription(() => ResString.cmd_checkSegmentsCount);
    private static readonly Option<bool> WriteMetaJson = new Option<bool>("--write-meta-json").WithDefault(true).WithDescription(() => ResString.cmd_writeMetaJson);
    private static readonly Option<bool> AppendUrlParams = new Option<bool>("--append-url-params").WithDefault(false).WithDescription(() => ResString.cmd_appendUrlParams);
    private static readonly Option<bool> MP4RealTimeDecryption = new Option<bool>("--mp4-real-time-decryption").WithDefault(false).WithDescription(() => ResString.cmd_MP4RealTimeDecryption);
    private static readonly Option<bool> UseShakaPackager = new Option<bool>("--use-shaka-packager") { Hidden = true }.WithDefault(false).WithDescription(() => ResString.cmd_useShakaPackager);
    internal static readonly Option<DecryptEngine> DecryptionEngine = new Option<DecryptEngine>("--decryption-engine") { DefaultValueFactory = _ => DecryptEngine.MP4DECRYPT }.WithDescription(() => ResString.cmd_decryptionEngine);
    internal static readonly Option<bool> ForceAnsiConsole = new Option<bool>("--force-ansi-console").WithDescription(() => ResString.cmd_forceAnsiConsole);
    internal static readonly Option<bool> NoAnsiColor = new Option<bool>("--no-ansi-color").WithDescription(() => ResString.cmd_noAnsiColor);
    internal static readonly Option<string?> DecryptionBinaryPath = new Option<string?>("--decryption-binary-path") { HelpName = "PATH" }.WithDescription(() => ResString.cmd_decryptionBinaryPath);
    internal static readonly Option<string?> FFmpegBinaryPath = new Option<string?>("--ffmpeg-binary-path") { HelpName = "PATH" }.WithDescription(() => ResString.cmd_ffmpegBinaryPath);
    private static readonly Option<string?> BaseUrl = new Option<string?>("--base-url").WithDescription(() => ResString.cmd_baseUrl);
    private static readonly Option<bool> ConcurrentDownload = new Option<bool>("-mt", "--concurrent-download").WithDefault(false).WithDescription(() => ResString.cmd_concurrentDownload);
    private static readonly Option<bool> NoLog = new Option<bool>("--no-log").WithDefault(false).WithDescription(() => ResString.cmd_noLog);
    private static readonly Option<bool> AllowHlsMultiExtMap = new Option<bool>("--allow-hls-multi-ext-map").WithDefault(false).WithDescription(() => ResString.cmd_allowHlsMultiExtMap);
    private static readonly Option<string[]?> AdKeywords = new Option<string[]?>("--ad-keyword") { HelpName = "REG" }.WithDescription(() => ResString.cmd_adKeyword);
    private static readonly Option<bool> VodSelectParts = new Option<bool>("--vod-select-parts").WithDescription(() => ResString.cmd_vodSelectParts);
    private static readonly Option<bool> VodListParts = new Option<bool>("--vod-list-parts").WithDefault(false).WithDescription(() => ResString.cmd_vodListParts);
    private static readonly Option<string?> VodDropParts = new Option<string?>("--vod-drop-parts") { HelpName = "IDS" }.WithDescription(() => ResString.cmd_vodDropParts);
    private static readonly Option<long?> MaxSpeed = new Option<long?>("-R", "--max-speed") { HelpName = "SPEED", CustomParser = ParseSpeedLimit }.WithDescription(() => ResString.cmd_maxSpeed);


    private static readonly Option<string?> NetworkInterface = new Option<string?>("--interface") { HelpName = "INTERFACE" }.WithDescription(() => ResString.cmd_networkInterface);

    // 代理选项
    private static readonly Option<bool> UseSystemProxy = new Option<bool>("--use-system-proxy").WithDefault(true).WithDescription(() => ResString.cmd_useSystemProxy);
    private static readonly Option<WebProxy?> CustomProxy = new Option<WebProxy?>("--custom-proxy") { HelpName = "URL", CustomParser = ParseProxy }.WithDescription(() => ResString.cmd_customProxy);

    // 只下载部分分片
    private static readonly Option<CustomRange?> CustomRange = new Option<CustomRange?>("--custom-range") { HelpName = "RANGE", CustomParser = ParseCustomRange }.WithDescription(() => ResString.cmd_customRange);


    // morehelp
    internal static readonly Option<string?> MoreHelp = new Option<string?>("--morehelp") { HelpName = "OPTION" }.WithDescription(() => ResString.cmd_moreHelp);
    private static readonly Option<string?> GenerateCompletion = new Option<string?>("--generate-completion")
    {
        HelpName = "SHELL", Arity = ArgumentArity.ExactlyOne,
        Action = new PowerShellCompletionAction()
    }.AcceptOnlyFromAmong("powershell").WithDescription(() => ResString.cmd_generateCompletion);

    // 自定义KEY等
    private static readonly Option<EncryptMethod?> CustomHLSMethod = new Option<EncryptMethod?>("--custom-hls-method") { HelpName = "METHOD" }.WithDescription(() => ResString.cmd_customHLSMethod);
    // byte[] 是单个密钥或 IV 的解析结果，不能按数组类型推断成可重复的参数列表。
    private static readonly Option<byte[]?> CustomHLSKey = new Option<byte[]?>("--custom-hls-key") { HelpName = "FILE|HEX|BASE64", Arity = ArgumentArity.ExactlyOne, CustomParser = ParseHLSCustomKey }.WithDescription(() => ResString.cmd_customHLSKey);
    private static readonly Option<byte[]?> CustomHLSIv = new Option<byte[]?>(name: "--custom-hls-iv") { HelpName = "FILE|HEX|BASE64", Arity = ArgumentArity.ExactlyOne, CustomParser = ParseHLSCustomKey }.WithDescription(() => ResString.cmd_customHLSIv);
    private static readonly Option<CustomHlsScope> CustomHLSScope = new Option<CustomHlsScope>("--custom-hls-scope") { HelpName = "SCOPE", DefaultValueFactory = _ => CustomHlsScope.ALL }.WithDescription(() => ResString.cmd_customHLSScope);
    private static readonly Option<string[]?> Keys = new Option<string[]?>("--key") { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, CustomParser = ParseCustomKeys }.WithDescription(() => ResString.cmd_keys);

    // 任务开始时间
    private static readonly Option<DateTime?> TaskStartAt = new Option<DateTime?>("--task-start-at") { HelpName = "yyyyMMddHHmmss", CustomParser = ParseStartTime }.WithDescription(() => ResString.cmd_taskStartAt);


    // 直播相关
    private static readonly Option<bool> LivePerformAsVod = new Option<bool>("--live-perform-as-vod").WithDefault(false).WithDescription(() => ResString.cmd_livePerformAsVod);
    private static readonly Option<bool> LiveRealTimeMerge = new Option<bool>("--live-real-time-merge").WithDefault(false).WithDescription(() => ResString.cmd_liveRealTimeMerge);
    private static readonly Option<bool> LiveKeepSegments = new Option<bool>("--live-keep-segments").WithDefault(true).WithDescription(() => ResString.cmd_liveKeepSegments);
    private static readonly Option<bool> LivePipeMux = new Option<bool>("--live-pipe-mux").WithDefault(false).WithDescription(() => ResString.cmd_livePipeMux);
    private static readonly Option<TimeSpan?> LiveRecordLimit = new Option<TimeSpan?>("--live-record-limit") { HelpName = "HH:mm:ss", CustomParser = ParseLiveLimit }.WithDescription(() => ResString.cmd_liveRecordLimit);
    private static readonly Option<int?> LiveWaitTime = new Option<int?>("--live-wait-time") { HelpName = "SEC" }.WithDescription(() => ResString.cmd_liveWaitTime);
    private static readonly Option<int?> LiveIdleTimeout = new Option<int?>("--live-idle-timeout") { HelpName = "SEC", CustomParser = ParseLiveIdleTimeout }.WithDescription(() => ResString.cmd_liveIdleTimeout);
    private static readonly Option<LiveCatchup?> LiveCatchupOption = new Option<LiveCatchup?>("--live-catchup") { HelpName = "TIME", CustomParser = ParseLiveCatchup }.WithDescription(() => ResString.cmd_liveCatchup);
    private static readonly Option<int> LiveTakeCount = new Option<int>("--live-take-count") { HelpName = "NUM", DefaultValueFactory = _ => 16 }.WithDescription(() => ResString.cmd_liveTakeCount);
    private static readonly Option<bool> LiveFixVttByAudio = new Option<bool>("--live-fix-vtt-by-audio").WithDefault(false).WithDescription(() => ResString.cmd_liveFixVttByAudio);


    // 复杂命令行如下
    private static readonly Option<MuxOptions?> MuxAfterDone = new Option<MuxOptions?>("-M", "--mux-after-done") { HelpName = "OPTIONS", CustomParser = ParseMuxAfterDone }.WithDescription(() => ResString.cmd_muxAfterDone);
    private static readonly Option<List<OutputFile>> MuxImports = CreateMuxImportsOption().WithDescription(() => ResString.cmd_muxImport);
    private static readonly Option<StreamFilter?> VideoFilter = new Option<StreamFilter?>("-sv", "--select-video") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_selectVideo);
    private static readonly Option<StreamFilter?> AudioFilter = new Option<StreamFilter?>("-sa", "--select-audio") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_selectAudio);
    private static readonly Option<StreamFilter?> SubtitleFilter = new Option<StreamFilter?>("-ss", "--select-subtitle") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_selectSubtitle);

    private static readonly Option<StreamFilter?> DropVideoFilter = new Option<StreamFilter?>("-dv", "--drop-video") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_dropVideo);
    private static readonly Option<StreamFilter?> DropAudioFilter = new Option<StreamFilter?>("-da", "--drop-audio") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_dropAudio);
    private static readonly Option<StreamFilter?> DropSubtitleFilter = new Option<StreamFilter?>("-ds", "--drop-subtitle") { HelpName = "OPTIONS", CustomParser = ParseStreamFilter }.WithDescription(() => ResString.cmd_dropSubtitle);

    private static LiveCatchup? ParseLiveCatchup(ArgumentResult result)
    {
        try
        {
            return LiveCatchup.Parse(result.Tokens[0].Value);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException)
        {
            result.AddError(ResString.liveCatchupInvalid);
            return null;
        }
    }

    /// <summary>
    /// 解析下载速度限制
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static long? ParseSpeedLimit(ArgumentResult result)
    {
        var input = result.Tokens[0].Value.ToUpper();
        try
        {
            var reg = SpeedStrRegex();
            if (!reg.IsMatch(input)) throw new ArgumentException($"Invalid Speed Limit: {input}");

            var number = double.Parse(reg.Match(input).Groups[1].Value);
            if (reg.Match(input).Groups[2].Value == "M")
                return (long)(number * 1024 * 1024);
            return (long)(number * 1024);
        }
        catch (Exception)
        {
            result.AddError("error in parse SpeedLimit: " + input);
            return null;
        }
    }

    /// <summary>
    /// 解析用户定义的下载范围
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private static CustomRange? ParseCustomRange(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        // 支持的种类 0-100; 01:00:00-02:30:00; -300; 300-; 05:00-; -03:00;
        try
        {
            if (string.IsNullOrEmpty(input))
                return null;

            var arr = input.Split('-');
            if (arr.Length != 2)
                throw new ArgumentException("Bad format!");

            if (input.Contains(':'))
            {
                return new CustomRange()
                {
                    InputStr = input,
                    StartSec = arr[0] == "" ? 0 : OtherUtil.ParseDur(arr[0]).TotalSeconds,
                    EndSec = arr[1] == "" ? double.MaxValue : OtherUtil.ParseDur(arr[1]).TotalSeconds,
                };
            }

            if (RangeRegex().IsMatch(input))
            {
                var left = RangeRegex().Match(input).Groups[1].Value;
                var right = RangeRegex().Match(input).Groups[2].Value;
                return new CustomRange()
                {
                    InputStr = input,
                    StartSegIndex = left == "" ? 0 : long.Parse(left),
                    EndSegIndex = right == "" ? long.MaxValue : long.Parse(right),
                };
            }

            throw new ArgumentException("Bad format!");
        }
        catch (Exception ex)
        {
            result.AddError("error in parse CustomRange: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 解析用户代理
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    private static WebProxy? ParseProxy(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        try
        {
            if (string.IsNullOrEmpty(input))
                return null;

            var uri = new Uri(input);
            var proxy = new WebProxy(uri, true);
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var infos = uri.UserInfo.Split(':');
                proxy.Credentials = new NetworkCredential(infos.First(), infos.Last());
            }
            return proxy;
        }
        catch (Exception ex)
        {
            result.AddError("error in parse proxy: " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 解析自定义KEY（用于mp4decrypt等第三方程序）
    /// 支持格式：<br/>
    /// - KEY（hex）<br/>
    /// - KID:KEY（hex）<br/>
    /// - Base64KEY<br/>
    /// - Base64KID:Base64KEY
    /// </summary>
    private static string[]? ParseCustomKeys(ArgumentResult result)
    {
        const int KeyBytes = 16;
        const int KeyHexLen = KeyBytes * 2;
        
        string ParsePart(string part, string label)
        {
            if (SingleHexKeyRegex().IsMatch(part))
                return part.ToLowerInvariant();

            if (HexUtil.TryParseBase64(part, out var hex) && hex is { Length: KeyHexLen })
                return hex.ToLowerInvariant();

            throw new ArgumentException($"{label} must be valid 16-byte HEX or Base64. Input string: {part}");
        }

        var keys = new List<string>();
        var inputs = result.Tokens.Select(t => t.Value).ToList();

        try
        {
            foreach (var input in inputs)
            {
                // 已匹配标准格式的，直接添加
                if (PairKeyRegex().IsMatch(input) || IdHexKeyRegex().IsMatch(input) || SingleHexKeyRegex().IsMatch(input))
                {
                    keys.Add(input);
                    continue;
                }

                // 拆分KID:KEY
                var parts = input.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                if (parts.Length is < 1 or > 2)
                    throw new ArgumentException("Input must be KEY or KID:KEY format.");

                if (parts.Length == 1)
                {
                    var key = ParsePart(parts[0], "KEY");
                    keys.Add(key);
                }
                else // KID:KEY
                {
                    var kid = ParsePart(parts[0], "KID");
                    var key = ParsePart(parts[1], "KEY");
                    keys.Add($"{kid}:{key}");
                }
            }

            return [.. keys];
        }
        catch (Exception ex)
        {
            result.AddError($"error in parse custom key: {ex.Message}. All Inputs=[{string.Join(", ", inputs)}]");
            return null;
        }
    }

    /// <summary>
    /// 解析自定义KEY
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static byte[]? ParseHLSCustomKey(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        try
        {
            if (string.IsNullOrEmpty(input))
                return null;
            if (File.Exists(input))
                return File.ReadAllBytes(input);
            if (HexUtil.TryParseHexString(input, out byte[]? bytes))
                return bytes;
            return Convert.FromBase64String(input);
        }
        catch (Exception)
        {
            result.AddError("error in parse hls custom key: " + input);
            return null;
        }
    }

    /// <summary>
    /// 解析录制直播时长限制
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static TimeSpan? ParseLiveLimit(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        try
        {
            return OtherUtil.ParseDur(input);
        }
        catch (Exception)
        {
            result.AddError("error in parse LiveRecordLimit: " + input);
            return null;
        }
    }

    private static int? ParseLiveIdleTimeout(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        if (int.TryParse(input, out var seconds) && seconds > 0)
        {
            return seconds;
        }

        result.AddError("live-idle-timeout must be a positive number of seconds: " + input);
        return null;
    }

    /// <summary>
    /// 解析任务开始时间
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static DateTime? ParseStartTime(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        try
        {
            CultureInfo provider = CultureInfo.InvariantCulture;
            return DateTime.ParseExact(input, "yyyyMMddHHmmss", provider);
        }
        catch (Exception)
        {
            result.AddError("error in parse TaskStartTime: " + input);
            return null;
        }
    }

    private static string? ParseSaveName(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        var newName = OtherUtil.GetValidFileName(input);
        if (string.IsNullOrEmpty(newName))
        {
            result.AddError("Invalid save name!");
            return null;
        }
        return newName;
    }

    private static string? ParseFilePath(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        var path = "";
        try
        {
            path = Path.GetFullPath(input);
        }
        catch (Exception e)
        {
            result.AddError("Invalid log path!");
            return null;
        }
        var dir = Path.GetDirectoryName(path);
        var filename = Path.GetFileName(path);
        var newName = OtherUtil.GetValidFileName(filename);
        if (string.IsNullOrEmpty(newName))
        {
            result.AddError("Invalid log file name!");
            return null;
        }
        return Path.Combine(dir!, newName);
    }

    /// <summary>
    /// 流过滤器
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static StreamFilter? ParseStreamFilter(ArgumentResult result)
    {
        var streamFilter = new StreamFilter();
        var input = result.Tokens[0].Value;
        var p = new ComplexParamParser(input);


        // 目标范围
        var forStr = "";
        if (input == ForStrRegex().Match(input).Value)
        {
            forStr = input;
        }
        else
        {
            forStr = p.GetValue("for") ?? "best";
            if (forStr != ForStrRegex().Match(forStr).Value)
            {
                result.AddError($"for={forStr} not valid");
                return null;
            }
        }
        streamFilter.For = forStr;

        var id = p.GetValue("id");
        if (!string.IsNullOrEmpty(id))
            streamFilter.GroupIdReg = new Regex(id);

        var lang = p.GetValue("lang");
        if (!string.IsNullOrEmpty(lang))
            streamFilter.LanguageReg = new Regex(lang);

        var name = p.GetValue("name");
        if (!string.IsNullOrEmpty(name))
            streamFilter.NameReg = new Regex(name);

        var codecs = p.GetValue("codecs");
        if (!string.IsNullOrEmpty(codecs))
            streamFilter.CodecsReg = new Regex(codecs);

        var res = p.GetValue("res");
        if (!string.IsNullOrEmpty(res))
            streamFilter.ResolutionReg = new Regex(res);

        var frame = p.GetValue("frame");
        if (!string.IsNullOrEmpty(frame))
            streamFilter.FrameRateReg = new Regex(frame);

        var channel = p.GetValue("channel");
        if (!string.IsNullOrEmpty(channel))
            streamFilter.ChannelsReg = new Regex(channel);

        var range = p.GetValue("range");
        if (!string.IsNullOrEmpty(range))
            streamFilter.VideoRangeReg = new Regex(range);

        var url = p.GetValue("url");
        if (!string.IsNullOrEmpty(url))
            streamFilter.UrlReg = new Regex(url);

        var period = p.GetValue("period");
        if (!string.IsNullOrEmpty(period))
            streamFilter.PeriodIdReg = new Regex(period);

        var segsMin = p.GetValue("segsMin");
        if (!string.IsNullOrEmpty(segsMin))
            streamFilter.SegmentsMinCount = long.Parse(segsMin);

        var segsMax = p.GetValue("segsMax");
        if (!string.IsNullOrEmpty(segsMax))
            streamFilter.SegmentsMaxCount = long.Parse(segsMax);

        var plistDurMin = p.GetValue("plistDurMin");
        if (!string.IsNullOrEmpty(plistDurMin))
            streamFilter.PlaylistMinDur = OtherUtil.ParseSeconds(plistDurMin);

        var plistDurMax = p.GetValue("plistDurMax");
        if (!string.IsNullOrEmpty(plistDurMax))
            streamFilter.PlaylistMaxDur = OtherUtil.ParseSeconds(plistDurMax);

        var bwMin = p.GetValue("bwMin");
        if (!string.IsNullOrEmpty(bwMin))
            streamFilter.BandwidthMin = int.Parse(bwMin) * 1000;

        var bwMax = p.GetValue("bwMax");
        if (!string.IsNullOrEmpty(bwMax))
            streamFilter.BandwidthMax = int.Parse(bwMax) * 1000;

        var role = p.GetValue("role");
        if (System.Enum.TryParse(role, true, out RoleType roleType))
            streamFilter.Role = roleType;

        return streamFilter;
    }

    /// <summary>
    /// 分割Header
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static Dictionary<string, string> ParseHeaders(ArgumentResult result)
    {
        var array = result.Tokens.Select(t => t.Value).ToArray();
        return OtherUtil.SplitHeaderArrayToDic(array);
    }

    /// <summary>
    /// 解析混流引入的外部文件
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    internal static List<OutputFile> ParseImports(ArgumentResult result)
    {
        var imports = new List<OutputFile>();

        foreach (var item in result.Tokens)
        {
            var file = ParseMuxInput(item.Value);
            if (string.IsNullOrEmpty(file.FilePath) || !File.Exists(file.FilePath))
            {
                result.AddError("path empty or file not exists!");
                return imports;
            }
            imports.Add(file);
        }

        return imports;
    }

    internal static OutputFile ParseMuxInput(string value)
    {
        // 已有普通路径原样保留，复合参数复用现有 path/lang/name 格式。
        var p = new ComplexParamParser(value);
        var options = !File.Exists(value) && p.GetValue("path") != null;
        return new OutputFile
        {
            Index = 999,
            FilePath = options ? p.GetValue("path") ?? "" : value, // 若未获取到 path，直接整个字符串作为路径
            LangCode = options ? p.GetValue("lang") : null,
            Description = options ? p.GetValue("name") : null
        };
    }

    /// <summary>
    /// 解析混流选项
    /// </summary>
    /// <param name="result"></param>
    /// <returns></returns>
    private static MuxOptions? ParseMuxAfterDone(ArgumentResult result)
    {
        var v = result.Tokens[0].Value;
        var p = new ComplexParamParser(v);
        // 混流格式
        var format = p.GetValue("format") ?? v.Split(':')[0]; // 若未获取到，直接:前的字符串作为format解析
        var parseResult = System.Enum.TryParse(format.ToUpperInvariant(), out MuxFormat muxFormat);
        if (!parseResult)
        {
            result.AddError($"format={format} not valid");
            return null;
        }
        // 混流器
        var muxer = p.GetValue("muxer") ?? "ffmpeg";
        if (muxer != "ffmpeg" && muxer != "mkvmerge")
        {
            result.AddError($"muxer={muxer} not valid");
            return null;
        }
        // 混流器路径
        var bin_path = p.GetValue("bin_path") ?? "auto";
        if (string.IsNullOrEmpty(bin_path))
        {
            result.AddError($"bin_path={bin_path} not valid");
            return null;
        }
        // 是否删除
        var keep = p.GetValue("keep") ?? "false";
        if (keep != "true" && keep != "false")
        {
            result.AddError($"keep={keep} not valid");
            return null;
        }
        // 是否忽略字幕
        var skipSub = p.GetValue("skip_sub") ?? "false";
        if (skipSub != "true" && skipSub != "false")
        {
            result.AddError($"skip_sub={keep} not valid");
            return null;
        }
        // 冲突检测
        if (muxer == "mkvmerge" && format == "mp4")
        {
            result.AddError($"mkvmerge can not do mp4");
            return null;
        }
        return new MuxOptions()
        {
            UseMkvmerge = muxer == "mkvmerge",
            MuxFormat = muxFormat,
            KeepFiles = keep == "true",
            SkipSubtitle = skipSub == "true",
            BinPath = bin_path == "auto" ? null : bin_path
        };
    }

    private static bool HasOption(this ParseResult result, Option option)
        => result.GetResult(option) is { Implicit: false };
    
    private static Option<T> WithDefault<T>(this Option<T> option, T defaultValue)
    {
        if (option is not Option<bool>)
            return option;
        option.DefaultValueFactory = _ => defaultValue;
        var currentDesc = option.Description ?? string.Empty;
        var defaultText = defaultValue?.ToString() ?? "null";
        // 拼接：原描述 + 空格 + [default: ...]
        option.Description = string.IsNullOrWhiteSpace(currentDesc)
            ? $"[default: {defaultText}]"
            : $"{currentDesc.Trim()} [default: {defaultText}]";
        return option;
    }

    private static MyOption GetOptions(ParseResult result)
    {
        var option = new MyOption
        {
            Input = result.GetRequiredValue(Input),
            ForceAnsiConsole = result.GetValue(ForceAnsiConsole),
            NoAnsiColor = result.GetValue(NoAnsiColor),
            LogLevel = result.GetValue(LogLevel),
            AutoSelect = result.GetValue(AutoSelect),
            DisableUpdateCheck = result.GetValue(DisableUpdateCheck),
            SkipMerge = result.GetValue(SkipMerge),
            BinaryMerge = result.GetValue(BinaryMerge),
            UseFFmpegConcatDemuxer = result.GetValue(UseFFmpegConcatDemuxer),
            FFmpegConcatMode = result.GetValue(FFmpegConcatMode),
            DelAfterDone = result.GetValue(DelAfterDone),
            AutoSubtitleFix = result.GetValue(AutoSubtitleFix),
            CheckSegmentsCount = result.GetValue(CheckSegmentsCount),
            SubtitleFormat = result.GetValue(SubtitleFormat),
            SubOnly = result.GetValue(SubOnly),
            TmpDir = result.GetValue(TmpDir),
            SaveDir = result.GetValue(SaveDir),
            SaveName = result.GetValue(SaveName),
            LogFilePath = result.GetValue(LogFilePath),
            ThreadCount = result.GetValue(ThreadCount),
            UILanguage = result.GetValue(UILanguage),
            SkipDownload = result.GetValue(SkipDownload),
            WriteMetaJson = result.GetValue(WriteMetaJson),
            AppendUrlParams = result.GetValue(AppendUrlParams),
            SavePattern = result.GetValue(SavePattern),
            Keys = result.GetValue(Keys),
            UrlProcessorArgs = result.GetValue(UrlProcessorArgs),
            MP4RealTimeDecryption = result.GetValue(MP4RealTimeDecryption),
            UseShakaPackager = result.GetValue(UseShakaPackager),
            DecryptionEngine = result.GetValue(DecryptionEngine),
            DecryptionBinaryPath = result.GetValue(DecryptionBinaryPath),
            FFmpegBinaryPath = result.GetValue(FFmpegBinaryPath),
            KeyTextFile = result.GetValue(KeyTextFile),
            Cookies = result.GetValue(Cookies),
            DownloadRetryCount = result.GetValue(DownloadRetryCount),
            HttpRequestTimeout = result.GetValue(HttpRequestTimeout),
            HttpRequestTimeoutSpecified = result.GetResult(HttpRequestTimeout) is { Implicit: false },
            BaseUrl = result.GetValue(BaseUrl),
            MuxImports = result.GetValue(MuxImports),
            ConcurrentDownload = result.GetValue(ConcurrentDownload),
            VideoFilter = result.GetValue(VideoFilter),
            AudioFilter = result.GetValue(AudioFilter),
            SubtitleFilter = result.GetValue(SubtitleFilter),
            DropVideoFilter = result.GetValue(DropVideoFilter),
            DropAudioFilter = result.GetValue(DropAudioFilter),
            DropSubtitleFilter = result.GetValue(DropSubtitleFilter),
            LiveRealTimeMerge = result.GetValue(LiveRealTimeMerge),
            LiveKeepSegments = result.GetValue(LiveKeepSegments),
            LiveRecordLimit = result.GetValue(LiveRecordLimit),
            TaskStartAt = result.GetValue(TaskStartAt),
            LivePerformAsVod = result.GetValue(LivePerformAsVod),
            LivePipeMux = result.GetValue(LivePipeMux),
            LiveFixVttByAudio = result.GetValue(LiveFixVttByAudio),
            UseSystemProxy = result.GetValue(UseSystemProxy),
            CustomProxy = result.GetValue(CustomProxy),
            NetworkInterface = result.GetValue(NetworkInterface),
            CustomRange = result.GetValue(CustomRange),
            LiveWaitTime = result.GetValue(LiveWaitTime),
            LiveIdleTimeout = result.GetValue(LiveIdleTimeout),
            LiveTakeCount = result.GetValue(LiveTakeCount),
            LiveCatchup = result.GetValue(LiveCatchupOption),
            NoDateInfo = result.GetValue(NoDateInfo),
            NoLog = result.GetValue(NoLog),
            AllowHlsMultiExtMap = result.GetValue(AllowHlsMultiExtMap),
            AdKeywords = result.GetValue(AdKeywords),
            // 未传参数保留自动判断，显式 false 则关闭选段交互。
            VodSelectParts = result.HasOption(VodSelectParts) ? result.GetValue(VodSelectParts) : null,
            VodListParts = result.GetValue(VodListParts),
            VodDropParts = result.GetValue(VodDropParts),
            MaxSpeed = result.GetValue(MaxSpeed),
        };

        if (result.HasOption(CustomHLSMethod)) option.CustomHLSMethod = result.GetValue(CustomHLSMethod);
        if (result.HasOption(CustomHLSKey)) option.CustomHLSKey = result.GetValue(CustomHLSKey);
        if (result.HasOption(CustomHLSIv)) option.CustomHLSIv = result.GetValue(CustomHLSIv);
        option.CustomHLSScope = result.GetValue(CustomHLSScope);

        var parsedHeaders = result.GetValue(Headers);
        if (parsedHeaders != null)
            option.Headers = parsedHeaders;


        // 以用户选择语言为准优先
        if (option.UILanguage != null)
        {
            CultureUtil.ChangeCurrentCultureName(option.UILanguage);
        }

        // 混流设置
        var muxAfterDoneValue = result.GetValue(MuxAfterDone);
        if (muxAfterDoneValue == null) return option;
        
        option.MuxAfterDone = true;
        option.MuxOptions = muxAfterDoneValue;
        if (muxAfterDoneValue.UseMkvmerge) option.MkvmergeBinaryPath = muxAfterDoneValue.BinPath;
        else option.FFmpegBinaryPath ??= muxAfterDoneValue.BinPath;

        return option;
    }


    private static T WithDescription<T>(this T symbol, Func<string> text) where T : Symbol
    {
        // WithDefault 已为布尔选项附加默认值说明，刷新语言时也要保留。
        var suffix = symbol.Description;
        Descriptions.Add((symbol, string.IsNullOrEmpty(suffix) ? text : () => $"{text()} {suffix}"));
        return symbol;
    }

    // 参数名、默认值和解析规则共用定义；下载和工具命令分别提供各自的帮助说明。
    internal static Option<bool> CreateAutoSubtitleFixOption() =>
        new Option<bool>("--auto-subtitle-fix").WithDefault(true);

    internal static Option<List<OutputFile>> CreateMuxImportsOption() =>
        new("--mux-import")
        {
            HelpName = "OPTIONS", Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false, CustomParser = ParseImports
        };

    internal static RootCommand CreateRootCommand()
    {
        // 配置加载前也需要完整的参数边界；选定 UI 语言后再更新帮助文本。
        foreach (var (symbol, text) in Descriptions)
            symbol.Description = text();
        var root = new RootCommand(VERSION_INFO)
        {
            Input, Config, NoConfig, TmpDir, SaveDir, SaveName, SavePattern, LogFilePath, BaseUrl, ThreadCount, DownloadRetryCount, HttpRequestTimeout, ForceAnsiConsole, NoAnsiColor,AutoSelect, SkipMerge, SkipDownload, CheckSegmentsCount,
            BinaryMerge, FFmpegConcatMode, UseFFmpegConcatDemuxer, DelAfterDone, NoDateInfo, NoLog, WriteMetaJson, AppendUrlParams, ConcurrentDownload, Headers, Cookies, SubOnly, SubtitleFormat, AutoSubtitleFix,
            FFmpegBinaryPath,
            LogLevel, UILanguage, UrlProcessorArgs, Keys, KeyTextFile, DecryptionEngine, DecryptionBinaryPath, UseShakaPackager, MP4RealTimeDecryption,
            MaxSpeed,
            MuxAfterDone,
            CustomHLSMethod, CustomHLSKey, CustomHLSIv, CustomHLSScope, UseSystemProxy, CustomProxy, NetworkInterface, CustomRange, TaskStartAt,
            LivePerformAsVod, LiveRealTimeMerge, LiveKeepSegments, LivePipeMux, LiveFixVttByAudio, LiveRecordLimit, LiveWaitTime, LiveIdleTimeout, LiveTakeCount, LiveCatchupOption,
            MuxImports, VideoFilter, AudioFilter, SubtitleFilter, DropVideoFilter, DropAudioFilter, DropSubtitleFilter, AdKeywords, VodSelectParts, VodListParts, VodDropParts, DisableUpdateCheck, AllowHlsMultiExtMap, MoreHelp, GenerateCompletion
        };
        // 根命令仍可直接下载；参数边界解析时不能强制要求子命令。
        root.SetAction(_ => { });
        ToolCommands.AddTo(root);
        root.Options.OfType<HelpOption>().Single().Action = new UtilityHelpAction(Input);
        return root;
    }

    private static Argument<string> CreateInputArgument()
    {
        var input = new Argument<string>("input").WithDescription(() => ResString.cmd_Input);
        input.Validators.Add(ValidateInput);
        return input;
    }

    private static bool IsPathInput(string input) =>
        Path.IsPathRooted(input) || input.IndexOfAny(['/', '\\']) >= 0 || Path.HasExtension(input) || input.StartsWith('.') ||
        Uri.TryCreate(input, UriKind.Absolute, out var uri) && uri.IsFile;

    private static void ValidateInput(ArgumentResult result)
    {
        if (result.Parent is not CommandResult { Command: RootCommand root })
            return;
        if (root.Subcommands.Any(command => result.GetResult(command) != null))
            return;
        var input = result.GetValueOrDefault<string>();
        if (string.IsNullOrEmpty(input) || File.Exists(input))
            return;
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
                return;
            if (uri.IsFile)
            {
                if (!File.Exists(uri.LocalPath))
                    result.AddError($"{ResString.toolsInputMissing}: {input}");
                return;
            }
        }
        if (IsPathInput(input))
        {
            result.AddError($"{ResString.toolsInputMissing}: {input}");
            return;
        }
        result.AddError(string.Format(ResString.inputInvalid, input));
    }

    internal static ParseResult ParseArgs(RootCommand root, string[] args, ParserConfiguration? configuration = null)
    {
        var result = root.Parse(args, configuration);
        if (result.Action is not ParseErrorAction errorAction)
            return result;
        var input = result.GetResult(Input);
        // 输入错误已有具体说明，避免再打印整页下载参数掩盖命令拼写提示。
        if (result.Errors.Any(error => error.SymbolResult == result.RootCommandResult ||
                error.SymbolResult == input && input?.Tokens.Count == 1))
            errorAction.ShowHelp = false;
        // 只有无效裸值才重新按命令解析；同时存在其他参数错误时保留完整错误信息。
        if (input?.Tokens.Count != 1 || result.Errors.Count != 1 || result.Errors[0].SymbolResult != input ||
            IsPathInput(input.Tokens[0].Value))
            return result;
        var commands = new RootCommand();
        commands.SetAction(_ => { });
        commands.Options.Clear();
        commands.Directives.Clear();
        foreach (var command in root.Subcommands)
        {
            var copy = new Command(command.Name) { Hidden = command.Hidden };
            foreach (var alias in command.Aliases)
                copy.Aliases.Add(alias);
            commands.Subcommands.Add(copy);
        }
        // 无位置参数时输入会成为 UnmatchedTokens，由官方 ParseErrorAction 生成拼写建议。
        var unmatched = commands.Parse(["--", input.Tokens[0].Value], configuration);
        if (unmatched.Action is not ParseErrorAction suggestions)
            return result;
        suggestions.ShowHelp = false;
        return unmatched;
    }

    public static async Task<int> InvokeArgs(ConfigFile configFile, Func<MyOption, Task> action)
    {
        var rootCommand = CreateRootCommand();
        string[] args;
        try
        {
            args = configFile.Merge(rootCommand);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"{ResString.configFileLoadFailed}: {ex.Message}");
            Environment.Exit(1);
            return 1;
        }
        var argList = new List<string>(args);
        var index = -1;
        if ((index = argList.IndexOf(MoreHelp.Name)) >= 0 && argList.Count > index + 1)
        {
            var option = argList[index + 1];
            var msg = option switch
            {
                "mux-after-done" => ResString.cmd_muxAfterDone_more,
                "mux-import" => ResString.cmd_muxImport_more,
                "select-video" => ResString.cmd_selectVideo_more,
                "select-audio" => ResString.cmd_selectAudio_more,
                "select-subtitle" => ResString.cmd_selectSubtitle_more,
                "custom-range" => ResString.cmd_custom_range,
                "save-pattern" => ResString.cmd_savePattern_more,
                _ => $"Option=\"{option}\" not found"
            };
            Console.WriteLine($"More Help:\r\n\r\n  --{option}\r\n\r\n" + msg);
            Environment.Exit(0);
        }

        rootCommand.TreatUnmatchedTokensAsErrors = true;
        rootCommand.SetAction(parseResult =>
        {
            var myOption = GetOptions(parseResult);
            return action(myOption);
        });

        var config = new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null
        };

        try
        {
            var parseResult = ParseArgs(rootCommand, args, config);
            var exitCode = await parseResult.InvokeAsync();
            // 下载或录制失败会设置进程退出码，不能被命令行解析成功返回的 0 覆盖。
            Environment.Exit(exitCode != 0 ? exitCode : Environment.ExitCode);
        }
        catch (Exception ex)
        {
            var msg = Logger.LogLevel == Common.Log.LogLevel.DEBUG 
                ? ex.ToString() 
                : ex.Message;
#if DEBUG
            msg = ex.ToString();
#endif
            Logger.Error(msg);
            Thread.Sleep(3000);
            Environment.Exit(1);
        }
        finally
        {
            try { Console.CursorVisible = true; } catch { }
        }

        return 0;
    }
}
