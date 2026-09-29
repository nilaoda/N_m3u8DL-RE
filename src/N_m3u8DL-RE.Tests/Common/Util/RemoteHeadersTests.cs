using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

[CollectionDefinition("HTTPUtil state", DisableParallelization = true)]
public class HTTPUtilStateCollection;

[Collection("HTTPUtil state")]
public class RemoteHeadersTests : IDisposable
{
    public void Dispose()
    {
        HTTPUtil.ConfigureRemoteHeaders(false);
        HTTPUtil.ChangeHosts.Clear();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedirectAndSegment_MergeHeadersOnlyWhenEnabled(bool enabled)
    {
        HTTPUtil.ConfigureRemoteHeaders(enabled);
        await using var server = new LoopbackServer(
            new("302 Found", "Location: /media.m3u8\r\nX-Custom-Header: my-value\r\nX-Entry: retained\r\n"),
            new("200 OK", "x-custom-header: newer-value\r\nX-Playlist: playlist-value\r\n", "#EXTM3U\n"),
            new("200 OK", "X-Key-Response: do-not-inherit\r\n", "key"),
            new("200 OK", "", "segment"));
        var headers = new Dictionary<string, string>
        {
            ["X-CUSTOM-HEADER"] = "user-value",
            ["X-Global"] = "global-value"
        };

        var (_, redirectedUrl) = await HTTPUtil.GetWebSourceAndNewUrlAsync(server.Url + "entry.m3u8", headers);
        Assert.Equal(server.Url + "media.m3u8", redirectedUrl);
        await HTTPUtil.GetBytesAsync(server.Url + "key", headers);
        var path = Path.GetTempFileName();
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await DownloadUtil.DownloadToFileAsync(server.Url + "segment.ts", path, new SpeedContainer(), cancellation, headers);
            Assert.Equal("segment", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
        await server.Completion;

        Assert.Equal("user-value", server.Requests[0]["X-Custom-Header"]);
        Assert.Equal(enabled ? "my-value" : "user-value", server.Requests[1]["X-Custom-Header"]);
        foreach (var request in server.Requests.Skip(2))
        {
            Assert.Equal(enabled ? "newer-value" : "user-value", request["X-Custom-Header"]);
            Assert.Equal("global-value", request["X-Global"]);
            Assert.Equal(enabled, request.ContainsKey("X-Entry"));
            Assert.Equal(enabled, request.ContainsKey("X-Playlist"));
            if (enabled)
            {
                Assert.Equal("retained", request["X-Entry"]);
                Assert.Equal("playlist-value", request["X-Playlist"]);
            }
            Assert.False(request.ContainsKey("X-Key-Response"));
        }
        Assert.Equal("user-value", headers["X-CUSTOM-HEADER"]);
    }

    [Fact]
    public async Task Refresh_PreservesUnionAndIgnoresFailedResponses()
    {
        HTTPUtil.ConfigureRemoteHeaders(true);
        await using var server = new LoopbackServer(
            new("200 OK", "X-Token: first\r\nX-Old: retained\r\n", "#EXTM3U\n"),
            new("200 OK", "x-token: refreshed\r\nX-New: added\r\n", "#EXTM3U\n"),
            new("403 Forbidden", "X-Token: invalid\r\n", "expired"));
        await HTTPUtil.GetWebSourceAndNewUrlAsync(server.Url);
        await HTTPUtil.GetWebSourceAndNewUrlAsync(server.Url);
        await Assert.ThrowsAsync<HttpRequestException>(() => HTTPUtil.GetWebSourceAndNewUrlAsync(server.Url));
        await server.Completion;

        using var request = HTTPUtil.CreateRequest(HttpMethod.Get, server.Url);
        HTTPUtil.ApplyHeaders(request, null);
        Assert.Equal("refreshed", Assert.Single(request.Headers.GetValues("X-Token")));
        Assert.Equal("retained", Assert.Single(request.Headers.GetValues("X-Old")));
        Assert.Equal("added", Assert.Single(request.Headers.GetValues("X-New")));
        Assert.Equal("first", server.Requests[1]["X-Token"]);
        Assert.Equal("refreshed", server.Requests[2]["X-Token"]);

        HTTPUtil.ConfigureRemoteHeaders(false);
        using var disabledRequest = HTTPUtil.CreateRequest(HttpMethod.Get, server.Url);
        HTTPUtil.ApplyHeaders(disabledRequest, null);
        Assert.False(disabledRequest.Headers.Contains("X-Token"));
        HTTPUtil.ConfigureRemoteHeaders(true);
        using var freshRequest = HTTPUtil.CreateRequest(HttpMethod.Get, server.Url);
        HTTPUtil.ApplyHeaders(freshRequest, null);
        Assert.False(freshRequest.Headers.Contains("X-Token"));
    }

    [Fact]
    public async Task TransportHeaders_AreNotForwardedAndChangeHostStillWins()
    {
        HTTPUtil.ConfigureRemoteHeaders(true);
        await using var server = new LoopbackServer(new Response("200 OK",
            "Connection: X-Hop\r\nX-Hop: connection-only\r\nHost: wrong.example\r\n" +
            "Content-Type: application/vnd.apple.mpegurl\r\nServer: test-server\r\n" +
            "Location: /response-only\r\nKeep-Alive: timeout=5\r\n" +
            "X-Custom-Header: my-value\r\nX-Multiple: one\r\nX-Multiple: two\r\n", "#EXTM3U\n"));
        await HTTPUtil.GetWebSourceAndNewUrlAsync(server.Url);
        await server.Completion;
        HTTPUtil.ChangeHosts["original.example"] = new Uri(server.Url).Authority;
        using var request = HTTPUtil.CreateRequest(HttpMethod.Get, "http://original.example/segment.ts");
        request.Headers.Range = new(0, 100);
        HTTPUtil.ApplyHeaders(request, new() { ["Host"] = "user.example" });
        Assert.Equal(new Uri(server.Url).Authority, request.RequestUri!.Authority);
        Assert.Equal("original.example", request.Headers.Host);
        Assert.Equal("bytes=0-100", request.Headers.Range!.ToString());
        Assert.Equal("my-value", Assert.Single(request.Headers.GetValues("X-Custom-Header")));
        Assert.Equal(new[] { "one", "two" }, request.Headers.GetValues("X-Multiple"));
        var headerNames = request.Headers.Select(header => header.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var excluded in new[] { "X-Hop", "Connection", "Keep-Alive", "Content-Type", "Content-Length", "Server", "Location" })
            Assert.DoesNotContain(excluded, headerNames);
    }

    private record Response(string Status, string Headers, string Body = "");

    private sealed class LoopbackServer : IAsyncDisposable
    {
        private readonly TcpListener listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource cancellation = new(TimeSpan.FromSeconds(15));
        public string Url { get; }
        public List<Dictionary<string, string>> Requests { get; } = [];
        public Task Completion { get; }

        public LoopbackServer(params Response[] responses)
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            Completion = ServeAsync(responses);
        }

        private async Task ServeAsync(Response[] responses)
        {
            foreach (var response in responses)
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                await reader.ReadLineAsync(cancellation.Token);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (await reader.ReadLineAsync(cancellation.Token) is { Length: > 0 } line)
                {
                    var separator = line.IndexOf(':');
                    headers.Add(line[..separator], line[(separator + 1)..].Trim());
                }
                Requests.Add(headers);
                var body = Encoding.UTF8.GetBytes(response.Body);
                var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {response.Status}\r\nConnection: close\r\nContent-Length: {body.Length}\r\n{response.Headers}\r\n");
                await stream.WriteAsync(head, cancellation.Token);
                await stream.WriteAsync(body, cancellation.Token);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            listener.Stop();
            try { await Completion; }
            catch (OperationCanceledException) { }
            finally { cancellation.Dispose(); }
        }
    }
}
