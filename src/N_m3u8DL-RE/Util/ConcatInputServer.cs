using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Unicode;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Util;

// 对外提供可 seek 的连续字节输入，避免 concat 协议同时打开所有分片。
// 不改写媒体内容或时间戳，也不生成整份中间文件。
internal sealed class ConcatInputServer : IDisposable
{
    private const int HeaderLimit = 16 * 1024;
    private const int BufferSize = 64 * 1024;
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
        _files = Array.ConvertAll(files, Path.GetFullPath);
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
        // 每个工作循环复用一个缓冲；请求头只占前 HeaderLimit 字节，
        // 解析完成后用于响应头和媒体传输，避免每次 seek 都重新分配。
        var buffer = new byte[BufferSize];
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
                try
                {
                    await RespondAsync(client.GetStream(), buffer).ConfigureAwait(false);
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

    private async Task<string?> ReadHeadersAsync(NetworkStream stream, byte[] buffer)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var count = 0;
        while (count < HeaderLimit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(count, HeaderLimit - count), timeout.Token).ConfigureAwait(false);
            if (read == 0)
                return null;
            // 保留上次读取的最后三个字节，识别跨读取边界的请求头结束标记。
            var searchStart = Math.Max(0, count - 3);
            count += read;
            var headerEnd = buffer.AsSpan(searchStart, count - searchStart).IndexOf("\r\n\r\n"u8);
            if (headerEnd >= 0)
                return Encoding.ASCII.GetString(buffer.AsSpan(0, searchStart + headerEnd));
        }
        return null;
    }

    private async Task RespondAsync(NetworkStream stream, byte[] buffer)
    {
        var headers = await ReadHeadersAsync(stream, buffer).ConfigureAwait(false);
        if (headers == null)
        {
            await WriteHeadersAsync(stream, buffer, "400 Bad Request", 0).ConfigureAwait(false);
            return;
        }
        var headerSpan = headers.AsSpan();
        var lines = headerSpan.Split("\r\n");
        lines.MoveNext();
        var requestLine = headerSpan[lines.Current];
        // 多留一个位置以识别字段过多；这里只记录切片边界，不创建子字符串。
        Span<Range> fields = stackalloc Range[4];
        if (requestLine.Split(fields, ' ') != 3 || !requestLine[fields[1]].SequenceEqual(_path))
        {
            await WriteHeadersAsync(stream, buffer, "404 Not Found", 0).ConfigureAwait(false);
            return;
        }
        var method = requestLine[fields[0]];
        var isHead = method is "HEAD";
        if (!isHead && method is not "GET")
        {
            await WriteHeadersAsync(stream, buffer, "405 Method Not Allowed", 0).ConfigureAwait(false);
            return;
        }
        if (Error != null)
        {
            await WriteHeadersAsync(stream, buffer, "500 Internal Server Error", 0).ConfigureAwait(false);
            return;
        }
        long start = 0;
        var end = Length - 1;
        const string rangeHeader = "Range:";
        ReadOnlySpan<char> rangeValue = default;
        var rangeCount = 0;
        while (lines.MoveNext())
        {
            var line = headerSpan[lines.Current];
            if (!line.StartsWith(rangeHeader, StringComparison.OrdinalIgnoreCase))
                continue;
            rangeValue = line[rangeHeader.Length..].Trim();
            rangeCount++;
        }
        // HEAD 按整个虚拟文件返回长度；GET 支持 FFmpeg 使用的单一字节范围。
        var partial = !isHead && rangeCount > 0;
        if (partial && (rangeCount != 1 || !TryGetRange(rangeValue, out start, out end)))
        {
            await WriteHeadersAsync(stream, buffer, "416 Range Not Satisfiable", 0,
                FormattableString.Invariant($"bytes */{Length}")).ConfigureAwait(false);
            return;
        }
        await WriteHeadersAsync(stream, buffer, partial ? "206 Partial Content" : "200 OK", end - start + 1,
            partial ? FormattableString.Invariant($"bytes {start}-{end}/{Length}") : null).ConfigureAwait(false);
        if (!isHead)
            await CopyRangeAsync(stream, buffer, start, end).ConfigureAwait(false);
    }

    private bool TryGetRange(ReadOnlySpan<char> range, out long start, out long end)
    {
        start = 0;
        end = Length - 1;
        const string rangeUnit = "bytes=";
        if (!range.StartsWith(rangeUnit, StringComparison.OrdinalIgnoreCase) || Length == 0)
            return false;
        range = range[rangeUnit.Length..];
        var separator = range.IndexOf('-');
        if (separator < 0)
            return false;
        var startValue = range[..separator];
        var endValue = range[(separator + 1)..];
        if (startValue.IsEmpty)
        {
            if (!long.TryParse(endValue, NumberStyles.None, CultureInfo.InvariantCulture, out var suffix) || suffix == 0)
                return false;
            start = Length - Math.Min(Length, suffix);
        }
        else
        {
            if (!long.TryParse(startValue, NumberStyles.None, CultureInfo.InvariantCulture, out start))
                return false;
            if (!endValue.IsEmpty)
            {
                if (!long.TryParse(endValue, NumberStyles.None, CultureInfo.InvariantCulture, out end))
                    return false;
                end = Math.Min(end, Length - 1);
            }
        }
        return start <= end && start < Length;
    }

    private async Task WriteHeadersAsync(NetworkStream stream, byte[] buffer, string status, long length, string? range = null)
    {
        // 直接格式化到复用的字节缓冲，避免先拼接字符串再编码；数值不受当前区域设置影响。
        var headers = buffer.AsSpan();
        if (!Utf8.TryWrite(headers, CultureInfo.InvariantCulture,
            $"HTTP/1.1 {status}\r\nContent-Length: {length}\r\nAccept-Ranges: bytes\r\nContent-Type: application/octet-stream\r\nConnection: close\r\n", out var written))
            throw new InvalidOperationException();
        if (range != null)
        {
            if (!Utf8.TryWrite(headers[written..], CultureInfo.InvariantCulture,
                $"Content-Range: {range}\r\n", out var rangeWritten))
                throw new InvalidOperationException();
            written += rangeWritten;
        }
        "\r\n"u8.CopyTo(headers[written..]);
        await stream.WriteAsync(buffer.AsMemory(0, written + 2), _stop.Token).ConfigureAwait(false);
    }

    private async Task CopyRangeAsync(NetworkStream output, byte[] buffer, long position, long end)
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
        for (var i = low; i < _files.Length && position <= end; i++)
        {
            var fileStart = i == 0 ? 0 : _ends[i - 1];
            if (_ends[i] == fileStart)
                continue;
            FileStream? sourceStream = null;
            try
            {
                // 已使用上面的传输缓冲；关闭 FileStream 内部缓冲，避免每个分片尾部
                // 的小块读取再分配一份缓冲并复制数据。
                sourceStream = new FileStream(_files[i], FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (sourceStream.Length != _ends[i] - fileStart)
                {
                    throw new IOException(ResString.concatInputLengthChanged);
                }
                sourceStream.Position = position - fileStart;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                sourceStream?.Dispose();
                Interlocked.CompareExchange(ref _error, ex, null);
                throw;
            }
            using (sourceStream)
            {
                var remaining = Math.Min(end + 1, _ends[i]) - position;
                while (remaining > 0)
                {
                    int read;
                    try
                    {
                        read = await sourceStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), _stop.Token).ConfigureAwait(false);
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