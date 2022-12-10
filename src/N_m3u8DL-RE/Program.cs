using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser;
using Spectre.Console;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using System.Globalization;
using System.Text;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Processor;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Util;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.CommandLine;
using System.Net;
using System.Net.Http.Headers;

namespace N_m3u8DL_RE
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            ServicePointManager.DefaultConnectionLimit = 1024;
            try { Console.CursorVisible = true; } catch { }
            string loc = "en-US";
            string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
            if (currLoc == "zh-CN" || currLoc == "zh-SG") loc = "zh-CN";
            else if (currLoc.StartsWith("zh-")) loc = "zh-TW";

            //处理用户-h等请求
            var index = -1;
            var list = new List<string>(args);
            if ((index = list.IndexOf("--ui-language")) != -1 && list.Count > index + 1 && new List<string> { "en-US", "zh-CN", "zh-TW" }.Contains(list[index + 1]))
            {
                loc = list[index + 1];
            }

            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(loc);
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(loc);
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(loc);


            await CommandInvoker.InvokeArgs(args, DoWorkAsync);
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            Logger.WarnMarkUp("Force Exit...");
            try 
            { 
                Console.CursorVisible = true;
                if (!OperatingSystem.IsWindows())
                    System.Diagnostics.Process.Start("stty", "echo");
            } catch { }
            Environment.Exit(0);
        }

        static int GetOrder(StreamSpec streamSpec)
        {
            if (streamSpec.Channels == null) return 0;
            else
            {
                var str = streamSpec.Channels.Split('/')[0];
                return int.TryParse(str, out var order) ? order : 0;
            }
        }

        static async Task DoWorkAsync(MyOption option)
        {
            //检测更新
            CheckUpdateAsync();

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

            //检查互斥的选项

            if (!option.MuxAfterDone && option.MuxImports != null && option.MuxImports.Count > 0)
            {
                throw new ArgumentException("MuxAfterDone disabled, MuxImports not allowed!");
            }

            //预先检查ffmpeg
            if (option.FFmpegBinaryPath == null)
                option.FFmpegBinaryPath = GlobalUtil.FindExecutable("ffmpeg");

            if (string.IsNullOrEmpty(option.FFmpegBinaryPath) || !File.Exists(option.FFmpegBinaryPath))
            {
                throw new FileNotFoundException(ResString.ffmpegNotFound);
            }

            //预先检查mkvmerge
            if (option.UseMkvmerge && option.MuxAfterDone)
            {
                if (option.MkvmergeBinaryPath == null)
                    option.MkvmergeBinaryPath = GlobalUtil.FindExecutable("mkvmerge");
                if (string.IsNullOrEmpty(option.MkvmergeBinaryPath) || !File.Exists(option.MkvmergeBinaryPath))
                {
                    throw new FileNotFoundException("mkvmerge not found");
                }
            }

            //预先检查
            if ((option.Keys != null && option.Keys.Length > 0) || option.KeyTextFile != null)
            {
                if (string.IsNullOrEmpty(option.DecryptionBinaryPath))
                {
                    if (option.UseShakaPackager)
                    {
                        var file = GlobalUtil.FindExecutable("shaka-packager");
                        var file2 = GlobalUtil.FindExecutable("packager-linux-x64");
                        var file3 = GlobalUtil.FindExecutable("packager-osx-x64");
                        var file4 = GlobalUtil.FindExecutable("packager-win-x64");
                        if (file == null && file2 == null && file3 == null && file4 == null) throw new FileNotFoundException("shaka-packager not found!");
                        option.DecryptionBinaryPath = file ?? file2 ?? file3 ?? file4;
                    }
                    else
                    {
                        var file = GlobalUtil.FindExecutable("mp4decrypt");
                        if (file == null) throw new FileNotFoundException("mp4decrypt not found!");
                        option.DecryptionBinaryPath = file;
                    }
                }
                else if (!File.Exists(option.DecryptionBinaryPath))
                {
                    throw new FileNotFoundException(option.DecryptionBinaryPath);
                }
            }

            //默认的headers
            var headers = new Dictionary<string, string>()
            {
                ["user-agent"] = "Mozilla/5.0 (Windows NT 10.0; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/78.0.3904.108 Safari/537.36"
            };
            //添加或替换用户输入的headers
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

            //demo1
            parserConfig.ContentProcessors.Insert(0, new DemoProcessor());
            //demo2
            parserConfig.KeyProcessors.Insert(0, new DemoProcessor2());
            //for www.nowehoryzonty.pl
            parserConfig.UrlProcessors.Insert(0, new NowehoryzontyUrlProcessor());

            //等待任务开始时间
            if (option.TaskStartAt != null && option.TaskStartAt > DateTime.Now)
            {
                Logger.InfoMarkUp(ResString.taskStartAt + option.TaskStartAt);
                while (option.TaskStartAt > DateTime.Now)
                {
                    await Task.Delay(1000);
                }
            }

            var url = option.Input;

            //流提取器配置
            var extractor = new StreamExtractor(parserConfig);
            extractor.LoadSourceFromUrl(url);

            //解析流信息
            var streams = await extractor.ExtractStreamsAsync();

            //全部媒体
            var lists = streams.OrderBy(p => p.MediaType).ThenByDescending(p => p.Bandwidth).ThenByDescending(GetOrder);
            //基本流
            var basicStreams = lists.Where(x => x.MediaType == null || x.MediaType == MediaType.VIDEO);
            //可选音频轨道
            var audios = lists.Where(x => x.MediaType == MediaType.AUDIO);
            //可选字幕轨道
            var subs = lists.Where(x => x.MediaType == MediaType.SUBTITLES);

            if (option.WriteMetaJson)
            {
                Logger.Warn(ResString.writeJson);
                await File.WriteAllTextAsync("meta.json", GlobalUtil.ConvertToJson(lists), Encoding.UTF8);
            }

            Logger.Info(ResString.streamsInfo, lists.Count(), basicStreams.Count(), audios.Count(), subs.Count());

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
                lists = basicStreams.Concat(audios).Concat(subs).OrderBy(x => true);
            }

            if (option.AutoSelect)
            {
                if (basicStreams.Any())
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
                //展示交互式选择框
                selectedStreams = FilterUtil.SelectStreams(lists);
            }

            if (!selectedStreams.Any())
                throw new Exception(ResString.noStreamsToDownload);

            //HLS: 选中流中若有没加载出playlist的，加载playlist
            //DASH/MSS: 加载playlist (调用url预处理器)
            if (selectedStreams.Any(s => s.Playlist == null) || extractor.ExtractorType == ExtractorType.MPEG_DASH || extractor.ExtractorType == ExtractorType.MSS)
                await extractor.FetchPlayListAsync(selectedStreams);

            //直播检测
            var livingFlag = selectedStreams.Any(s => s.Playlist?.IsLive == true) && !option.LivePerformAsVod;
            if (livingFlag)
            {
                Logger.WarnMarkUp($"[white on darkorange3_1]{ResString.liveFound}[/]");
            }

            //无法识别的加密方式，自动开启二进制合并
            if (selectedStreams.Any(s => s.Playlist.MediaParts.Any(p => p.MediaSegments.Any(m => m.EncryptInfo.Method == EncryptMethod.UNKNOWN))))
            {
                Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge3}[/]");
                option.BinaryMerge = true;
            }

            if (option.WriteMetaJson)
            {
                Logger.Warn(ResString.writeJson);
                await File.WriteAllTextAsync("meta_selected.json", GlobalUtil.ConvertToJson(selectedStreams), Encoding.UTF8);
            }

            Logger.Info(ResString.selectedStream);
            foreach (var item in selectedStreams)
            {
                Logger.InfoMarkUp(item.ToString());
            }

            if (option.SkipDownload)
            {
                return;
            }

#if DEBUG
            Console.ReadKey();
#endif

            //尝试从URL或文件读取文件名
            if (string.IsNullOrEmpty(option.SaveName))
            {
                option.SaveName = OtherUtil.GetFileNameFromInput(option.Input);
            }

            Logger.InfoMarkUp(ResString.saveName + $"[deepskyblue1]{option.SaveName.EscapeMarkup()}[/]");

            //下载配置
            var downloadConfig = new DownloaderConfig()
            {
                MyOptions = option,
                Headers = parserConfig.Headers, //使用命令行解析得到的Headers
            };

            var result = false;
            
            if (extractor.ExtractorType == ExtractorType.HTTP_LIVE)
            {
                var sldm = new HTTPLiveRecordManager(downloadConfig, selectedStreams, extractor);
                result = await sldm.StartRecordAsync();
            }
            else if(!livingFlag)
            {
                //开始下载
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

        //重定向
        static async Task<string> Get302Async(string url)
        {
            //this allows you to set the settings so that we can get the redirect url
            var handler = new HttpClientHandler()
            {
                AllowAutoRedirect = false
            };
            string redirectedUrl = "";
            using (HttpClient client = new(handler))
            using (HttpResponseMessage response = await client.GetAsync(url))
            using (HttpContent content = response.Content)
            {
                // ... Read the response to see if we have the redirected url
                if (response.StatusCode == System.Net.HttpStatusCode.Found)
                {
                    HttpResponseHeaders headers = response.Headers;
                    if (headers != null && headers.Location != null)
                    {
                        redirectedUrl = headers.Location.AbsoluteUri;
                    }
                }
            }

            return redirectedUrl;
        }
    }
}