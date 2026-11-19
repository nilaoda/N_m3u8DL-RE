using N_m3u8DL_RE.Common.Log;
using N_m3u8DL_RE.Runtime;
using Spectre.Console;
using System.IO.Pipes;
using System.Text;
using N_m3u8DL_RE.Config;

namespace N_m3u8DL_RE.Util;

internal static class PipeUtil
{
    public static Stream CreatePipe(string pipeName)
    {
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        }

        var directory = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeTmpDir, Path.GetTempPath());
        Directory.CreateDirectory(directory);
        var path = Path.GetFullPath(Path.Combine(directory, pipeName));
        // Unix Worker 使用 .NET 命名管道对应的域套接字，异步写入和连接等待可被取消。
        if (TaskExecutionContext.Current?.IsWorker == true)
            return new NamedPipeServerStream(path, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var result = ProcessUtil.RunAsync("mkfifo", [path], default).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new IOException(result.Error);
        // FFmpeg 使用相同目录；流关闭后删除 FIFO，避免多次录制遗留管道文件。
        return new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
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
        var dateString = DateTime.Now.ToString("o");
        var isWorker = TaskExecutionContext.Current?.IsWorker == true;
        var logLevel = isWorker ? "warning" : "quiet";
        var command = new StringBuilder($"-y -fflags +genpts -loglevel {logLevel} ");

        var customDest = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeOptions);
        var pipeDir = OtherUtil.GetEnvironmentVariable(EnvConfigKey.ReLivePipeTmpDir, Path.GetTempPath());

        if (!string.IsNullOrEmpty(customDest))
        {
            command.Append(" -re ");
        }

        foreach (var item in pipeNames)
        {
            if (OperatingSystem.IsWindows())
                command.Append($" -i \"\\\\.\\pipe\\{item}\" ");
            else
            {
                var path = Path.GetFullPath(Path.Combine(pipeDir, item));
                var input = isWorker ? "unix://" + path : path;
                command.Append($" -i \"{input}\" ");
            }
        }

        for (var i = 0; i < pipeNames.Length; i++)
        {
            command.Append($" -map {i} ");
        }

        command.Append($" {BinaryToolUtil.FFmpegStrictOptions} -c copy ");
        command.Append($" -metadata date=\"{dateString}\" ");
        command.Append($" -ignore_unknown -copy_unknown ");


        if (!string.IsNullOrEmpty(customDest))
        {
            if (customDest.Trim().StartsWith('-'))
                command.Append(customDest);
            else
                command.Append($" -f mpegts -shortest \"{customDest}\"");
            Logger.WarnMarkUp($"[deepskyblue3]{command.ToString().EscapeMarkup()}[/]");
        }
        else
        {
            command.Append($" -f mpegts -shortest \"{outputPath}\"");
        }

        var result = ProcessUtil.RunCommandAsync(binary, command.ToString(), Environment.CurrentDirectory,
            line => Logger.Warn(line)).GetAwaiter().GetResult();
        return result.ExitCode == 0;
    }
}