﻿using N_m3u8DL_RE.Common.Log;
using Spectre.Console;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace N_m3u8DL_RE.Util;

internal static class PipeUtil
{
    public static Stream CreatePipe(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut);
        }

        var path = Path.Combine(Path.GetTempPath(), pipeName);
        using var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            FileName = "mkfifo",
            Arguments = path,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        p.Start();
        p.WaitForExit();
        Thread.Sleep(200);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
    }

    public static async Task<bool> StartPipeMuxAsync(string binary, string[] pipeNames, string outputPath)
    {
        return await Task.Run(async () =>
        {
            await Task.Delay(1000);
            return StartPipeMux(binary, pipeNames, outputPath);
        });
    }

    public static bool StartPipeMux(string binary, string[] pipeNames, string outputPath)
    {
        Process p = CreatePipeMux(binary, pipeNames, outputPath);
        p.Start();
        p.WaitForExit();
        bool exitNormally = p.ExitCode == 0;
        p.Dispose();
        if (!exitNormally)
        {
            throw new Exception("FFmpeg pipe mux exit with non-zero exit code");
        }
        return exitNormally;
    }

    public static Process CreatePipeMux(string binary, string[] pipeNames, string outputPath)
    {
        string dateString = DateTime.Now.ToString("o");
        StringBuilder command = new StringBuilder("-y -fflags +genpts -loglevel quiet ");

        string customDest = OtherUtil.GetEnvironmentVariable("RE_LIVE_PIPE_OPTIONS");
        string pipeDir = OtherUtil.GetEnvironmentVariable("RE_LIVE_PIPE_TMP_DIR", Path.GetTempPath());

        if (!string.IsNullOrEmpty(customDest))
        {
            command.Append(" -re ");
        }

        foreach (var item in pipeNames)
        {
            if (OperatingSystem.IsWindows())
                command.Append($" -i \"\\\\.\\pipe\\{item}\" ");
            else
                // command.Append($" -i \"unix://{Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{item}")}\" ");
                command.Append($" -i \"{Path.Combine(pipeDir, item)}\" ");
        }

        for (int i = 0; i < pipeNames.Length; i++)
        {
            command.Append($" -map {i} ");
        }

        command.Append(" -strict unofficial -c copy ");
        command.Append($" -metadata date=\"{dateString}\" ");
        command.Append($" -ignore_unknown -copy_unknown ");


        if (!string.IsNullOrEmpty(customDest))
        {
            if (customDest.Trim().StartsWith('-'))
                command.Append(customDest);
            else
                command.Append($" -f mpegts -shortest \"{customDest}\"");
            Logger.WarnMarkUp($"[deepskyblue1]{command.ToString().EscapeMarkup()}[/]");
        }
        else
        {
            command.Append($" -f mpegts -shortest \"{outputPath}\"");
        }

        var p = new Process();
        p.StartInfo = new ProcessStartInfo()
        {
            WorkingDirectory = Environment.CurrentDirectory,
            FileName = binary,
            Arguments = command.ToString(),
            CreateNoWindow = true,
            UseShellExecute = false
        };
        // p.StartInfo.Environment.Add("FFREPORT", "file=ffreport.log:level=42");
        return p;
    }
}