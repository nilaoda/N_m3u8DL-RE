using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Util;

public class ConcatInputServerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("concat-input-").FullName;
    private readonly HttpClient _client = new(new HttpClientHandler { UseProxy = false })
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private string[] Files()
    {
        // 空文件和含中文/引号的路径不应影响虚拟字节边界。
        string[] names = ["空白.bin", "first '.bin", "empty.bin", "last.bin"];
        var files = names
            .Select(name => Path.Combine(_root, name)).ToArray();
        File.WriteAllBytes(files[0], []);
        File.WriteAllBytes(files[1], [0, 1, 2]);
        File.WriteAllBytes(files[2], []);
        File.WriteAllBytes(files[3], [3, 4, 5]);
        return files;
    }

    [Theory]
    [InlineData(null, 0, 6)]
    [InlineData("bytes=0-", 0, 6)]
    [InlineData("bytes=1-4", 1, 4)]
    [InlineData("bytes=3-", 3, 3)]
    [InlineData("bytes=5-99", 5, 1)]
    [InlineData("bytes=-2", 4, 2)]
    [InlineData("bytes=-99", 0, 6)]
    public async Task ReadsWholeInputAndRangesAcrossFileBoundaries(string? range, int start, int count)
    {
        using var server = new ConcatInputServer(Files());
        Assert.Equal("127.0.0.1", new Uri(server.Url).Host);
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url);
        if (range != null)
            request.Headers.TryAddWithoutValidation("Range", range);
        using var response = await _client.SendAsync(request);
        Assert.Equal(range == null ? HttpStatusCode.OK : HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(count, response.Content.Headers.ContentLength);
        Assert.Equal(Enumerable.Range(start, count).Select(i => (byte)i), await response.Content.ReadAsByteArrayAsync());
        if (range != null)
        {
            Assert.Equal(start, response.Content.Headers.ContentRange!.From);
            Assert.Equal(start + count - 1, response.Content.Headers.ContentRange.To);
            Assert.Equal(6, response.Content.Headers.ContentRange.Length);
        }
        Assert.Null(server.Error);
    }

    [Theory]
    [InlineData("bytes=6-")]
    [InlineData("bytes=4-2")]
    [InlineData("bytes=-0")]
    [InlineData("bytes=0-1,3-4")]
    [InlineData("bytes=9223372036854775808-")]
    [InlineData("bytes=abc-")]
    public async Task RejectsUnsatisfiableRanges(string range)
    {
        using var server = new ConcatInputServer(Files());
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url);
        request.Headers.TryAddWithoutValidation("Range", range);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode);
        Assert.Equal("bytes */6", response.Content.Headers.ContentRange!.ToString());
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task HeadDoesNotOpenThousandsOfSourceFiles()
    {
        var files = Files();
        using var server = new ConcatInputServer(Enumerable.Range(0, 4000).Select(i => files[i % files.Length]).ToArray());
        // 文件可独占打开，说明服务器没有长期持有源文件句柄；HEAD 也只读取长度索引。
        using var first = File.Open(files[1], FileMode.Open, FileAccess.Read, FileShare.None);
        using var last = File.Open(files[3], FileMode.Open, FileAccess.Read, FileShare.None);
        using var request = new HttpRequestMessage(HttpMethod.Head, server.Url);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6000, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ParallelSeeksReadIndependentPositions()
    {
        using var server = new ConcatInputServer(Files());
        await Task.WhenAll(Enumerable.Range(0, 12).Select(async i =>
        {
            var start = i % 6;
            using var request = new HttpRequestMessage(HttpMethod.Get, server.Url);
            request.Headers.Range = new(start, start);
            using var response = await _client.SendAsync(request);
            Assert.Equal([(byte)start], await response.Content.ReadAsByteArrayAsync());
        }));
    }

    [Fact]
    public async Task EmptyInputHasZeroLength()
    {
        using var server = new ConcatInputServer([]);
        using var response = await _client.GetAsync(server.Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task UnknownPathAndUnsupportedMethodDoNotExposeFiles()
    {
        using var server = new ConcatInputServer(Files());
        using var wrong = await _client.GetAsync(new Uri(new Uri(server.Url), "/other"));
        Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var post = await _client.PostAsync(server.Url, null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrChangedFileAbortsResponseAndRecordsFailure(bool changedLength)
    {
        var files = Files();
        using var server = new ConcatInputServer(files);
        if (changedLength)
            File.WriteAllBytes(files[1], [9]);
        else
            File.Delete(files[1]);
        await Assert.ThrowsAsync<HttpRequestException>(() => _client.GetByteArrayAsync(server.Url));
        Assert.IsAssignableFrom<IOException>(server.Error);
        using var response = await _client.GetAsync(server.Url);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [Fact]
    public async Task SeekCanProceedDuringBlockedTransferAndDisposeClosesSource()
    {
        var file = Path.Combine(_root, "large.bin");
        const int length = 32 * 1024 * 1024;
        using (var source = File.Create(file))
            source.SetLength(length);
        using var server = new ConcatInputServer([file]);
        var uri = new Uri(server.Url);
        using var blocked = new TcpClient();
        await blocked.ConnectAsync(uri.Host, uri.Port);
        var stream = blocked.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"GET {uri.AbsolutePath} HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n"));
        Assert.True(await stream.ReadAsync(new byte[1]) > 0);
        // 不再读取首条响应；尾部 seek 仍能由另一工作循环处理。
        using var request = new HttpRequestMessage(HttpMethod.Get, server.Url);
        request.Headers.Range = new(length - 1, null);
        using var response = await _client.SendAsync(request);
        Assert.Equal([0], await response.Content.ReadAsByteArrayAsync());
        await Task.Run(server.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task DisposeCancelsClientsWaitingForHeadersAndReleasesPort()
    {
        using var server = new ConcatInputServer(Files());
        var uri = new Uri(server.Url);
        using var first = new TcpClient();
        using var second = new TcpClient();
        await first.ConnectAsync(uri.Host, uri.Port);
        await second.ConnectAsync(uri.Host, uri.Port);
        await Task.Run(server.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, uri.Port);
        try
        {
            listener.Start();
        }
        finally
        {
            listener.Stop();
        }
    }

    public void Dispose()
    {
        _client.Dispose();
        Directory.Delete(_root, true);
    }
}