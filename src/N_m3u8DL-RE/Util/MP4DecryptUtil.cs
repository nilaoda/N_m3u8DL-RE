using N_m3u8DL_RE.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace N_m3u8DL_RE.Util
{
    internal class MP4DecryptUtil
    {
        public static async Task<bool> DecryptAsync(string bin, string[]? keys, string source, string dest, string init = "")
        {
            if (keys == null || keys.Length == 0) return false;

            var cmd = string.Join(" ", keys.Select(k => $"--key {k}"));
            if (init != "")
            {
                cmd += $" --fragments-info \"{init}\" ";
            }
            cmd += $" \"{source}\" \"{dest}\"";

            await Process.Start(new ProcessStartInfo()
            {
                FileName = bin,
                Arguments = cmd,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!.WaitForExitAsync();

            if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            {
                return true;
            }

            return false;
        }
    }
}
