using Mp4SubtitleParser;
using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Common.Resource;
using System.Diagnostics;
using System.Text.RegularExpressions;
using N_m3u8DL_RE.Enum;

namespace N_m3u8DL_RE.Util;

internal static class MP4DecryptUtil
{
    private static readonly string ZeroKid = "00000000000000000000000000000000";
    public static async Task<bool> DecryptAsync(DecryptEngine decryptEngine, string bin, string[]? keys, string source, string dest, string? kid, string init = "", bool isMultiDRM=false)
    {
        if (keys == null || keys.Length == 0) return false;

        var keyPairs = keys.ToList();
        string? keyPair = null;
        string? trackId = null;

        if (isMultiDRM)
        {
            trackId = "1";
        }

        if (!string.IsNullOrEmpty(kid))
        {
            var test = keyPairs.Where(k => k.StartsWith(kid)).ToList();
            if (test.Any()) keyPair = test.First();
        }

        // Apple
        if (kid == ZeroKid)
        {
            keyPair = keyPairs.First();
            trackId = "1";
        }

        // user only input key, append kid
        if (keyPair == null && keyPairs.Count == 1 && !keyPairs.First().Contains(':'))
        {
            keyPairs = keyPairs.Select(x => $"{kid}:{x}").ToList();
            keyPair = keyPairs.First();
        }
            
        if (keyPair == null) return false;

        // shakaPackager/ffmpeg 无法单独解密init文件
        if (source.EndsWith("_init.mp4") && decryptEngine != DecryptEngine.MP4DECRYPT) return false;

        string cmd;

        var tmpFile = "";
        if (decryptEngine == DecryptEngine.SHAKA_PACKAGE)
        {
            var enc = source;
            // shakaPackager 手动构造文件
            if (init != "")
            {
                tmpFile = Path.ChangeExtension(source, ".itmp");
                MergeUtil.CombineMultipleFilesIntoSingleFile([init, source], tmpFile);
                enc = tmpFile;
            }

            cmd = $"--quiet --enable_raw_key_decryption input=\"{enc}\",stream=0,output=\"{dest}\" " +
                  $"--keys {(trackId != null ? $"label={trackId}:" : "")}key_id={(trackId != null ? ZeroKid : kid)}:key={keyPair.Split(':')[1]}";
        }
        else if (decryptEngine == DecryptEngine.MP4DECRYPT)
        {
            if (trackId == null)
            {
                cmd = string.Join(" ", keyPairs.Select(k => $"--key {k}"));
            }
            else
            {
                cmd = string.Join(" ", keyPairs.Select(k => $"--key {trackId}:{k.Split(':')[1]}"));
            }
            if (init != "")
            {
                cmd += $" --fragments-info \"{init}\" ";
            }
            cmd += $" \"{source}\" \"{dest}\"";
        }
        else
        {
            var enc = source;
            // ffmpeg实时解密 手动构造文件
            if (init != "")
            {
                tmpFile = Path.ChangeExtension(source, ".itmp");
                MergeUtil.CombineMultipleFilesIntoSingleFile([init, source], tmpFile);
                enc = tmpFile;
            }
            
            cmd = $"-loglevel error -nostdin -decryption_key {keyPair.Split(':')[1]} -i \"{enc}\" -c copy \"{dest}\"";
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
            // RedirectStandardOutput = true,
            // RedirectStandardError = true,
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
            while (await reader.ReadLineAsync() is { } line)
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

    public static ParsedMP4Info GetMP4Info(byte[] data)
    {
        var info = MP4InitUtil.ReadInit(data);
        if (info.Scheme != null) Logger.WarnMarkUp($"[grey]Type: {info.Scheme}[/]");
        if (info.PSSH != null) Logger.WarnMarkUp($"[grey]PSSH(WV): {info.PSSH}[/]");
        if (info.KID != null) Logger.WarnMarkUp($"[grey]KID: {info.KID}[/]");
        return info;
    }

    public static ParsedMP4Info GetMP4Info(string output)
    {
        using var fs = File.OpenRead(output);
        var header = new byte[1 * 1024 * 1024]; // 1MB
        _ = fs.Read(header);
        return GetMP4Info(header);
    }

    public static string? ReadInitShaka(string output, string bin)
    {
        Regex shakaKeyIdRegex = new("Key for key_id=([0-9a-f]+) was not found");

        // TODO: handle the case that shaka packager actually decrypted (key ID == ZeroKid)
        //       - stop process
        //       - remove {output}.tmp.webm
        var cmd = $"--quiet --enable_raw_key_decryption input=\"{output}\",stream=0,output=\"{output}.tmp.webm\" " +
                  $"--keys key_id={ZeroKid}:key={ZeroKid}";

        using var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            FileName = bin,
            Arguments = cmd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        p.Start();
        var errorOutput = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return shakaKeyIdRegex.Match(errorOutput).Groups[1].Value;
    }
}