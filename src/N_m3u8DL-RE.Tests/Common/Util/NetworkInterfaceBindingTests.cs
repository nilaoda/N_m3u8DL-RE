using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using N_m3u8DL_RE.Common.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class NetworkInterfaceBindingTests
{
    [Theory]
    [InlineData("missing-network-interface-875")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public void InvalidInterfaceOrUnspecifiedAddressIsRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => NetworkInterfaceBinding.Create(value));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task HttpAndProxyConnectionsUseSelectedInterfaceOrAddress(bool byName, bool ipv6, bool proxy)
    {
        if (ipv6 && !Socket.OSSupportsIPv6)
            return;
        var address = ipv6 ? IPAddress.IPv6Loopback : IPAddress.Loopback;
        var selected = NetworkInterface.GetAllNetworkInterfaces().First(n =>
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
        var binding = NetworkInterfaceBinding.Create(byName ? selected.Name : address.ToString());
        using var listener = new TcpListener(address, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = binding.ConnectAsync,
            UseProxy = proxy,
            Proxy = proxy ? new WebProxy($"http://127.0.0.1:{endpoint.Port}") : null,
        };
        using var client = new HttpClient(handler);
        var url = proxy ? "http://example.invalid/file" : $"http://{endpoint}/file";
        var download = client.GetStringAsync(url, timeout.Token);
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        // 检查服务端实际看到的源地址，避免只验证配置对象却没有绑定真实连接。
        Assert.Equal(address, ((IPEndPoint)connection.Client.RemoteEndPoint!).Address);
        await using var stream = connection.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        var request = await reader.ReadLineAsync(timeout.Token);
        Assert.Equal(proxy ? "GET http://example.invalid/file HTTP/1.1" : "GET /file HTTP/1.1", request);
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
        await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), timeout.Token);
        Assert.Equal("ok", await download);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task HttpsAndConnectTunnelPreserveTlsAndInterfaceBinding(bool byName, bool proxy)
    {
        var address = IPAddress.Loopback;
        var selected = NetworkInterface.GetAllNetworkInterfaces().First(n =>
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.Equals(address)));
        var binding = NetworkInterfaceBinding.Create(byName ? selected.Name : address.ToString());
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        using var listener = new TcpListener(address, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new SocketsHttpHandler
        {
            ConnectCallback = binding.ConnectAsync,
            UseProxy = proxy,
            Proxy = proxy ? new WebProxy($"http://127.0.0.1:{endpoint.Port}") : null,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    cert != null && cert.GetRawCertData().AsSpan().SequenceEqual(certificate.RawData),
            },
        };
        using var client = new HttpClient(handler);
        var download = client.GetStringAsync(proxy ? "https://example.invalid/file" : $"https://localhost:{endpoint.Port}/file", timeout.Token);
        using var connection = await listener.AcceptTcpClientAsync(timeout.Token);
        Assert.Equal(address, ((IPEndPoint)connection.Client.RemoteEndPoint!).Address);
        await using var stream = connection.GetStream();
        if (proxy)
        {
            using var proxyReader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
            Assert.Equal("CONNECT example.invalid:443 HTTP/1.1", await proxyReader.ReadLineAsync(timeout.Token));
            while (!string.IsNullOrEmpty(await proxyReader.ReadLineAsync(timeout.Token))) { }
            await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray(), timeout.Token);
        }
        // 回调只建立 TCP，TLS 和代理隧道仍交给 HTTP handler，必须保留目标主机的 SNI。
        await using var tls = new SslStream(stream, leaveInnerStreamOpen: true);
        await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = certificate }, timeout.Token);
        Assert.Equal(proxy ? "example.invalid" : "localhost", tls.TargetHostName);
        using var reader = new StreamReader(tls, Encoding.ASCII, leaveOpen: true);
        Assert.Equal("GET /file HTTP/1.1", await reader.ReadLineAsync(timeout.Token));
        while (!string.IsNullOrEmpty(await reader.ReadLineAsync(timeout.Token))) { }
        await tls.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 2\r\nConnection: close\r\n\r\nok"u8.ToArray(), timeout.Token);
        Assert.Equal("ok", await download);
    }

    [Fact]
    public async Task SelectedIpv4AddressDoesNotFallBackToIpv6()
    {
        if (!Socket.OSSupportsIPv6)
            return;
        using var listener = new TcpListener(IPAddress.IPv6Loopback, 0);
        listener.Start();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = NetworkInterfaceBinding.Create("127.0.0.1").ConnectAsync,
        };
        using var client = new HttpClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"http://{listener.LocalEndpoint}/"));
        Assert.False(listener.Pending());
    }

    [Fact]
    public async Task NamedExternalInterfaceDoesNotFallBackToLoopback()
    {
        var selected = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
            n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            n.GetIPProperties().UnicastAddresses.Any(a => a.Address.AddressFamily == AddressFamily.InterNetwork));
        if (selected == null)
            return;
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = NetworkInterfaceBinding.Create(selected.Name).ConnectAsync,
        };
        using var client = new HttpClient(handler);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var error = await Record.ExceptionAsync(() => client.GetAsync($"http://{listener.LocalEndpoint}/", timeout.Token));
        Assert.True(error is HttpRequestException or OperationCanceledException);
        Assert.False(listener.Pending());
    }
}
