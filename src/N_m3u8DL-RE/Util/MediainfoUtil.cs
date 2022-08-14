using N_m3u8DL_RE.Entity;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace N_m3u8DL_RE.Util
{
    internal partial class MediainfoUtil
    {
        [RegexGenerator(" Stream #.*")]
        private static partial Regex TextRegex();
        [RegexGenerator("#0:\\d(\\[0x\\w+?\\])")]
        private static partial Regex IdRegex();
        [RegexGenerator(": (\\w+): (.*)")]
        private static partial Regex TypeRegex();
        [RegexGenerator("(.*?)(,|$)")]
        private static partial Regex BaseInfoRegex();
        [RegexGenerator(" \\/ 0x\\w+")]
        private static partial Regex ReplaceRegex();
        [RegexGenerator("\\d{2,}x\\d+")]
        private static partial Regex ResRegex();
        [RegexGenerator("\\d+ kb\\/s")]
        private static partial Regex BitrateRegex();
        [RegexGenerator("\\d+ fps")]
        private static partial Regex FpsRegex();

        public static async Task<List<Mediainfo>> ReadInfoAsync(string binary, string file)
        {
            var result = new List<Mediainfo>();

            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return result;

            string cmd = "-hide_banner -i \"" + file + "\"";
            var p = Process.Start(new ProcessStartInfo()
            {
                FileName = binary,
                Arguments = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!;
            var output = p.StandardError.ReadToEnd();
            await p.WaitForExitAsync();

            foreach (Match stream in TextRegex().Matches(output))
            {
                var info = new Mediainfo()
                {
                    Text = TypeRegex().Match(stream.Value).Groups[2].Value,
                    Id = IdRegex().Match(stream.Value).Groups[1].Value,
                    Type = TypeRegex().Match(stream.Value).Groups[1].Value,
                };

                info.Resolution = ResRegex().Match(info.Text).Value;
                info.Bitrate = BitrateRegex().Match(info.Text).Value;
                info.Fps = FpsRegex().Match(info.Text).Value;
                info.BaseInfo = BaseInfoRegex().Match(info.Text).Groups[1].Value;
                info.BaseInfo = ReplaceRegex().Replace(info.BaseInfo, "");

                if (info.BaseInfo.Contains("dvhe")
                    || info.BaseInfo.Contains("dvh1")
                    || info.BaseInfo.Contains("DOVI")
                    || info.Type.Contains("dvvideo")
                    )
                    info.DolbyVison = true;

                result.Add(info);
            }

            if (result.Count == 0)
            {
                result.Add(new Mediainfo()
                {
                    Type = "Unknown"
                });
            }

            return result;
        }
    }
}
