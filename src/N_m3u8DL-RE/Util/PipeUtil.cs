﻿using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Text;

using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Config;

using Spectre.Console;

namespace N_m3u8DL_RE.Util
{
    internal static class PipeUtil
    {
        public static Stream CreatePipe(string pipeName)
        {
            if (OperatingSystem.IsWindows())
            {
                return new NamedPipeServerStream(pipeName, PipeDirection.InOut);
            }

            string path = Path.Combine(Path.GetTempPath(), pipeName);
            using Process p = new();
            p.StartInfo = new ProcessStartInfo()
            {
                FileName = "mkfifo",
                Arguments = path,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            _ = p.Start();
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
            string dateString = DateTime.Now.ToString("o");
            StringBuilder command = new("-y -fflags +genpts -loglevel quiet ");

            string customDest = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeOptions);
            string pipeDir = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeTmpDir, Path.GetTempPath());

            if (!string.IsNullOrEmpty(customDest))
            {
                _ = command.Append(" -re ");
            }

            foreach (string item in pipeNames)
            {
                if (OperatingSystem.IsWindows())
                {
                    _ = command.Append(CultureInfo.InvariantCulture, $" -i \"\\\\.\\pipe\\{item}\" ");
                }
                else
                {
                    // command.Append($" -i \"unix://{Path.Combine(Path.GetTempPath(), $"CoreFxPipe_{item}")}\" ");
                    _ = command.Append(CultureInfo.InvariantCulture, $" -i \"{Path.Combine(pipeDir, item)}\" ");
                }
            }

            for (int i = 0; i < pipeNames.Length; i++)
            {
                _ = command.Append(CultureInfo.InvariantCulture, $" -map {i} ");
            }

            _ = command.Append(" -strict unofficial -c copy ");
            _ = command.Append(CultureInfo.InvariantCulture, $" -metadata date=\"{dateString}\" ");
            _ = command.Append($" -ignore_unknown -copy_unknown ");


            if (!string.IsNullOrEmpty(customDest))
            {
                _ = customDest.Trim().StartsWith('-') ? command.Append(customDest) : command.Append(CultureInfo.InvariantCulture, $" -f mpegts -shortest \"{customDest}\"");

                Logger.WarnMarkUp($"[deepskyblue1]{command.ToString().EscapeMarkup()}[/]");
            }
            else
            {
                _ = command.Append(CultureInfo.InvariantCulture, $" -f mpegts -shortest \"{outputPath}\"");
            }

            using Process p = new();
            p.StartInfo = new ProcessStartInfo()
            {
                WorkingDirectory = Environment.CurrentDirectory,
                FileName = binary,
                Arguments = command.ToString(),
                CreateNoWindow = true,
                UseShellExecute = false
            };
            // p.StartInfo.Environment.Add("FFREPORT", "file=ffreport.log:level=42");
            _ = p.Start();
            p.WaitForExit();

            return p.ExitCode == 0;
        }
    }
}