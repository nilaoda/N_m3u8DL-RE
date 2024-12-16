using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Enum;
using N_m3u8DL_RE.Util;
using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace N_m3u8DL_RE.CommandLine;

internal static partial class CommandInvoker
{
    public const string VERSION_INFO = "N_m3u8DL-RE (Beta version) 20241216";

    [GeneratedRegex("((best|worst)\\d*|all)")]
    private static partial Regex ForStrRegex();
    [GeneratedRegex(@"(\d*)-(\d*)")]
    private static partial Regex RangeRegex();
    [GeneratedRegex(@"([\d\\.]+)(M|K)")]
    private static partial Regex SpeedStrRegex();

    private static readonly Argument<string> Input = new(name: "input", description: ResString.cmd_Input);
    private static readonly Option<string?> TmpDir = new(["--tmp-dir"], description: ResString.cmd_tmpDir);
    private static readonly Option<string?> SaveDir = new(["--save-dir"], description: ResString.cmd_saveDir);
    private static readonly Option<string?> SaveName = new(["--save-name"], description: ResString.cmd_saveName, parseArgument: ParseSaveName);
    private static readonly Option<string?> SavePattern = new(["--save-pattern"], description: ResString.cmd_savePattern, getDefaultValue: () => "<SaveName>_<Id>_<Codecs>_<Language>_<Ext>");
    private static readonly Option<string?> UILanguage = new Option<string?>(["--ui-language"], description: ResString.cmd_uiLanguage).FromAmong("en-US", "zh-CN", "zh-TW");
    private static readonly Option<string?> UrlProcessorArgs = new(["--urlprocessor-args"], description: ResString.cmd_urlProcessorArgs);
    private static readonly Option<string[]?> Keys = new(["--key"], description: ResString.cmd_keys) { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private static readonly Option<string> KeyTextFile = new(["--key-text-file"], description: ResString.cmd_keyText);
    private static readonly Option<Dictionary<string, string>> Headers = new(["-H", "--header"], description: ResString.cmd_header, parseArgument: ParseHeaders) { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false };
    private static readonly Option<LogLevel> LogLevel = new(name: "--log-level", description: ResString.cmd_logLevel, getDefaultValue: () => Common.Log.LogLevel.INFO);
    private static readonly Option<SubtitleFormat> SubtitleFormat = new(name: "--sub-format", description: ResString.cmd_subFormat, getDefaultValue: () => Enum.SubtitleFormat.SRT);
    private static readonly Option<bool> DisableUpdateCheck = new(["--disable-update-check"], description: ResString.cmd_disableUpdateCheck, getDefaultValue: () => false);
    private static readonly Option<bool> AutoSelect = new(["--auto-select"], description: ResString.cmd_autoSelect, getDefaultValue: () => false);
    private static readonly Option<bool> SubOnly = new(["--sub-only"], description: ResString.cmd_subOnly, getDefaultValue: () => false);
    private static readonly Option<int> ThreadCount = new(["--thread-count"], description: ResString.cmd_threadCount, getDefaultValue: () => Environment.ProcessorCount) { ArgumentHelpName = "number" };
    private static readonly Option<int> DownloadRetryCount = new(["--download-retry-count"], description: ResString.cmd_downloadRetryCount, getDefaultValue: () => 3) { ArgumentHelpName = "number" };
    private static readonly Option<double> HttpRequestTimeout = new(["--http-request-timeout"], description: ResString.cmd_httpRequestTimeout, getDefaultValue: () => 100) { ArgumentHelpName = "seconds" };
    private static readonly Option<bool> SkipMerge = new(["--skip-merge"], description: ResString.cmd_skipMerge, getDefaultValue: () => false);
    private static readonly Option<bool> SkipDownload = new(["--skip-download"], description: ResString.cmd_skipDownload, getDefaultValue: () => false);
    private static readonly Option<bool> NoDateInfo = new(["--no-date-info"], description: ResString.cmd_noDateInfo, getDefaultValue: () => false);
    private static readonly Option<bool> BinaryMerge = new(["--binary-merge"], description: ResString.cmd_binaryMerge, getDefaultValue: () => false);
    private static readonly Option<bool> UseFFmpegConcatDemuxer = new(["--use-ffmpeg-concat-demuxer"], description: ResString.cmd_useFFmpegConcatDemuxer, getDefaultValue: () => false);
    private static readonly Option<bool> DelAfterDone = new(["--del-after-done"], description: ResString.cmd_delAfterDone, getDefaultValue: () => true);
    private static readonly Option<bool> AutoSubtitleFix = new(["--auto-subtitle-fix"], description: ResString.cmd_subtitleFix, getDefaultValue: () => true);
    private static readonly Option<bool> CheckSegmentsCount = new(["--check-segments-count"], description: ResString.cmd_checkSegmentsCount, getDefaultValue: () => true);
    private static readonly Option<bool> WriteMetaJson = new(["--write-meta-json"], description: ResString.cmd_writeMetaJson, getDefaultValue: () => true);
    private static readonly Option<bool> AppendUrlParams = new(["--append-url-params"], description: ResString.cmd_appendUrlParams, getDefaultValue: () => false);
    private static readonly Option<bool> MP4RealTimeDecryption = new (["--mp4-real-time-decryption"], description: ResString.cmd_MP4RealTimeDecryption, getDefaultValue: () => false);
    private static readonly Option<bool> UseShakaPackager = new (["--use-shaka-packager"], description: ResString.cmd_useShakaPackager, getDefaultValue: () => false) { IsHidden = true };
    private static readonly Option<DecryptEngine> DecryptionEngine = new (["--decryption-engine"], description: ResString.cmd_decryptionEngine, getDefaultValue: () => DecryptEngine.MP4DECRYPT);
    private static readonly Option<bool> ForceAnsiConsole = new(["--force-ansi-console"], description: ResString.cmd_forceAnsiConsole);
    private static readonly Option<bool> NoAnsiColor = new(["--no-ansi-color"], description: ResString.cmd_noAnsiColor);
    private static readonly Option<string?> DecryptionBinaryPath = new(["--decryption-binary-path"], description: ResString.cmd_decryptionBinaryPath) { ArgumentHelpName = "PATH" };
    private static readonly Option<string?> FFmpegBinaryPath = new(["--ffmpeg-binary-path"], description: ResString.cmd_ffmpegBinaryPath) { ArgumentHelpName = "PATH" };
    private static readonly Option<string?> BaseUrl = new(["--base-url"], description: ResString.cmd_baseUrl);
    private static readonly Option<bool> ConcurrentDownload = new(["-mt", "--concurrent-download"], description: ResString.cmd_concurrentDownload, getDefaultValue: () => false);
    private static readonly Option<bool> NoLog = new(["--no-log"], description: ResString.cmd_noLog, getDefaultValue: () => false);
    private static readonly Option<bool> AllowHlsMultiExtMap = new(["--allow-hls-multi-ext-map"], description: ResString.cmd_allowHlsMultiExtMap, getDefaultValue: () => false);
    private static readonly Option<string[]?> AdKeywords = new(["--ad-keyword"], description: ResString.cmd_adKeyword) { ArgumentHelpName = "REG" };
    private static readonly Option<long?> MaxSpeed = new(["-R", "--max-speed"], description: ResString.cmd_maxSpeed, parseArgument: ParseSpeedLimit) { ArgumentHelpName = "SPEED" };


    // 代理选项
    private static readonly Option<bool> UseSystemProxy = new(["--use-system-proxy"], description: ResString.cmd_useSystemProxy, getDefaultValue: () => true);
    private static readonly Option<WebProxy?> CustomProxy = new(["--custom-proxy"], description: ResString.cmd_customProxy, parseArgument: ParseProxy) { ArgumentHelpName = "URL" };

    // 只下载部分分片
    private static readonly Option<CustomRange?> CustomRange = new(["--custom-range"], description: ResString.cmd_customRange, parseArgument: ParseCustomRange) { ArgumentHelpName = "RANGE" };


    // morehelp
    private static readonly Option<string?> MoreHelp = new(["--morehelp"], description: ResString.cmd_moreHelp) { ArgumentHelpName = "OPTION" };

    // 自定义KEY等
    private static readonly Option<EncryptMethod?> CustomHLSMethod = new(name: "--custom-hls-method", description: ResString.cmd_customHLSMethod) { ArgumentHelpName = "METHOD" };
    private static readonly Option<byte[]?> CustomHLSKey = new(name: "--custom-hls-key", description: ResString.cmd_customHLSKey, parseArgument: ParseHLSCustomKey) { ArgumentHelpName = "FILE|HEX|BASE64" };
    private static readonly Option<byte[]?> CustomHLSIv = new(name: "--custom-hls-iv", description: ResString.cmd_customHLSIv, parseArgument: ParseHLSCustomKey) { ArgumentHelpName = "FILE|HEX|BASE64" };

    // 任务开始时间
    private static readonly Option<DateTime?> TaskStartAt = new(["--task-start-at"], description: ResString.cmd_taskStartAt, parseArgument: ParseStartTime) { ArgumentHelpName = "yyyyMMddHHmmss" };


    // 直播相关
    private static readonly Option<bool> LivePerformAsVod = new(["--live-perform-as-vod"], description: ResString.cmd_livePerformAsVod, getDefaultValue: () => false);
    private static readonly Option<bool> LiveRealTimeMerge = new(["--live-real-time-merge"], description: ResString.cmd_liveRealTimeMerge, getDefaultValue: () => false);
    private static readonly Option<bool> LiveKeepSegments = new(["--live-keep-segments"], description: ResString.cmd_liveKeepSegments, getDefaultValue: () => true);
    private static readonly Option<bool> LivePipeMux = new(["--live-pipe-mux"], description: ResString.cmd_livePipeMux, getDefaultValue: () => false);
    private static readonly Option<TimeSpan?> LiveRecordLimit = new(["--live-record-limit"], description: ResString.cmd_liveRecordLimit, parseArgument: ParseLiveLimit) { ArgumentHelpName = "HH:mm:ss" };
    private static readonly Option<int?> LiveWaitTime = new(["--live-wait-time"], description: ResString.cmd_liveWaitTime) { ArgumentHelpName = "SEC" };
    private static readonly Option<int> LiveTakeCount = new(["--live-take-count"], description: ResString.cmd_liveTakeCount, getDefaultValue: () => 16) { ArgumentHelpName = "NUM" };
    private static readonly Option<bool> LiveFixVttByAudio = new(["--live-fix-vtt-by-audio"], description: ResString.cmd_liveFixVttByAudio, getDefaultValue: () => false);


    // 复杂命令行如下
    private static readonly Option<MuxOptions?> MuxAfterDone = new(["-M", "--mux-after-done"], description: ResString.cmd_muxAfterDone, parseArgument: ParseMuxAfterDone) { ArgumentHelpName = "OPTIONS" };
    private static readonly Option<List<OutputFile>> MuxImports = new("--mux-import", description: ResString.cmd_muxImport, parseArgument: ParseImports) { Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false, ArgumentHelpName = "OPTIONS" };
    private static readonly Option<StreamFilter?> VideoFilter = new(["-sv", "--select-video"], description: ResString.cmd_selectVideo, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };
    private static readonly Option<StreamFilter?> AudioFilter = new(["-sa", "--select-audio"], description: ResString.cmd_selectAudio, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };
    private static readonly Option<StreamFilter?> SubtitleFilter = new(["-ss", "--select-subtitle"], description: ResString.cmd_selectSubtitle, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };

    private static readonly Option<StreamFilter?> DropVideoFilter = new(["-dv", "--drop-video"], description: ResString.cmd_dropVideo, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };
    private static readonly Option<StreamFilter?> DropAudioFilter = new(["-da", "--drop-audio"], description: ResString.cmd_dropAudio, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };
    private static readonly Option<StreamFilter?> DropSubtitleFilter = new(["-ds", "--drop-subtitle"], description: ResString.cmd_dropSubtitle, parseArgument: ParseStreamFilter) { ArgumentHelpName = "OPTIONS" };

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
            result.ErrorMessage = "error in parse SpeedLimit: " + input;
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
            result.ErrorMessage = $"error in parse CustomRange: " + ex.Message;
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
            result.ErrorMessage = $"error in parse proxy: " + ex.Message;
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
            result.ErrorMessage = "error in parse hls custom key: " + input;
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
            result.ErrorMessage = "error in parse LiveRecordLimit: " + input;
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
            result.ErrorMessage = "error in parse TaskStartTime: " + input;
            return null;
        }
    }

    private static string? ParseSaveName(ArgumentResult result)
    {
        var input = result.Tokens[0].Value;
        var newName = OtherUtil.GetValidFileName(input);
        if (string.IsNullOrEmpty(newName))
        {
            result.ErrorMessage = "Invalid save name!";
            return null;
        }
        return newName;
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
                result.ErrorMessage = $"for={forStr} not valid";
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
                result.ErrorMessage = "path empty or file not exists!";
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
            result.ErrorMessage = $"format={format} not valid";
            return null;
        }
        // 混流器
        var muxer = p.GetValue("muxer") ?? "ffmpeg";
        if (muxer != "ffmpeg" && muxer != "mkvmerge")
        {
            result.ErrorMessage = $"muxer={muxer} not valid";
            return null;
        }
        // 混流器路径
        var bin_path = p.GetValue("bin_path") ?? "auto";
        if (string.IsNullOrEmpty(bin_path))
        {
            result.ErrorMessage = $"bin_path={bin_path} not valid";
            return null;
        }
        // 是否删除
        var keep = p.GetValue("keep") ?? "false";
        if (keep != "true" && keep != "false")
        {
            result.ErrorMessage = $"keep={keep} not valid";
            return null;
        }
        // 是否忽略字幕
        var skipSub = p.GetValue("skip_sub") ?? "false";
        if (skipSub != "true" && skipSub != "false")
        {
            result.ErrorMessage = $"skip_sub={keep} not valid";
            return null;
        }
        // 冲突检测
        if (muxer == "mkvmerge" && format == "mp4")
        {
            result.ErrorMessage = $"mkvmerge can not do mp4";
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

    class MyOptionBinder : BinderBase<MyOption>
    {
        protected override MyOption GetBoundValue(BindingContext bindingContext)
        {
            var option = new MyOption
            {
                Input = bindingContext.ParseResult.GetValueForArgument(Input),
                ForceAnsiConsole = bindingContext.ParseResult.GetValueForOption(ForceAnsiConsole),
                NoAnsiColor = bindingContext.ParseResult.GetValueForOption(NoAnsiColor),
                LogLevel = bindingContext.ParseResult.GetValueForOption(LogLevel),
                AutoSelect = bindingContext.ParseResult.GetValueForOption(AutoSelect),
                DisableUpdateCheck = bindingContext.ParseResult.GetValueForOption(DisableUpdateCheck),
                SkipMerge = bindingContext.ParseResult.GetValueForOption(SkipMerge),
                BinaryMerge = bindingContext.ParseResult.GetValueForOption(BinaryMerge),
                UseFFmpegConcatDemuxer = bindingContext.ParseResult.GetValueForOption(UseFFmpegConcatDemuxer),
                DelAfterDone = bindingContext.ParseResult.GetValueForOption(DelAfterDone),
                AutoSubtitleFix = bindingContext.ParseResult.GetValueForOption(AutoSubtitleFix),
                CheckSegmentsCount = bindingContext.ParseResult.GetValueForOption(CheckSegmentsCount),
                SubtitleFormat = bindingContext.ParseResult.GetValueForOption(SubtitleFormat),
                SubOnly = bindingContext.ParseResult.GetValueForOption(SubOnly),
                TmpDir = bindingContext.ParseResult.GetValueForOption(TmpDir),
                SaveDir = bindingContext.ParseResult.GetValueForOption(SaveDir),
                SaveName = bindingContext.ParseResult.GetValueForOption(SaveName),
                ThreadCount = bindingContext.ParseResult.GetValueForOption(ThreadCount),
                UILanguage = bindingContext.ParseResult.GetValueForOption(UILanguage),
                SkipDownload = bindingContext.ParseResult.GetValueForOption(SkipDownload),
                WriteMetaJson = bindingContext.ParseResult.GetValueForOption(WriteMetaJson),
                AppendUrlParams = bindingContext.ParseResult.GetValueForOption(AppendUrlParams),
                SavePattern = bindingContext.ParseResult.GetValueForOption(SavePattern),
                Keys = bindingContext.ParseResult.GetValueForOption(Keys),
                UrlProcessorArgs = bindingContext.ParseResult.GetValueForOption(UrlProcessorArgs),
                MP4RealTimeDecryption = bindingContext.ParseResult.GetValueForOption(MP4RealTimeDecryption),
                UseShakaPackager = bindingContext.ParseResult.GetValueForOption(UseShakaPackager),
                DecryptionEngine = bindingContext.ParseResult.GetValueForOption(DecryptionEngine),
                DecryptionBinaryPath = bindingContext.ParseResult.GetValueForOption(DecryptionBinaryPath),
                FFmpegBinaryPath = bindingContext.ParseResult.GetValueForOption(FFmpegBinaryPath),
                KeyTextFile = bindingContext.ParseResult.GetValueForOption(KeyTextFile),
                DownloadRetryCount = bindingContext.ParseResult.GetValueForOption(DownloadRetryCount),
                HttpRequestTimeout = bindingContext.ParseResult.GetValueForOption(HttpRequestTimeout),
                BaseUrl = bindingContext.ParseResult.GetValueForOption(BaseUrl),
                MuxImports = bindingContext.ParseResult.GetValueForOption(MuxImports),
                ConcurrentDownload = bindingContext.ParseResult.GetValueForOption(ConcurrentDownload),
                VideoFilter = bindingContext.ParseResult.GetValueForOption(VideoFilter),
                AudioFilter = bindingContext.ParseResult.GetValueForOption(AudioFilter),
                SubtitleFilter = bindingContext.ParseResult.GetValueForOption(SubtitleFilter),
                DropVideoFilter = bindingContext.ParseResult.GetValueForOption(DropVideoFilter),
                DropAudioFilter = bindingContext.ParseResult.GetValueForOption(DropAudioFilter),
                DropSubtitleFilter = bindingContext.ParseResult.GetValueForOption(DropSubtitleFilter),
                LiveRealTimeMerge = bindingContext.ParseResult.GetValueForOption(LiveRealTimeMerge),
                LiveKeepSegments = bindingContext.ParseResult.GetValueForOption(LiveKeepSegments),
                LiveRecordLimit = bindingContext.ParseResult.GetValueForOption(LiveRecordLimit),
                TaskStartAt = bindingContext.ParseResult.GetValueForOption(TaskStartAt),
                LivePerformAsVod = bindingContext.ParseResult.GetValueForOption(LivePerformAsVod),
                LivePipeMux = bindingContext.ParseResult.GetValueForOption(LivePipeMux),
                LiveFixVttByAudio = bindingContext.ParseResult.GetValueForOption(LiveFixVttByAudio),
                UseSystemProxy = bindingContext.ParseResult.GetValueForOption(UseSystemProxy),
                CustomProxy = bindingContext.ParseResult.GetValueForOption(CustomProxy),
                CustomRange = bindingContext.ParseResult.GetValueForOption(CustomRange),
                LiveWaitTime = bindingContext.ParseResult.GetValueForOption(LiveWaitTime),
                LiveTakeCount = bindingContext.ParseResult.GetValueForOption(LiveTakeCount),
                NoDateInfo = bindingContext.ParseResult.GetValueForOption(NoDateInfo),
                NoLog = bindingContext.ParseResult.GetValueForOption(NoLog),
                AllowHlsMultiExtMap = bindingContext.ParseResult.GetValueForOption(AllowHlsMultiExtMap),
                AdKeywords = bindingContext.ParseResult.GetValueForOption(AdKeywords),
                MaxSpeed = bindingContext.ParseResult.GetValueForOption(MaxSpeed),
            };

            if (bindingContext.ParseResult.HasOption(CustomHLSMethod)) option.CustomHLSMethod = bindingContext.ParseResult.GetValueForOption(CustomHLSMethod);
            if (bindingContext.ParseResult.HasOption(CustomHLSKey)) option.CustomHLSKey = bindingContext.ParseResult.GetValueForOption(CustomHLSKey);
            if (bindingContext.ParseResult.HasOption(CustomHLSIv)) option.CustomHLSIv = bindingContext.ParseResult.GetValueForOption(CustomHLSIv);

            var parsedHeaders = bindingContext.ParseResult.GetValueForOption(Headers);
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
            var muxAfterDoneValue = bindingContext.ParseResult.GetValueForOption(MuxAfterDone);
            if (muxAfterDoneValue == null) return option;
            
            option.MuxAfterDone = true;
            option.MuxOptions = muxAfterDoneValue;
            if (muxAfterDoneValue.UseMkvmerge) option.MkvmergeBinaryPath = muxAfterDoneValue.BinPath;
            else option.FFmpegBinaryPath ??= muxAfterDoneValue.BinPath;

            return option;
        }
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
            Input, TmpDir, SaveDir, SaveName, BaseUrl, ThreadCount, DownloadRetryCount, HttpRequestTimeout, ForceAnsiConsole, NoAnsiColor,AutoSelect, SkipMerge, SkipDownload, CheckSegmentsCount,
            BinaryMerge, UseFFmpegConcatDemuxer, DelAfterDone, NoDateInfo, NoLog, WriteMetaJson, AppendUrlParams, ConcurrentDownload, Headers, /**SavePattern,**/ SubOnly, SubtitleFormat, AutoSubtitleFix,
            FFmpegBinaryPath,
            LogLevel, UILanguage, UrlProcessorArgs, Keys, KeyTextFile, DecryptionEngine, DecryptionBinaryPath, UseShakaPackager, MP4RealTimeDecryption,
            MaxSpeed,
            MuxAfterDone,
            CustomHLSMethod, CustomHLSKey, CustomHLSIv, UseSystemProxy, CustomProxy, CustomRange, TaskStartAt,
            LivePerformAsVod, LiveRealTimeMerge, LiveKeepSegments, LivePipeMux, LiveFixVttByAudio, LiveRecordLimit, LiveWaitTime, LiveTakeCount,
            MuxImports, VideoFilter, AudioFilter, SubtitleFilter, DropVideoFilter, DropAudioFilter, DropSubtitleFilter, AdKeywords, DisableUpdateCheck, AllowHlsMultiExtMap, MoreHelp
        };

        rootCommand.TreatUnmatchedTokensAsErrors = true;
        rootCommand.SetHandler(async myOption => await action(myOption), new MyOptionBinder());

        var parser = new CommandLineBuilder(rootCommand)
            .UseDefaults()
            .EnablePosixBundling(false)
            .UseExceptionHandler((ex, context) =>
            {
                try { Console.CursorVisible = true; } catch { }
                string msg = Logger.LogLevel == Common.Log.LogLevel.DEBUG ? ex.ToString() : ex.Message;
#if DEBUG
                msg = ex.ToString();
#endif
                Logger.Error(msg);
                Thread.Sleep(3000);
                Environment.Exit(1);
            }, 1)
            .Build();

        return await parser.InvokeAsync(args);
    }
}
