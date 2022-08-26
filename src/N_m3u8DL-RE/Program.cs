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
using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
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

        static async Task DoWorkAsync(MyOption option)
        {
            Logger.LogLevel = option.LogLevel;

            try
            {
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

                var parserConfig = new ParserConfig()
                {
                    AppendUrlParams = option.AppendUrlParams,
                    UrlProcessorArgs = option.UrlProcessorArgs,
                    BaseUrl = option.BaseUrl!,
                    Headers = option.Headers
                };

                //demo1
                parserConfig.ContentProcessors.Insert(0, new DemoProcessor());
                //demo2
                parserConfig.KeyProcessors.Insert(0, new DemoProcessor2());
                //for www.nowehoryzonty.pl
                parserConfig.UrlProcessors.Insert(0, new NowehoryzontyUrlProcessor());

                var url = string.Empty;
                //url = "https://media.axprod.net/TestVectors/v7-Clear/Manifest_1080p.mpd"; //多音轨多字幕
                //url = "https://cmafref.akamaized.net/cmaf/live-ull/2006350/akambr/out.mpd"; //直播
                //url = "http://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8";
                //url = "https://vod.sdn.wavve.com/hls/S01/S01_E461382925.1/1/5000/chunklist.m3u8";
                url = "https://bitmovin-a.akamaihd.net/content/art-of-motion_drm/mpds/11331.mpd";
                //url = "http://tv-live.ynkmit.com/tv/anning.m3u8?txSecret=7528f35fb4b62bd24d55b891899db68f&txTime=632C8680"; //直播
                //url = "https://rest-as.ott.kaltura.com/api_v3/service/assetFile/action/playManifest/partnerId/147/assetId/1304099/assetType/media/assetFileId/16136929/contextType/PLAYBACK/isAltUrl/False/ks/djJ8MTQ3fMusTFH6PCZpcrfKLQwI-pPm9ex6b6r49wioe32WH2udXeM4reyWIkSDpi7HhvhxBHAHAKiHrcnkmIJQpyAt4MuDBG0ywGQ-jOeqQFcTRQ8BGJGw6g-smSBLwSbo4CCx9M9vWNJX3GkOfhoMAY4yRU-ur3okHiVq1mUJ82XBd_iVqLuzodnc9sJEtcHH0zc5CoPiTq2xor-dq3yDURnZm3isfSN3t9uLIJEW09oE-SJ84DM5GUuFUdbnIV8bdcWUsPicUg-Top1G2D3WcWXq4EvPnwvD8jrC_vsiOpLHf5akAwtdGsJ6__cXUmT7a-QlfjdvaZ5T8UhDLnttHmsxYs2E5c0lh4uOvvJou8dD8iYxUexlPI2j4QUkBRxqOEVLSNV3Y82-5TTRqgnK_uGYXHwk7EAmDws7hbLj2-DJ1heXDcye3OJYdunJgAS-9ma5zmQQNiY_HYh6wj2N1HpCTNAtWWga6R9fC0VgBTZbidW-YwMSGzIvMQfIfWKe15X7Oc_hCs-zGfW9XeRJZrutcWKK_D_HlzpQVBF2vIF3XgaI/a.mpd";
                //url = "https://dash.akamaized.net/dash264/TestCases/2c/qualcomm/1/MultiResMPEG2.mpd";
                //url = "http://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8";
                url = "https://cmaf.lln.latam.hbomaxcdn.com/videos/GYPGKMQjoDkVLBQEAAAAo/1/1b5ad5/1_single_J8sExA_1080hi.mpd";
                //url = "https://livesim.dashif.org/dash/vod/testpic_2s/multi_subs.mpd"; //ttml + mp4
                //url = "http://media.axprod.net/TestVectors/v6-Clear/Manifest_1080p.mpd"; //vtt + mp4
                //url = "https://livesim.dashif.org/dash/vod/testpic_2s/xml_subs.mpd"; //ttml
                //url = "https://storage.googleapis.com/shaka-demo-assets/angel-one-hls/hls.m3u8"; //HLS vtt
                //url = "https://devstreaming-cdn.apple.com/videos/streaming/examples/bipbop_adv_example_hevc/master.m3u8"; //高级HLS fMP4+VTT
                //url = "https://events-delivery.apple.com/0205eyyhwbbqexozkwmgccegwnjyrktg/m3u8/vod_index-dpyfrsVksFWjneFiptbXnAMYBtGYbXeZ.m3u8"; //高级HLS fMP4+VTT
                //url = "https://apionvod5.seezntv.com/ktmain1/cold/CP/55521/202207/media/MIAM61RPSGL150000100_DRM/MIAM61RPSGL150000100_H.m3u8?sid=0000000F50000040000A700000020000";
                //url = "https://ewcdn12.nowe.com/session/16-5-72579e3-2103014898783810281/Content/DASH_VOS3/VOD/6908/19585/d2afa5fe-e9c8-40f0-8d18-648aaaf292b6/f677841a-9d8f-2ff5-3517-674ba49ef192/manifest.mpd?token=894db5d69931835f82dd8e393974ef9f_1658146180";
                //url = "https://ols-ww100-cp.akamaized.net/manifest/master/06ee6f68-ee80-11ea-9bc5-02b68fb543c4/65794a72596d6c30496a6f7a4e6a67324e4441774d444173496e42735958526d62334a74496a6f695a47567a6133527663434973496d526c646d6c6a5a565235634755694f694a335a5749694c434a746232526c62434936496e6470626d527664334d694c434a7663315235634755694f694a6a61484a76625755694c434a7663794936496a45774d6934774c6a41694c434a68634841694f69497a4c6a416966513d3d/dash.mpd?cpatoken=exp=1658223027~acl=/manifest/master/06ee6f68-ee80-11ea-9bc5-02b68fb543c4/*~hmac=644c608aac361f688e9b24b0f345c801d0f2d335819431d1873ff7aeac46d6b2&access_token=eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJkZXZpY2VfaWQiOm51bGwsIndhdGNoX3R5cGUiOiJQUkVNSVVNIiwicHJvZ3JhbV9pZCI6ImUwMWRmYjAyLTM1YmItMTFlOS1hNDI3LTA2YTA0MTdjMWQxZSIsImFkX3RhZyI6ZmFsc2UsInBhcmVudF9wcm9ncmFtX2lkIjoiZmJmMDc2MDYtMzNmYi0xMWU5LWE0MjctMDZhMDQxN2MxZDFlIiwiY2xpZW50X2lkIjoiNGQ3MDViZTQtYTQ5ZS0xMWVhLWJiMzctMDI0MmFjMTMwMDAyIiwidmlkZW9fdHlwZSI6InZvZCIsImdyYW50X3R5cGUiOiJwbGF5X3ZpZGVvIiwidXNlcl9pZCI6ImFhNTMxZWQ2LWM2NTMtNDliYS04NGI1LWFkZDRmNGIzNGMyNyIsImN1cnJlbnRfc2Vjb25kIjowLCJyZXBvcnRfaWQiOiJOU1RHIiwic2NvcGUiOlsicHVibGljOi4qIiwibWU6LioiXSwiZXhwIjoxNjU4Mzk1ODI2LCJkZXRlY3Rpb25faWQiOm51bGwsInZpZGVvX2lkIjoiODc0Yjk0ZDItNzZiYi00YzliLTgzODQtNzJlMTA0NWVjOGMxIiwiaXNzIjoiQXNpYXBsYXktT0F1dGgtU2VydmVyIiwiaWF0IjoxNjU4MTM2NjI2LCJ0ZXJyaXRvcnkiOiJUVyJ9.1juciYIyMNzykXKu-nGLR_cYWvPMEAE9ub-ny7RzFnM";
                //url = "https://a38avoddashs3ww-a.akamaihd.net/ondemand/iad_2/8e91/f2f2/ec5a/430f-bd7a-0779f4a0189d/685cda75-609c-41c1-86bb-688f4cdb5521_corrected.mpd";
                //url = "https://dcs-vod.mp.lura.live/vod/p/session/manifest.mpd?i=i177610817-nb45239a2-e962-4137-bc70-1790359619e6";
                //url = "https://theater.kktv.com.tw/98/04000198010001_584b26392f7f7f11fc62299214a55fb7/16113081449d8d5e9960_sub_dash.mpd"; //MPD+VTT
                //url = "https://vsl.play.kakao.com/vod/rvty90n7btua6u9oebr97i8zl/dash/vhs/cenc/adaptive.mpd?e=1658297362&p=71&h=53766bdde112d59da2b2514e8ab41e81"; //需要补params
                //url = "https://a38avoddashs3ww-a.akamaihd.net/ondemand/iad_2/8e91/f2f2/ec5a/430f-bd7a-0779f4a0189d/685cda75-609c-41c1-86bb-688f4cdb5521_corrected.mpd";
                //url = "";

                if (!string.IsNullOrEmpty(option.Input))
                {
                    url = option.Input;
                }

                if (string.IsNullOrEmpty(url))
                {
                    url = AnsiConsole.Ask<string>("Input [green]URL[/]: ");
                }

                //流提取器配置
                var extractor = new StreamExtractor(parserConfig);
                extractor.LoadSourceFromUrl(url);

                //解析流信息
                var streams = await extractor.ExtractStreamsAsync();

                //直播检测
                var livingFlag = streams.Any(s => s.Playlist?.IsLive == true);
                if (livingFlag)
                {
                    Logger.WarnMarkUp($"[white on darkorange3_1]{ResString.liveFound}[/]");
                }

                //全部媒体
                var lists = streams.OrderBy(p => p.MediaType).ThenByDescending(p => p.Bandwidth);
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

                //展示交互式选择框
                //var selectedStreams = PromptUtil.SelectStreams(lists);
                var selectedStreams = new List<StreamSpec>();
                if (option.AutoSelect)
                {
                    if (basicStreams.Any())
                        selectedStreams.Add(basicStreams.First());
                    var langs = audios.DistinctBy(a => a.Language).Select(a => a.Language);
                    foreach (var lang in langs)
                    {
                        selectedStreams.Add(audios.Where(a => a.Language == lang).OrderByDescending(a => a.Bandwidth).First());
                    }
                    selectedStreams.AddRange(subs);
                }
                else if (option.SubOnly)
                {
                    selectedStreams.AddRange(subs);
                }
                else
                {
                    selectedStreams = PromptUtil.SelectStreams(lists);
                }
                //一个以上的话，需要手动重新加载playlist
                if (lists.Count() > 1)
                    await extractor.FetchPlayListAsync(selectedStreams);

                //无法识别的加密方式，自动开启二进制合并
                if (selectedStreams.Any(s => s.Playlist.MediaParts.Any(p => p.MediaSegments.Any(m => m.EncryptInfo.Method == EncryptMethod.UNKNOWN))))
                {
                    Logger.WarnMarkUp($"[darkorange3_1]{ResString.autoBinaryMerge3}[/]");
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

                //下载配置
                var downloadConfig = new DownloaderConfig()
                {
                    MyOptions = option,
                    Headers = parserConfig.Headers, //使用命令行解析得到的Headers
                };
                //开始下载
                var sdm = new SimpleDownloadManager(downloadConfig);
                var result = await sdm.StartDownloadAsync(selectedStreams);
                if (result)
                    Logger.InfoMarkUp("[white on green]Done[/]");
                else
                    Logger.ErrorMarkUp("[white on red]Faild[/]");
            }
            catch (Exception ex)
            {
                string msg = Logger.LogLevel == LogLevel.DEBUG ? ex.ToString() : ex.Message;
#if DEBUG
                msg = ex.ToString();
#endif
                Logger.Error(msg);
                await Task.Delay(3000);
            }
        }
    }
}