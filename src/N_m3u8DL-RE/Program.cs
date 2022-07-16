using N_m3u8DL_RE.Parser.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Parser.Util;
using N_m3u8DL_RE.Parser;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Serialization;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Processor;
using N_m3u8DL_RE.Downloader;
using N_m3u8DL_RE.Config;
using N_m3u8DL_RE.Util;
using System.Diagnostics;
using N_m3u8DL_RE.DownloadManager;

namespace N_m3u8DL_RE
{
    internal class Program
    {

        static async Task Main(string[] args)
        {
            string loc = "en-US";
            string currLoc = Thread.CurrentThread.CurrentUICulture.Name;
            if (currLoc == "zh-TW" || currLoc == "zh-HK" || currLoc == "zh-MO") loc = "zh-TW";
            else if (currLoc == "zh-CN" || currLoc == "zh-SG") loc = "zh-CN";
            //设置语言
            CultureInfo.DefaultThreadCurrentCulture = new CultureInfo(loc);
            Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo(loc);
            //Logger.LogLevel = LogLevel.DEBUG;

            try
            {
                var parserConfig = new ParserConfig();
                //demo1
                parserConfig.ContentProcessors.Insert(0, new DemoProcessor());
                //demo2
                parserConfig.KeyProcessors.Insert(0, new DemoProcessor2());

                var url = string.Empty;
                //url = "https://cmafref.akamaized.net/cmaf/live-ull/2006350/akambr/out.mpd"; //直播
                //url = "http://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8";
                //url = "https://vod.sdn.wavve.com/hls/S01/S01_E461382925.1/1/5000/chunklist.m3u8";
                url = "https://bitmovin-a.akamaihd.net/content/art-of-motion_drm/mpds/11331.mpd";
                //url = "http://tv-live.ynkmit.com/tv/anning.m3u8?txSecret=7528f35fb4b62bd24d55b891899db68f&txTime=632C8680"; //直播
                //url = "https://rest-as.ott.kaltura.com/api_v3/service/assetFile/action/playManifest/partnerId/147/assetId/1304099/assetType/media/assetFileId/16136929/contextType/PLAYBACK/isAltUrl/False/ks/djJ8MTQ3fMusTFH6PCZpcrfKLQwI-pPm9ex6b6r49wioe32WH2udXeM4reyWIkSDpi7HhvhxBHAHAKiHrcnkmIJQpyAt4MuDBG0ywGQ-jOeqQFcTRQ8BGJGw6g-smSBLwSbo4CCx9M9vWNJX3GkOfhoMAY4yRU-ur3okHiVq1mUJ82XBd_iVqLuzodnc9sJEtcHH0zc5CoPiTq2xor-dq3yDURnZm3isfSN3t9uLIJEW09oE-SJ84DM5GUuFUdbnIV8bdcWUsPicUg-Top1G2D3WcWXq4EvPnwvD8jrC_vsiOpLHf5akAwtdGsJ6__cXUmT7a-QlfjdvaZ5T8UhDLnttHmsxYs2E5c0lh4uOvvJou8dD8iYxUexlPI2j4QUkBRxqOEVLSNV3Y82-5TTRqgnK_uGYXHwk7EAmDws7hbLj2-DJ1heXDcye3OJYdunJgAS-9ma5zmQQNiY_HYh6wj2N1HpCTNAtWWga6R9fC0VgBTZbidW-YwMSGzIvMQfIfWKe15X7Oc_hCs-zGfW9XeRJZrutcWKK_D_HlzpQVBF2vIF3XgaI/a.mpd";
                //url = "https://dash.akamaized.net/dash264/TestCases/2c/qualcomm/1/MultiResMPEG2.mpd";
                //url = "http://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8";
                //url = "https://cmaf.lln.latam.hbomaxcdn.com/videos/GYPGKMQjoDkVLBQEAAAAo/1/1b5ad5/1_single_J8sExA_1080hi.mpd";
                //url = "https://livesim.dashif.org/dash/vod/testpic_2s/multi_subs.mpd"; //ttml + mp4
                //url = "http://media.axprod.net/TestVectors/v6-Clear/Manifest_1080p.mpd"; //vtt + mp4
                url = "https://livesim.dashif.org/dash/vod/testpic_2s/xml_subs.mpd"; //ttml

                if (args.Length > 0)
                {
                    url = args[0];
                }

                if (string.IsNullOrEmpty(url))
                {
                    url = AnsiConsole.Ask<string>("请输入 [green]URL[/]: ");
                }

                //流提取器配置
                var extractor = new StreamExtractor(parserConfig);
                extractor.LoadSourceFromUrl(url);

                //解析流信息
                var streams = await extractor.ExtractStreamsAsync();

                //全部媒体
                var lists = streams.OrderByDescending(p => p.Bandwidth);
                //基本流
                var basicStreams = lists.Where(x => x.MediaType == null);
                //可选音频轨道
                var audios = lists.Where(x => x.MediaType == MediaType.AUDIO);
                //可选字幕轨道
                var subs = lists.Where(x => x.MediaType == MediaType.SUBTITLES);

                Logger.Warn(ResString.writeJson);
                await File.WriteAllTextAsync("meta.json", GlobalUtil.ConvertToJson(lists), Encoding.UTF8);

                Logger.Info(ResString.streamsInfo, lists.Count(), basicStreams.Count(), audios.Count(), subs.Count());

                foreach (var item in lists)
                {
                    Logger.InfoMarkUp(item.ToString());
                }

                //展示交互式选择框
                var selectedStreams = PromptUtil.SelectStreams(lists);
                //一个以上的话，需要手动重新加载playlist
                if (lists.Count() > 1)
                    await extractor.FetchPlayListAsync(selectedStreams);
                Logger.Warn(ResString.writeJson);
                await File.WriteAllTextAsync("meta_selected.json", GlobalUtil.ConvertToJson(selectedStreams), Encoding.UTF8);
                Logger.Info(ResString.selectedStream);
                foreach (var item in selectedStreams)
                {
                    Logger.InfoMarkUp(item.ToString());
                }

                Console.ReadKey();

                //下载配置
                var downloadConfig = new DownloaderConfig()
                {
                    Headers = parserConfig.Headers,
                    BinaryMerge = true,
                    DelAfterDone = true,
                    CheckSegmentsCount = true
                };
                //开始下载
                var sdm = new SimpleDownloadManager(downloadConfig);
                var result = await sdm.StartDownloadAsync(selectedStreams);
                if (result)
                    Logger.InfoMarkUp("[white on green]成功[/]");
                else
                    Logger.ErrorMarkUp("[white on red]失败[/]");
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
            //Console.ReadKey();
        }
    }
}