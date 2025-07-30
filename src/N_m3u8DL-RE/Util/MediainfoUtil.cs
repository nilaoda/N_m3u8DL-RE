﻿using System.Diagnostics;
using System.Text.RegularExpressions;

using N_m3u8DL_RE.Entity;

namespace N_m3u8DL_RE.Util
{
    internal static partial class MediainfoUtil
    {
        [GeneratedRegex("  Stream #.*")]
        private static partial Regex TextRegex();
        [GeneratedRegex(@"#0:\d(\[0x\w+?\])")]
        private static partial Regex IdRegex();
        [GeneratedRegex(": (\\w+): (.*)")]
        private static partial Regex TypeRegex();
        [GeneratedRegex("(.*?)(,|$)")]
        private static partial Regex BaseInfoRegex();
        [GeneratedRegex(@" \/ 0x\w+")]
        private static partial Regex ReplaceRegex();
        [GeneratedRegex(@"\d{2,}x\d+")]
        private static partial Regex ResRegex();
        [GeneratedRegex(@"\d+ kb\/s")]
        private static partial Regex BitrateRegex();
        [GeneratedRegex(@"(\d+(\.\d+)?) fps")]
        private static partial Regex FpsRegex();
        [GeneratedRegex(@"DOVI configuration record.*profile: (\d).*compatibility id: (\d)")]
        private static partial Regex DoViRegex();
        [GeneratedRegex(@"Duration.*?start: (\d+\.?\d{0,3})")]
        private static partial Regex StartRegex();

        public static async Task<List<Mediainfo>> ReadInfoAsync(string binary, string file)
        {
            List<Mediainfo> result = [];

            if (string.IsNullOrEmpty(file) || !File.Exists(file))
            {
                return result;
            }

            string cmd = "-hide_banner -i \"" + file + "\"";
            Process p = Process.Start(new ProcessStartInfo()
            {
                FileName = binary,
                Arguments = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            string output = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();

            foreach (Match stream in TextRegex().Matches(output))
            {
                Mediainfo info = new()
                {
                    Text = TypeRegex().Match(stream.Value).Groups[2].Value.TrimEnd(),
                    Id = IdRegex().Match(stream.Value).Groups[1].Value,
                    Type = TypeRegex().Match(stream.Value).Groups[1].Value,
                };

                info.Resolution = ResRegex().Match(info.Text).Value;
                info.Bitrate = BitrateRegex().Match(info.Text).Value;
                info.Fps = FpsRegex().Match(info.Text).Value;
                info.BaseInfo = BaseInfoRegex().Match(info.Text).Groups[1].Value;
                info.BaseInfo = ReplaceRegex().Replace(info.BaseInfo, "");
                info.HDR = info.Text.Contains("/bt2020/");

                if (info.BaseInfo.Contains("dvhe")
                    || info.BaseInfo.Contains("dvh1")
                    || info.BaseInfo.Contains("DOVI")
                    || info.Type.Contains("dvvideo")
                    || (DoViRegex().IsMatch(output) && info.Type == "Video")
                   )
                {
                    info.DolbyVison = true;
                }

                if (StartRegex().IsMatch(output))
                {
                    string f = StartRegex().Match(output).Groups[1].Value;
                    if (double.TryParse(f, out double d))
                    {
                        info.StartTime = TimeSpan.FromSeconds(d);
                    }
                }

                result.Add(info);
            }

            if (result.Count == 0)
            {
                result.Add(new Mediainfo
                {
                    Type = "Unknown"
                });
            }

            return result;
        }
    }
}