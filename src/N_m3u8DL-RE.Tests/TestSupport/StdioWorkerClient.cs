using System.Diagnostics;
using System.Text.Json;
using N_m3u8DL_RE.CommandLine;

namespace N_m3u8DL_RE.Tests.TestSupport;

internal sealed class StdioWorkerClient : IAsyncDisposable
{
    private readonly Process process;
    private readonly Task<string> error;
    public List<JsonElement> Messages { get; } = [];
    public StdioWorkerClient()
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        start.ArgumentList.Add(typeof(CommandInvoker).Assembly.Location);
        start.ArgumentList.Add("worker");
        start.ArgumentList.Add("--stdio");
        process = Process.Start(start)!;
        error = process.StandardError.ReadToEndAsync();
    }
    internal Task<string> Diagnostics => error;
    public Task Send(object message) => SendRaw(JsonSerializer.Serialize(message));
    public async Task SendRaw(string line)
    {
        await process.StandardInput.WriteLineAsync(line);
        await process.StandardInput.FlushAsync();
    }
    public async Task SendBytes(byte[] bytes)
    {
        await process.StandardInput.BaseStream.WriteAsync(bytes);
        await process.StandardInput.BaseStream.FlushAsync();
    }
    public void CloseInput() => process.StandardInput.Close();
    public async Task<JsonElement> Read(string type)
    {
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(line != null, process.HasExited ? await error : "Worker closed stdout unexpectedly.");
            using var document = JsonDocument.Parse(line);
            var message = document.RootElement.Clone();
            Assert.Equal(Messages.Count + 1, message.GetProperty("sequence").GetInt64());
            Messages.Add(message);
            if (message.GetProperty("type").GetString() == type)
                return message;
        }
    }
    public async Task Exit(int code)
    {
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(code, process.ExitCode);
        Assert.Null(await process.StandardOutput.ReadLineAsync());
    }
    public async ValueTask DisposeAsync()
    {
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
        await error;
        process.Dispose();
    }
}
