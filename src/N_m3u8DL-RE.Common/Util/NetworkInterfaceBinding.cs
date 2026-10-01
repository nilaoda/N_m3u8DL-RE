using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using N_m3u8DL_RE.Common.Resource;

namespace N_m3u8DL_RE.Common.Util;

internal sealed class NetworkInterfaceBinding
{
    private const int IpProtocol = 0;
    private const int Ipv6Protocol = 41;
    private const int LinuxSocketLevel = 1;
    private const int LinuxBindToDevice = 25;
    private const int MacIpBoundIf = 25;
    private const int MacIpv6BoundIf = 125;
    private const int WindowsUnicastInterface = 31;

    private readonly string value;
    private readonly NetworkInterface? networkInterface;
    private readonly IPAddress? localAddress;

    private NetworkInterfaceBinding(string value, NetworkInterface? networkInterface, IPAddress? localAddress)
    {
        this.value = value;
        this.networkInterface = networkInterface;
        this.localAddress = localAddress;
    }

    internal static NetworkInterfaceBinding Create(string value)
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        if (IPAddress.TryParse(value, out var address))
        {
            // IPv6 链路本地地址可省略 scope，但必须能唯一匹配到本机地址。
            var matches = interfaces.SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .Where(a => a.AddressFamily == address.AddressFamily &&
                    a.GetAddressBytes().AsSpan().SequenceEqual(address.GetAddressBytes()) &&
                    (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId == 0 || a.ScopeId == address.ScopeId))
                .Distinct().ToArray();
            if (matches.Length == 1 && !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any))
                return new NetworkInterfaceBinding(value, null, matches[0]);
        }
        else
        {
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var selected = interfaces.FirstOrDefault(n => string.Equals(n.Name, value, comparison) ||
                string.Equals(n.Id, value, comparison));
            if (selected != null && selected.OperationalStatus == OperationalStatus.Up &&
                selected.GetIPProperties().UnicastAddresses.Count > 0)
            {
                if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
                    throw new PlatformNotSupportedException(ResString.networkInterfaceUnsupported);
                return new NetworkInterfaceBinding(value, selected, null);
            }
        }
        throw new ArgumentException(string.Format(ResString.networkInterfaceInvalid, value));
    }

    internal async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        // DNS 仍由系统解析；所有候选连接都限制在用户指定的接口或源地址上。
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        var localAddresses = networkInterface?.GetIPProperties().UnicastAddresses.Select(a => a.Address).ToArray()
            ?? [localAddress!];
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            token.ThrowIfCancellationRequested();
            if (!localAddresses.Any(a => a.AddressFamily == address.AddressFamily))
                continue;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                Bind(socket);
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex)
            {
                lastError = ex;
                socket.Dispose();
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        throw new HttpRequestException(string.Format(ResString.networkInterfaceConnectFailed, value, context.DnsEndPoint), lastError);
    }

    private void Bind(Socket socket)
    {
        try
        {
            if (networkInterface != null)
            {
                // 仅绑定网卡上的 IP 不能保证实际出口；网卡名必须同时设置系统的接口约束。
                if (OperatingSystem.IsLinux())
                {
                    socket.SetRawSocketOption(LinuxSocketLevel, LinuxBindToDevice,
                        Encoding.UTF8.GetBytes(networkInterface.Name + '\0'));
                }
                else
                {
                    var ipv6 = socket.AddressFamily == AddressFamily.InterNetworkV6;
                    var properties = networkInterface.GetIPProperties();
                    var index = ipv6 ? properties.GetIPv6Properties().Index : properties.GetIPv4Properties().Index;
                    // 序号 0 会取消接口约束，不能把无效序号当作成功绑定。
                    if (index <= 0)
                        throw new IOException(string.Format(ResString.networkInterfaceInvalid, value));
                    // Windows 的 IPv4 接口序号采用网络字节序，IPv6 和 macOS 则采用本机字节序。
                    var optionValue = OperatingSystem.IsWindows() && !ipv6 ? IPAddress.HostToNetworkOrder(index) : index;
                    var optionName = OperatingSystem.IsWindows() ? WindowsUnicastInterface
                        : ipv6 ? MacIpv6BoundIf : MacIpBoundIf;
                    socket.SetRawSocketOption(ipv6 ? Ipv6Protocol : IpProtocol, optionName, BitConverter.GetBytes(optionValue));
                }
            }
            var source = localAddress ?? (socket.AddressFamily == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any);
            socket.Bind(new IPEndPoint(source, 0));
        }
        catch (SocketException ex)
        {
            // 权限不足、地址已失效等绑定错误必须上报，不能退回默认接口继续下载。
            throw new IOException(string.Format(ResString.networkInterfaceBindFailed, value, ex.Message), ex);
        }
    }
}
