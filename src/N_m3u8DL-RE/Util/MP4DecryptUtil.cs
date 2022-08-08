using System.Diagnostics;

namespace N_m3u8DL_RE.Util
{
    internal class MP4DecryptUtil
    {
        public static async Task<bool> DecryptAsync(bool shakaPackager, string bin, string[]? keys, string source, string dest, string? kid, string init = "")
        {
            if (keys == null || keys.Length == 0) return false;

            var keyPair = keys.First();
            if (!string.IsNullOrEmpty(kid))
            {
                var test = keys.Where(k => k.StartsWith(kid));
                if (test.Any()) keyPair = test.First();
            }

            if (keyPair == null) return false;

            //shakaPackager 无法单独解密init文件
            if (source.EndsWith("_init.mp4") && shakaPackager) return true;

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

                cmd = $"--enable_raw_key_decryption input=\"{enc}\",stream=0,output=\"{dest}\" " +
                    $"--keys key_id={keyPair.Split(':')[0]}:key={keyPair.Split(':')[1]}";
            }
            else
            {
                cmd = string.Join(" ", keys.Select(k => $"--key {k}"));
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
            await Process.Start(new ProcessStartInfo()
            {
                FileName = name,
                Arguments = arg,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            })!.WaitForExitAsync();
        }
    }
}
