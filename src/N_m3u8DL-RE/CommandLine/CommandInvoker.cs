using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.CommandLine;

internal static partial class CommandInvoker
{
    public const string VERSION_INFO = "N_m3u8DL-RE (Beta version) 20251029";

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

    private static readonly Argument<string> Input = new("input") { Description = ResString.cmd_Input };
    private static readonly Option<string?> TmpDir = new("--tmp-dir") { Description = ResString.cmd_tmpDir };
    private static readonly Option<string?> SaveDir = new("--save-dir") { Description = ResString.cmd_saveDir };
    private static readonly Option<string?> SaveName = new("--save-name") { Description = ResString.cmd_saveName, CustomParser = ParseSaveName};
    private static readonly Option<string?> SavePattern = new("--save-pattern") { Description = ResString.cmd_savePattern };
    private static readonly Option<string?> LogFilePath = new("--log-file-path") { Description = ResString.cmd_logFilePath, CustomParser = ParseFilePath};
    private static readonly Option<string?> UILanguage = new Option<string?>("--ui-language") { Description = ResString.cmd_uiLanguage }.AcceptOnlyFromAmong("en-US", "zh-CN", "zh-TW");
    private static readonly Option<string?> UrlProcessorArgs = new("--urlprocessor-args") { Description = ResString.cmd_urlProcessorArgs };
    private static readonly Option<string> KeyTextFile = new("--key-text-file") { Description = ResString.cmd_keyText };
    private static readonly Option<Dictionary<string, string>> Headers = new("-H", "--header") { HelpName = "header", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, Description = ResString.cmd_header, CustomParser = ParseHeaders };
    private static readonly Option<LogLevel> LogLevel = new("--log-level") { Description = ResString.cmd_logLevel, DefaultValueFactory = _ => Common.Log.LogLevel.INFO };
    private static readonly Option<SubtitleFormat> SubtitleFormat = new("--sub-format") { Description = ResString.cmd_subFormat, DefaultValueFactory = _ => Enum.SubtitleFormat.SRT };
    private static readonly Option<bool> DisableUpdateCheck = new Option<bool>("--disable-update-check") { Description = ResString.cmd_disableUpdateCheck }.WithDefault(false);
    private static readonly Option<bool> AutoSelect = new Option<bool>("--auto-select") { Description = ResString.cmd_autoSelect }.WithDefault(false);
    private static readonly Option<bool> SubOnly = new Option<bool>("--sub-only") { Description = ResString.cmd_subOnly }.WithDefault(false);
    private static readonly Option<int> ThreadCount = new("--thread-count") { HelpName = "number", Description = ResString.cmd_threadCount, DefaultValueFactory = _ => Environment.ProcessorCount };
    private static readonly Option<int> DownloadRetryCount = new("--download-retry-count") { HelpName = "number", Description = ResString.cmd_downloadRetryCount, DefaultValueFactory = _ => 3 };
    private static readonly Option<double> HttpRequestTimeout = new("--http-request-timeout") { HelpName = "seconds", Description = ResString.cmd_httpRequestTimeout, DefaultValueFactory = _ => 100 };
    private static readonly Option<bool> SkipMerge = new Option<bool>("--skip-merge") { Description = ResString.cmd_skipMerge }.WithDefault(false);
    private static readonly Option<bool> SkipDownload = new Option<bool>("--skip-download") { Description = ResString.cmd_skipDownload }.WithDefault(false);
    private static readonly Option<bool> NoDateInfo = new Option<bool>("--no-date-info") { Description = ResString.cmd_noDateInfo }.WithDefault(false);
    private static readonly Option<bool> BinaryMerge = new Option<bool>("--binary-merge") { Description = ResString.cmd_binaryMerge }.WithDefault(false);
    private static readonly Option<bool> UseFFmpegConcatDemuxer = new Option<bool>("--use-ffmpeg-concat-demuxer") { Description = ResString.cmd_useFFmpegConcatDemuxer }.WithDefault(false);
    private static readonly Option<bool> DelAfterDone = new Option<bool>("--del-after-done") { Description = ResString.cmd_delAfterDone }.WithDefault(true);
    private static readonly Option<bool> AutoSubtitleFix = new Option<bool>("--auto-subtitle-fix") { Description = ResString.cmd_subtitleFix }.WithDefault(true);
    private static readonly Option<bool> CheckSegmentsCount = new Option<bool>("--check-segments-count") { Description = ResString.cmd_checkSegmentsCount }.WithDefault(true);
    private static readonly Option<bool> WriteMetaJson = new Option<bool>("--write-meta-json") { Description = ResString.cmd_writeMetaJson }.WithDefault(true);
    private static readonly Option<bool> AppendUrlParams = new Option<bool>("--append-url-params") { Description = ResString.cmd_appendUrlParams }.WithDefault(false);
    private static readonly Option<bool> MP4RealTimeDecryption = new Option<bool>("--mp4-real-time-decryption") { Description = ResString.cmd_MP4RealTimeDecryption }.WithDefault(false);
    private static readonly Option<bool> UseShakaPackager = new Option<bool>("--use-shaka-packager") { Hidden = true, Description = ResString.cmd_useShakaPackager }.WithDefault(false);
    private static readonly Option<DecryptEngine> DecryptionEngine = new ("--decryption-engine") { Description = ResString.cmd_decryptionEngine, DefaultValueFactory = _ => DecryptEngine.MP4DECRYPT };
    private static readonly Option<bool> ForceAnsiConsole = new("--force-ansi-console") { Description = ResString.cmd_forceAnsiConsole };
    private static readonly Option<bool> NoAnsiColor = new("--no-ansi-color") { Description = ResString.cmd_noAnsiColor };
    private static readonly Option<string?> DecryptionBinaryPath = new("--decryption-binary-path") { HelpName = "PATH", Description = ResString.cmd_decryptionBinaryPath };
    private static readonly Option<string?> FFmpegBinaryPath = new("--ffmpeg-binary-path") { HelpName = "PATH", Description = ResString.cmd_ffmpegBinaryPath };
    private static readonly Option<string?> BaseUrl = new("--base-url") { Description = ResString.cmd_baseUrl };
    private static readonly Option<bool> ConcurrentDownload = new Option<bool>("-mt", "--concurrent-download") { Description = ResString.cmd_concurrentDownload }.WithDefault(false);
    private static readonly Option<bool> NoLog = new Option<bool>("--no-log") { Description = ResString.cmd_noLog }.WithDefault(false);
    private static readonly Option<bool> AllowHlsMultiExtMap = new Option<bool>("--allow-hls-multi-ext-map") { Description = ResString.cmd_allowHlsMultiExtMap }.WithDefault(false);
    private static readonly Option<string[]?> AdKeywords = new("--ad-keyword") { HelpName = "REG", Description = ResString.cmd_adKeyword };
    private static readonly Option<long?> MaxSpeed = new("-R", "--max-speed") { HelpName = "SPEED", Description = ResString.cmd_maxSpeed, CustomParser = ParseSpeedLimit };


    // 代理选项
    private static readonly Option<bool> UseSystemProxy = new Option<bool>("--use-system-proxy") { Description = ResString.cmd_useSystemProxy }.WithDefault(true);
    private static readonly Option<WebProxy?> CustomProxy = new("--custom-proxy") { HelpName = "URL", Description = ResString.cmd_customProxy, CustomParser = ParseProxy};

    // 只下载部分分片
    private static readonly Option<CustomRange?> CustomRange = new("--custom-range") { HelpName = "RANGE", Description = ResString.cmd_customRange, CustomParser = ParseCustomRange };


    // morehelp
    private static readonly Option<string?> MoreHelp = new("--morehelp") { HelpName = "OPTION", Description = ResString.cmd_moreHelp };

    // 自定义KEY等
    private static readonly Option<EncryptMethod?> CustomHLSMethod = new("--custom-hls-method") { HelpName = "METHOD", Description = ResString.cmd_customHLSMethod };
    private static readonly Option<byte[]?> CustomHLSKey = new("--custom-hls-key") { HelpName = "FILE|HEX|BASE64", Description = ResString.cmd_customHLSKey, CustomParser = ParseHLSCustomKey };
    private static readonly Option<byte[]?> CustomHLSIv = new(name: "--custom-hls-iv") { HelpName = "FILE|HEX|BASE64", Description = ResString.cmd_customHLSIv, CustomParser = ParseHLSCustomKey };
    private static readonly Option<string[]?> Keys = new("--key") { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, Description = ResString.cmd_keys, CustomParser = ParseCustomKeys};

    // 任务开始时间
    private static readonly Option<DateTime?> TaskStartAt = new("--task-start-at") { HelpName = "yyyyMMddHHmmss", Description = ResString.cmd_taskStartAt, CustomParser = ParseStartTime };


    // 直播相关
    private static readonly Option<bool> LivePerformAsVod = new Option<bool>("--live-perform-as-vod") { Description = ResString.cmd_livePerformAsVod }.WithDefault(false);
    private static readonly Option<bool> LiveRealTimeMerge = new Option<bool>("--live-real-time-merge") { Description = ResString.cmd_liveRealTimeMerge }.WithDefault(false);
    private static readonly Option<bool> LiveKeepSegments = new Option<bool>("--live-keep-segments") { Description = ResString.cmd_liveKeepSegments }.WithDefault(true);
    private static readonly Option<bool> LivePipeMux = new Option<bool>("--live-pipe-mux") { Description = ResString.cmd_livePipeMux }.WithDefault(false);
    private static readonly Option<TimeSpan?> LiveRecordLimit = new("--live-record-limit") { HelpName = "HH:mm:ss", Description = ResString.cmd_liveRecordLimit, CustomParser = ParseLiveLimit };
    private static readonly Option<int?> LiveWaitTime = new("--live-wait-time") { HelpName = "SEC", Description = ResString.cmd_liveWaitTime };
    private static readonly Option<int> LiveTakeCount = new("--live-take-count") { HelpName = "NUM", Description = ResString.cmd_liveTakeCount, DefaultValueFactory = _ => 16 };
    private static readonly Option<bool> LiveFixVttByAudio = new Option<bool>("--live-fix-vtt-by-audio") { Description = ResString.cmd_liveFixVttByAudio }.WithDefault(false);


    // 复杂命令行如下
    private static readonly Option<MuxOptions?> MuxAfterDone = new("-M", "--mux-after-done") { HelpName = "OPTIONS", Description = ResString.cmd_muxAfterDone, CustomParser = ParseMuxAfterDone };
    private static readonly Option<List<OutputFile>> MuxImports = new("--mux-import") { HelpName = "OPTIONS", Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, Description = ResString.cmd_muxImport, CustomParser = ParseImports };
    private static readonly Option<StreamFilter?> VideoFilter = new("-sv", "--select-video") { HelpName = "OPTIONS", Description = ResString.cmd_selectVideo, CustomParser = ParseStreamFilter };
    private static readonly Option<StreamFilter?> AudioFilter = new("-sa", "--select-audio") { HelpName = "OPTIONS", Description = ResString.cmd_selectAudio, CustomParser = ParseStreamFilter };
    private static readonly Option<StreamFilter?> SubtitleFilter = new("-ss", "--select-subtitle") { HelpName = "OPTIONS", Description = ResString.cmd_selectSubtitle, CustomParser = ParseStreamFilter };

    private static readonly Option<StreamFilter?> DropVideoFilter = new("-dv", "--drop-video") { HelpName = "OPTIONS", Description = ResString.cmd_dropVideo, CustomParser = ParseStreamFilter };
    private static readonly Option<StreamFilter?> DropAudioFilter = new("-da", "--drop-audio") { HelpName = "OPTIONS", Description = ResString.cmd_dropAudio, CustomParser = ParseStreamFilter };
    private static readonly Option<StreamFilter?> DropSubtitleFilter = new("-ds", "--drop-subtitle") { HelpName = "OPTIONS", Description = ResString.cmd_dropSubtitle, CustomParser = ParseStreamFilter };

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
    private static List<OutputFile> ParseImports(ArgumentResult result)
    {
        var imports = new List<OutputFile>();

        foreach (var item in result.Tokens)
        {
            var p = new ComplexParamParser(item.Value);
            var path = p.GetValue("path") ?? item.Value; // 若未获取到，直接整个字符串作为path
            var lang = p.GetValue("lang");
            var name = p.GetValue("name");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                result.AddError("path empty or file not exists!");
                return imports;
            }
            imports.Add(new OutputFile()
            {
                Index = 999,
                FilePath = path,
                LangCode = lang,
                Description = name
            });
        }

        return imports;
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
    {
        var allTokens = result.Tokens.Select(x => x.Value).ToList();
        List<string> optionNames = [option.Name, ..option.Aliases];
        return optionNames.Any(x => allTokens.Contains(x));
    }
    
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
            DownloadRetryCount = result.GetValue(DownloadRetryCount),
            HttpRequestTimeout = result.GetValue(HttpRequestTimeout),
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
            CustomRange = result.GetValue(CustomRange),
            LiveWaitTime = result.GetValue(LiveWaitTime),
            LiveTakeCount = result.GetValue(LiveTakeCount),
            NoDateInfo = result.GetValue(NoDateInfo),
            NoLog = result.GetValue(NoLog),
            AllowHlsMultiExtMap = result.GetValue(AllowHlsMultiExtMap),
            AdKeywords = result.GetValue(AdKeywords),
            MaxSpeed = result.GetValue(MaxSpeed),
        };

        if (result.HasOption(CustomHLSMethod)) option.CustomHLSMethod = result.GetValue(CustomHLSMethod);
        if (result.HasOption(CustomHLSKey)) option.CustomHLSKey = result.GetValue(CustomHLSKey);
        if (result.HasOption(CustomHLSIv)) option.CustomHLSIv = result.GetValue(CustomHLSIv);

        var parsedHeaders = result.GetValue(Headers);
        if (parsedHeaders != null)
            option.Headers = parsedHeaders;


        // 以用户选择语言为准优先
        if (option.UILanguage != null)
        {
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(option.UILanguage);
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(option.UILanguage);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(option.UILanguage);
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


    public static async Task<int> InvokeArgs(string[] args, Func<MyOption, Task> action)
    {
        var argList = new List<string>(args);
        var index = -1;
        if ((index = argList.IndexOf("--morehelp")) >= 0 && argList.Count > index + 1)
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
                _ => $"Option=\"{option}\" not found"
            };
            Console.WriteLine($"More Help:\r\n\r\n  --{option}\r\n\r\n" + msg);
            Environment.Exit(0);
        }

        var rootCommand = new RootCommand(VERSION_INFO)
        {
            Input, TmpDir, SaveDir, SaveName, SavePattern, LogFilePath, BaseUrl, ThreadCount, DownloadRetryCount, HttpRequestTimeout, ForceAnsiConsole, NoAnsiColor,AutoSelect, SkipMerge, SkipDownload, CheckSegmentsCount,
            BinaryMerge, UseFFmpegConcatDemuxer, DelAfterDone, NoDateInfo, NoLog, WriteMetaJson, AppendUrlParams, ConcurrentDownload, Headers, SubOnly, SubtitleFormat, AutoSubtitleFix,
            FFmpegBinaryPath,
            LogLevel, UILanguage, UrlProcessorArgs, Keys, KeyTextFile, DecryptionEngine, DecryptionBinaryPath, UseShakaPackager, MP4RealTimeDecryption,
            MaxSpeed,
            MuxAfterDone,
            CustomHLSMethod, CustomHLSKey, CustomHLSIv, UseSystemProxy, CustomProxy, CustomRange, TaskStartAt,
            LivePerformAsVod, LiveRealTimeMerge, LiveKeepSegments, LivePipeMux, LiveFixVttByAudio, LiveRecordLimit, LiveWaitTime, LiveTakeCount,
            MuxImports, VideoFilter, AudioFilter, SubtitleFilter, DropVideoFilter, DropAudioFilter, DropSubtitleFilter, AdKeywords, DisableUpdateCheck, AllowHlsMultiExtMap, MoreHelp
        };

        rootCommand.TreatUnmatchedTokensAsErrors = true;
        rootCommand.SetAction(parseResult =>
        {
            var myOption = GetOptions(parseResult);
            return action(myOption);
        });

        var config = new ParserConfiguration
        {
            EnablePosixBundling = false
        };

        try
        {
            var parseResult = rootCommand.Parse(args, config);
            var exitCode = await parseResult.InvokeAsync();
            Environment.Exit(exitCode);
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
