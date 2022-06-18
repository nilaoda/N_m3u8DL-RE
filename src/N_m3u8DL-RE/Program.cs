using N_m3u8DL_RE.Common.Config;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Enum;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Parser;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Serialization;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Common.Log;
using System.Globalization;
using System.Text;
using N_m3u8DL_RE.Parser.Util;

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

            try
            {
                //Logger.LogLevel = LogLevel.DEBUG;
                var config = new ParserConfig();
                var url = string.Empty;
                //url = "http://playertest.longtailvideo.com/adaptive/oceans_aes/oceans_aes.m3u8";
                url = "https://devstreaming-cdn.apple.com/videos/streaming/examples/bipbop_16x9/bipbop_16x9_variant.m3u8";

                if (string.IsNullOrEmpty(url))
                {
                    url = AnsiConsole.Ask<string>("Input [green]URL[/]: ");
                }

                //流提取器配置
                var extractor = new StreamExtractor(config);
                extractor.LoadSourceFromUrl(url);

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

                if (streams.Count > 1)
                {

                    foreach (var item in lists) Logger.InfoMarkUp(item.ToString());

                    var selectedStreams = PromptUtil.SelectStreams(lists);

                    Logger.Info(ResString.selectedStream);
                    await File.WriteAllTextAsync("meta_selected.json", GlobalUtil.ConvertToJson(selectedStreams), Encoding.UTF8);
                    foreach (var item in selectedStreams)
                    {
                        Logger.InfoMarkUp(item.ToString());
                    }
                }
                else if (streams.Count == 1)
                {
                    var playlist = streams.First().Playlist;
                    if (playlist.IsLive)
                    {
                        Logger.Warn(ResString.liveFound);
                    }
                    //Print(playlist);
                }
                else
                {
                    throw new Exception("解析失败");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex.ToString());
            }
            //Console.ReadKey();
        }

        static void Print(object o)
        {
            Console.WriteLine(GlobalUtil.ConvertToJson(o));
        }
    }
}