using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Util;

// 对外提供可 seek 的连续字节输入，避免 concat 协议同时打开所有分片。
// 不改写媒体内容或时间戳，也不生成整份中间文件。
internal sealed class ConcatInputServer : IDisposable
{
    private const int HeaderLimit = 16 * 1024;
    private readonly string[] _files;
    private readonly long[] _ends;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task[] _workers;
    private readonly string _path = $"/{Guid.NewGuid():N}";
    private Exception? _error;
    private int _disposed;

    internal string Url { get; }
    internal long Length { get; }
    internal Exception? Error => Volatile.Read(ref _error);

    internal ConcatInputServer(string[] files)
    {
        _files = files.Select(Path.GetFullPath).ToArray();
        _ends = new long[files.Length];
        long length = 0;
        for (var i = 0; i < _files.Length; i++)
        {
            length = checked(length + new FileInfo(_files[i]).Length);
            _ends[i] = length;
        }
        Length = length;
        // 明确绑定回环地址和临时端口。
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Url = $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{_path}";
        // FFmpeg seek 时可能尚未关闭上一条响应；两条固定工作循环既允许切换，
        // 又把连接和源文件句柄数量限制为常数，不能为每个请求无限创建任务。
        _workers = [ServeAsync(), ServeAsync()];
    }

    private async Task ServeAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    await RespondAsync(client.GetStream()).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or UnauthorizedAccessException)
                {
                    // seek 和进程退出会主动断开响应；源文件读取错误由 CopyRangeAsync 单独记录。
                }
            }
        }
        catch (Exception ex) when (_stop.IsCancellationRequested &&
                                   ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Interlocked.CompareExchange(ref _error, ex, null);
            _stop.Cancel();
            _listener.Stop();
        }
    }

    private async Task<string?> ReadHeadersAsync(NetworkStream stream)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var buffer = new byte[HeaderLimit];
        var count = 0;
        while (count < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count), timeout.Token).ConfigureAwait(false);
            if (read == 0)
                return null;
            var previous = count;
            count += read;
            for (var i = Math.Max(0, previous - 3); i + 3 < count; i++)
            {
                if (buffer[i] == '\r' && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
                    return Encoding.ASCII.GetString(buffer, 0, i);
            }
        }
        return null;
    }

    private async Task RespondAsync(NetworkStream stream)
    {
        var headers = await ReadHeadersAsync(stream).ConfigureAwait(false);
        if (headers == null)
        {
            await WriteHeadersAsync(stream, "400 Bad Request", 0).ConfigureAwait(false);
            return;
        }
        var lines = headers.Split("\r\n");
        var request = lines[0].Split(' ');
        if (request.Length != 3 || request[1] != _path)
        {
            await WriteHeadersAsync(stream, "404 Not Found", 0).ConfigureAwait(false);
            return;
        }
        if (request[0] is not ("GET" or "HEAD"))
        {
            await WriteHeadersAsync(stream, "405 Method Not Allowed", 0).ConfigureAwait(false);
            return;
        }
        if (Error != null)
        {
            await WriteHeadersAsync(stream, "500 Internal Server Error", 0).ConfigureAwait(false);
            return;
        }
        long start = 0;
        var end = Length - 1;
        var ranges = lines.Skip(1).Where(line => line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase)).ToList();
        // HEAD 按整个虚拟文件返回长度；GET 支持 FFmpeg 使用的单一字节范围。
        var partial = request[0] == "GET" && ranges.Count > 0;
        if (partial && (ranges.Count != 1 || !TryGetRange(ranges[0][6..].Trim(), out start, out end)))
        {
            await WriteHeadersAsync(stream, "416 Range Not Satisfiable", 0,
                FormattableString.Invariant($"bytes */{Length}")).ConfigureAwait(false);
            return;
        }
        await WriteHeadersAsync(stream, partial ? "206 Partial Content" : "200 OK", end - start + 1,
            partial ? FormattableString.Invariant($"bytes {start}-{end}/{Length}") : null).ConfigureAwait(false);
        if (request[0] == "GET")
            await CopyRangeAsync(stream, start, end).ConfigureAwait(false);
    }

    private bool TryGetRange(string range, out long start, out long end)
    {
        start = 0;
        end = Length - 1;
        if (!range.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || Length == 0)
            return false;
        var values = range[6..].Split('-');
        if (values.Length != 2)
            return false;
        if (values[0].Length == 0)
        {
            if (!long.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix == 0)
                return false;
            start = Length - Math.Min(Length, suffix);
        }
        else
        {
            if (!long.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out start))
                return false;
            if (values[1].Length > 0)
            {
                if (!long.TryParse(values[1], NumberStyles.None, CultureInfo.InvariantCulture, out end))
                    return false;
                end = Math.Min(end, Length - 1);
            }
        }
        return start <= end && start < Length;
    }

    private async Task WriteHeadersAsync(NetworkStream stream, string status, long length, string? range = null)
    {
        var headers = FormattableString.Invariant($"HTTP/1.1 {status}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n");
        if (range != null)
            headers += $"Content-Range: {range}\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers + "\r\n"), _stop.Token).ConfigureAwait(false);
    }

    private async Task CopyRangeAsync(NetworkStream output, long position, long end)
    {
        // 累计结束位置可能重复（空分片）；查找第一个结束位置大于 position 的文件。
        var low = 0;
        var high = _ends.Length;
        while (low < high)
        {
            var mid = low + (high - low) / 2;
            if (_ends[mid] <= position)
                low = mid + 1;
            else
                high = mid;
        }
        var buffer = new byte[64 * 1024];
        for (var i = low; i < _files.Length && position <= end; i++)
        {
            var fileStart = i == 0 ? 0 : _ends[i - 1];
            if (_ends[i] == fileStart)
                continue;
            FileStream? input = null;
            try
            {
                // 已使用上面的传输缓冲；关闭 FileStream 内部缓冲，避免每个分片尾部
                // 的小块读取再分配一份缓冲并复制数据。
                input = new FileStream(_files[i], FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (input.Length != _ends[i] - fileStart)
                {
                    throw new IOException(ResString.concatInputLengthChanged);
                }
                input.Position = position - fileStart;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                input?.Dispose();
                Interlocked.CompareExchange(ref _error, ex, null);
                throw;
            }
            using (input)
            {
                var remaining = Math.Min(end + 1, _ends[i]) - position;
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), _stop.Token).ConfigureAwait(false);
                        if (read == 0)
                            throw new EndOfStreamException();
                    }
                    catch (IOException ex)
                    {
                        Interlocked.CompareExchange(ref _error, ex, null);
                        throw;
                    }
                    await output.WriteAsync(buffer.AsMemory(0, read), _stop.Token).ConfigureAwait(false);
                    position += read;
                    remaining -= read;
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        _listener.Stop();
        Task.WhenAll(_workers).GetAwaiter().GetResult();
        _stop.Dispose();
    }
}