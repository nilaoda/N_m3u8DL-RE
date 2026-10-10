using System.Diagnostics;
using System.Text;

namespace N_m3u8DL_RE.Util;

internal static class ProcessUtil
{
    internal sealed record Result(int ExitCode, string Output, string Error);

    internal static async Task<Result> RunAsync(string binary, IEnumerable<string> arguments,
        CancellationToken token, bool printError = false, string? workingDirectory = null,
        bool loopbackInput = false, Action<string>? errorLine = null, int captureLimit = 16384,
        Action<string>? outputLine = null)
    {
        var startInfo = new ProcessStartInfo(binary);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        return await RunAsync(startInfo, token, printError, workingDirectory, loopbackInput, errorLine, captureLimit, outputLine).ConfigureAwait(false);
    }

    internal static async Task<Result> RunCommandAsync(string binary, string command, string workingDirectory,
        Action<string> errorLine, bool loopbackInput = false, Action<string>? outputLine = null)
    {
        return await RunAsync(new ProcessStartInfo(binary, command), default, false, workingDirectory,
            loopbackInput, errorLine, 16384, outputLine).ConfigureAwait(false);
    }

    private static async Task<Result> RunAsync(ProcessStartInfo startInfo, CancellationToken token,
        bool printError, string? workingDirectory, bool loopbackInput, Action<string>? errorLine, int captureLimit,
        Action<string>? outputLine)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        if (workingDirectory != null)
            startInfo.WorkingDirectory = workingDirectory;
        if (loopbackInput)
        {
            // concat 内部打开 HTTP 时不会继承 http_proxy 参数，只对当前子进程
            // 增补回环地址的代理豁免，不能修改整个程序的代理环境。
            startInfo.Environment.TryGetValue("no_proxy", out var noProxy);
            startInfo.Environment["no_proxy"] = string.IsNullOrEmpty(noProxy) ? "127.0.0.1" : $"{noProxy},127.0.0.1";
        }
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var output = outputLine == null ? ReadAsync(process.StandardOutput, false, captureLimit) : ReadLinesAsync(process.StandardOutput, outputLine, captureLimit);
        var error = errorLine == null ? ReadAsync(process.StandardError, printError, captureLimit) : ReadLinesAsync(process.StandardError, errorLine, captureLimit);
        try
        {
            await process.WaitForExitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> write, int captureLimit)
    {
        // 保留工具输出尾部，便于判断最后的失败原因；长时间处理的进度消息不能无限占用内存。
        // 下载和工具命令日志同时逐行输出；mkvmerge 的 stdout 也由这里处理。
        var text = new StringBuilder();
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            text.AppendLine(line[Math.Max(0, line.Length - captureLimit)..]);
            if (text.Length > captureLimit)
                text.Remove(0, text.Length - captureLimit);
            if (!string.IsNullOrEmpty(line))
                write(line);
        }
        return text.ToString();
    }

    private static async Task<string> ReadAsync(StreamReader reader, bool print, int captureLimit)
    {
        // 诊断只需版本横幅，仍持续排空管道，避免输出过多时子进程阻塞。
        var text = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            if (print)
                await Console.Error.WriteAsync(buffer.AsMemory(0, count)).ConfigureAwait(false);
            var remaining = captureLimit - text.Length;
            if (remaining > 0)
                text.Append(buffer, 0, Math.Min(count, remaining));
        }
        return text.ToString();
    }
}
