using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging;
using N_m3u8DL_RE.CommandLine;
using N_m3u8DL_RE.Common.Entity;
using N_m3u8DL_RE.Common.Util;
using N_m3u8DL_RE.DownloadManager;
using N_m3u8DL_RE.Entity;
using N_m3u8DL_RE.Util;

namespace N_m3u8DL_RE.Tests.Common.Util;

[Collection("Download console")]
public class HttpVersionTests
{
    [Theory]
    [InlineData(null, HttpProtocols.Http1AndHttp2, "HTTP/1.1")]
    [InlineData("1.1", HttpProtocols.Http1AndHttp2, "HTTP/1.1")]
    [InlineData("2", HttpProtocols.Http2, "HTTP/2")]
    [InlineData("2", HttpProtocols.Http1AndHttp2, "HTTP/2")]
    [InlineData("2", HttpProtocols.Http1, "HTTP/1.1")]
    public async Task MediaRequestsUseHttp1ByDefaultAndAllowOptingIntoHttp2(string? httpVersion, HttpProtocols protocols, string expectedProtocol)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var key = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
        var data = Enumerable.Range(0, 3072).Select(i => (byte)(i % 251)).ToArray();
        const string manifest = "#EXTM3U\n#EXT-X-TARGETDURATION:1\n#EXTINF:1,\nfile\n#EXT-X-ENDLIST\n";
        ConcurrentQueue<(string Method, string Path, string Protocol, string Header, string Range)> requests = [];

        // 使用真实 TLS/ALPN 和 HTTP/2 服务端，不能只检查请求对象上的版本属性。
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0, listener =>
        {
            listener.Protocols = protocols;
            listener.UseHttps(certificate);
        }));
        await using var app = builder.Build();
        app.Run(async context =>
        {
            var path = context.Request.Path.Value!;
            requests.Enqueue((context.Request.Method, path, context.Request.Protocol,
                context.Request.Headers["X-Fixture"].ToString(), context.Request.Headers.Range.ToString()));
            if (path == "/post")
            {
                await context.Request.Body.CopyToAsync(Stream.Null, context.RequestAborted);
                await context.Response.WriteAsync("ok", context.RequestAborted);
                return;
            }
            if (context.Request.Headers["X-Fixture"] != "fixture")
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if (path.StartsWith("/redirect-", StringComparison.Ordinal))
            {
                context.Response.Redirect("/" + path[10..]);
                return;
            }
            if (path == "/manifest")
            {
                await context.Response.WriteAsync(manifest, context.RequestAborted);
                return;
            }
            var body = path == "/key" ? data[..16] : data;
            var from = 0;
            var to = body.Length - 1;
            context.Response.Headers.AcceptRanges = "bytes";
            context.Response.Headers.ETag = "\"fixture-v1\"";
            if (context.Request.Headers.Range.Count > 0)
            {
                var range = RangeHeaderValue.Parse(context.Request.Headers.Range.ToString()).Ranges.Single();
                from = (int)(range.From ?? 0);
                to = (int)(range.To ?? to);
                context.Response.StatusCode = StatusCodes.Status206PartialContent;
                context.Response.Headers.ContentRange = $"bytes {from}-{to}/{body.Length}";
            }
            context.Response.ContentLength = to - from + 1;
            if (!HttpMethods.IsHead(context.Request.Method))
                await context.Response.Body.WriteAsync(body.AsMemory(from, to - from + 1), context.RequestAborted);
        });
        await app.StartAsync(timeout.Token);
        var url = app.Urls.Single();
        Dictionary<string, string> headers = new() { ["X-Fixture"] = "fixture" };
        if (httpVersion == null)
        {
            using var source = await HTTPUtil.GetWebSourceResultAsync(url + "/redirect-manifest", headers,
                cancellationToken: timeout.Token);
            Assert.Equal(manifest, source.Source);
            Assert.Equal(data[..16], await HTTPUtil.GetBytesAsync(url + "/redirect-key", headers, timeout.Token));
            Assert.Equal("ok", await HTTPUtil.GetPostResponseAsync(url + "/post", "body"u8.ToArray()));
            Assert.True(await LargeSingleFileSplitUtil.CanSplitAsync(url + "/file", headers));
            Assert.NotNull(await LargeSingleFileSplitUtil.SplitUrlAsync(new MediaSegment { Url = url + "/file" }, headers));

            var directory = Directory.CreateTempSubdirectory("re-http2-").FullName;
            try
            {
                var segmentPath = Path.Combine(directory, "segment.bin");
                await DownloadUtil.DownloadToFileAsync(url + "/redirect-file", segmentPath, new SpeedContainer(), timeout, headers);
                Assert.Equal(data, await File.ReadAllBytesAsync(segmentPath, timeout.Token));

                using var handler = new SocketsHttpHandler
                {
                    UseProxy = false,
                    AllowAutoRedirect = false,
                    ConnectCallback = NetworkInterfaceBinding.Create("127.0.0.1").ConnectAsync,
                    SslOptions = new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, cert, _, _) =>
                            cert != null && cert.GetRawCertData().AsSpan().SequenceEqual(certificate.RawData),
                    },
                };
                using var client = new HttpClient(handler)
                {
                    DefaultRequestVersion = HttpVersion.Version11,
                    DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
                };
                var binaryPath = Path.Combine(directory, "binary.bin");
                var manager = new BinaryDownloadManager(client, blockSize: 512);
                await manager.DownloadAsync(url + "/redirect-file", binaryPath, headers, threadCount: 2, retryCount: 0,
                    onLength: length => Assert.Equal(data.Length, length), onReceived: _ => { }, onDownloaded: _ => { },
                    cancellationToken: timeout.Token);
                Assert.Equal(data, await File.ReadAllBytesAsync(binaryPath, timeout.Token));
            }
            finally
            {
                Directory.Delete(directory, true);
            }

            Assert.Contains(requests, request => request.Method == "HEAD");
        }
        else
        {
            await DownloadWithCliAsync(url, data, httpVersion, expectedProtocol, timeout.Token);
        }

        Assert.All(requests, request => Assert.Equal(expectedProtocol, request.Protocol));
        Assert.All(requests.Where(request => request.Path != "/post"), request => Assert.Equal("fixture", request.Header));
        Assert.Contains(requests, request => request.Range.Length > 0);
        await app.StopAsync(timeout.Token);
    }

    private static async Task DownloadWithCliAsync(string url, byte[] data, string httpVersion, string expectedProtocol,
        CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("re-http-version-cli-").FullName;
        try
        {
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = directory,
            };
            foreach (var arg in new[] { typeof(CommandInvoker).Assembly.Location, url + "/redirect-file",
                "--http-version", httpVersion, "--interface", "127.0.0.1", "--use-system-proxy", "false",
                "-H", "X-Fixture: fixture", "--save-dir", directory, "--save-name", "result.bin",
                "--thread-count", "2", "--download-retry-count", "0", "--no-log", "--no-ansi-color",
                "--disable-update-check", "--log-level", "DEBUG" })
                info.ArgumentList.Add(arg);
            using var process = Process.Start(info)!;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var output = await stdout + await stderr;
                Assert.True(process.ExitCode == 0, output);
                // 响应日志须反映真实协商结果，包括 HTTP/2 回退为 HTTP/1.1 的情况。
                Assert.Contains(expectedProtocol == "HTTP/2" ? "HTTP/2.0 200" : "HTTP/1.1 200", output);
                Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(directory, "result.bin"), cancellationToken));
            }
            finally
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
