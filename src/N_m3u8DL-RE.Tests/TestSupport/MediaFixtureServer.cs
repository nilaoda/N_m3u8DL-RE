using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace N_m3u8DL_RE.Tests.TestSupport;

// 点播和直播共用的本地 HTTP 服务；请求计数和清单回调均属于当前实例。
internal sealed class MediaFixtureServer : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<string, int> requests = new();
    private readonly List<Task> handlers = [];
    private readonly Task loop;
    public string Url { get; }
    public int RequestCount(string path) => requests.GetValueOrDefault(path);

    public MediaFixtureServer(string root, Func<string, int, string?> manifest, Func<string, Task>? beforeResponse = null,
        Func<string, int, HttpStatusCode?>? responseStatus = null,
        Func<string, int, Stream, CancellationToken, Task<bool>>? responseWriter = null)
    {
        listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
        loop = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    var client = await listener.AcceptTcpClientAsync(stop.Token);
                    handlers.Add(ServeSafely(client));
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        async Task ServeSafely(TcpClient client)
        {
            try
            {
                await Serve(client);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (IOException ex) when (ex.InnerException is SocketException)
            {
                // 取消请求时客户端可能已断开连接，与真实 HTTP 服务一样结束该连接即可。
            }
        }
        async Task Serve(TcpClient client)
        {
            using (client)
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var request = await reader.ReadLineAsync(stop.Token);
                if (request == null)
                    return;
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync(stop.Token))) { }
                var path = new Uri(Url + request.Split(' ')[1].TrimStart('/')).AbsolutePath.TrimStart('/');
                var count = requests.AddOrUpdate(path, 1, (_, old) => old + 1);
                if (beforeResponse != null)
                    await beforeResponse(path);
                if (responseWriter != null && await responseWriter(path, count, stream, stop.Token))
                    return;
                if (responseStatus?.Invoke(path, count) is { } status)
                {
                    var errorHeaders = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int)status} {status}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(errorHeaders, stop.Token);
                    return;
                }
                var text = manifest(path, count - 1);
                var bytes = text != null ? Encoding.UTF8.GetBytes(text) : await File.ReadAllBytesAsync(Path.Combine(root, path), stop.Token);
                var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(headers, stop.Token);
                await stream.WriteAsync(bytes, stop.Token);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        await loop;
        await Task.WhenAll(handlers);
        stop.Dispose();
    }
}
