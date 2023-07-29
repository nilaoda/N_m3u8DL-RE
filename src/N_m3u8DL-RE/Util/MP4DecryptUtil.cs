using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using N_m3u8DL_RE.Config;
using System.Diagnostics;

namespace N_m3u8DL_RE.Util
{
    internal class MP4DecryptUtil
    {
        private static string ZeroKid = "00000000000000000000000000000000";
        public static async Task<bool> DecryptAsync(bool shakaPackager, string bin, string[]? keys, string source, string dest, string? kid, string init = "")
        {
            if (keys == null || keys.Length == 0) return false;

            string? keyPair = null;
            string? trackId = null;
            if (!string.IsNullOrEmpty(kid))
            {
                var test = keys.Where(k => k.StartsWith(kid));
                if (test.Any()) keyPair = test.First();
            }

            //Apple
            if (kid == ZeroKid)
            {
                keyPair = keys.First();
                trackId = "1";
            }

            if (keyPair == null) return false;

            //shakaPackager 无法单独解密init文件
            if (source.EndsWith("_init.mp4") && shakaPackager) return false;

            var cmd = "";

            var tmpFile = "";
            if (shakaPackager)
            {
                var enc = source;
                //shakaPackager 手动构造文件
                if (init != "")
                {
                    tmpFile = Path.ChangeExtension(source, ".itmp");
                    MergeUtil.CombineMultipleFilesIntoSingleFile(new string[] { init, source }, tmpFile);
                    enc = tmpFile;
                }

                cmd = $"--quiet --enable_raw_key_decryption input=\"{enc}\",stream=0,output=\"{dest}\" " +
                    $"--keys {(trackId != null ? $"label={trackId}:" : "")}key_id={(trackId != null ? ZeroKid : kid)}:key={keyPair.Split(':')[1]}";
            }
            else
            {
                if (trackId == null)
                {
                    cmd = string.Join(" ", keys.Select(k => $"--key {k}"));
                }
                else
                {
                    cmd = string.Join(" ", keys.Select(k => $"--key {trackId}:{k.Split(':')[1]}"));
                }
                if (init != "")
                {
                    cmd += $" --fragments-info \"{init}\" ";
                }
                cmd += $" \"{source}\" \"{dest}\"";
            }

            await RunCommandAsync(bin, cmd);

            if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            {
                if (tmpFile != "" && File.Exists(tmpFile)) File.Delete(tmpFile);
                return true;
            }

            return false;
        }

        private static async Task RunCommandAsync(string name, string arg)
        {
            Logger.DebugMarkUp($"FileName: {name}");
            Logger.DebugMarkUp($"Arguments: {arg}");
            await Process.Start(new ProcessStartInfo()
            {
                FileName = name,
                Arguments = arg,
                //RedirectStandardOutput = true,
                //RedirectStandardError = true,
                CreateNoWindow = true,
                UseShellExecute = false
            })!.WaitForExitAsync();
        }

        /// <summary>
        /// 从文本文件中查询KID的KEY
        /// </summary>
        /// <param name="file">文本文件</param>
        /// <param name="kid">目标KID</param>
        /// <returns></returns>
        public static async Task<string?> SearchKeyFromFileAsync(string? file, string? kid)
        {
            try
            {
                if (string.IsNullOrEmpty(file) || !File.Exists(file) || string.IsNullOrEmpty(kid)) 
                    return null;

                Logger.InfoMarkUp(ResString.searchKey);
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(stream);
                var line = "";
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    if (line.Trim().StartsWith(kid))
                    {
                        Logger.InfoMarkUp($"[green]OK[/] [grey]{line.Trim()}[/]");
                        return line.Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.ErrorMarkUp(ex.Message);
            }
            return null;
        }

        public static string? ReadInit(byte[] data)
        {
            var info = MP4InitUtil.ReadInit(data);
            if (info.Scheme != null) Logger.WarnMarkUp($"[grey]Type: {info.Scheme}[/]");
            if (info.PSSH != null) Logger.WarnMarkUp($"[grey]PSSH(WV): {info.PSSH}[/]");
            if (info.KID != null) Logger.WarnMarkUp($"[grey]KID: {info.KID}[/]");
            return info.KID;
        }

        public static string? ReadInit(string output)
        {
            using (var fs = File.OpenRead(output))
            {
                var header = new byte[1 * 1024 * 1024]; //1MB
                fs.Read(header);
                return ReadInit(header);
            }
        }
    }
}
