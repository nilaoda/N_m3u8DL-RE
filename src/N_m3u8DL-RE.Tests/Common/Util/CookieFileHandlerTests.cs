using System.Net;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class CookieFileHandlerTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RedirectsMatchTargetCookiesAndServerUpdatesAreRetained(bool useFile)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var source = $"http://127.0.0.1:{port}";
        var target = $"http://localhost:{port}";
        string[] responses =
        [
            $"302 Found\r\nLocation: {target}/media/manifest\r\nSet-Cookie: sourceSession=updated; Path=/",
            "200 OK\r\nSet-Cookie: targetSession=updated; Path=/media",
            "200 OK",
            "200 OK",
            "200 OK"
        ];
        var server = Task.Run(async () =>
        {
            var received = new List<string>();
            foreach (var response in responses)
            {
                using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var cookie = "";
                while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } line)
                {
                    if (line.StartsWith("Cookie:", StringComparison.OrdinalIgnoreCase))
                        cookie = line[7..].Trim();
                }
                received.Add(cookie);
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {response}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), timeout.Token);
            }
            return received;
        }, timeout.Token);

        using var transport = new SocketsHttpHandler { UseCookies = !useFile, AllowAutoRedirect = false, UseProxy = false };
        using var handler = new CookieFileHandler(transport)
        {
            Cookies = useFile ? NetscapeCookieFile.Parse(
            [
                "127.0.0.1\tFALSE\t/\tFALSE\t0\tsource\tfile",
                "localhost\tFALSE\t/media\tFALSE\t0\ttarget\tfile"
            ]) : null
        };
        using var client = new HttpClient(handler);
        using var first = await client.GetAsync(source + "/start", timeout.Token);
        Assert.Equal(HttpStatusCode.Found, first.StatusCode);
        using var manifest = await client.GetAsync(first.Headers.Location, timeout.Token);
        using var segment = await client.GetAsync(target + "/media/segment", timeout.Token);
        using var manual = new HttpRequestMessage(HttpMethod.Get, target + "/media/init");
        manual.Headers.TryAddWithoutValidation("cOoKiE", "manual=exact");
        using var init = await client.SendAsync(manual, timeout.Token);
        using var key = await client.GetAsync(source + "/key", timeout.Token);
        var headers = await server;

        Assert.Equal(useFile ? "source=file" : "", headers[0]);
        Assert.Equal(useFile ? "target=file" : "", headers[1]);
        Assert.Contains("targetSession=updated", headers[2]);
        Assert.DoesNotContain("source", headers[1]);
        Assert.DoesNotContain("source", headers[2]);
        Assert.Contains("sourceSession=updated", headers[4]);
        Assert.DoesNotContain("target", headers[4]);
        if (useFile)
        {
            Assert.Contains("target=file", headers[2]);
            Assert.Equal("manual=exact", headers[3]);
            Assert.Contains("source=file", headers[4]);
        }
    }
}
