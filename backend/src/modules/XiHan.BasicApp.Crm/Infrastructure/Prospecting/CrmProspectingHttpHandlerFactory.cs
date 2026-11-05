using System.Net;
using System.Net.Sockets;

namespace XiHan.BasicApp.Crm.Infrastructure.Prospecting;

/// <summary>
/// Creates outbound handlers for fixed, trusted procurement APIs. DNS answers are validated
/// and the socket connects directly to the selected address, so the TCP connection cannot
/// perform a second DNS lookup (DNS-rebinding pinning). TLS validation remains owned by HttpClient.
/// </summary>
public static class CrmProspectingHttpHandlerFactory
{
    public static SocketsHttpHandler Create(params string[] approvedHosts)
    {
        ArgumentNullException.ThrowIfNull(approvedHosts);
        var hosts = approvedHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().TrimEnd('.'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (hosts.Count == 0)
            throw new ArgumentException("At least one approved provider host is required.", nameof(approvedHosts));

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectCallback = (context, cancellationToken) =>
                ConnectPinnedAsync(context, hosts, cancellationToken)
        };
    }

    private static async ValueTask<Stream> ConnectPinnedAsync(
        SocketsHttpConnectionContext context,
        HashSet<string> approvedHosts,
        CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host.TrimEnd('.');
        if (!approvedHosts.Contains(host))
            throw new HttpRequestException("The prospecting HTTP destination host is not approved.");

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new HttpRequestException("The prospecting provider DNS response contained a non-public IP address.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception exception) when (exception is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (exception is OperationCanceledException)
                    throw;
                lastError = exception;
            }
        }

        throw new HttpRequestException("Could not connect to an approved public provider address.", lastError);
    }

    /// <summary>
    /// Conservative public-unicast filter. Known non-public/special-use ranges are rejected; IPv6 must be global unicast.
    /// IPv4-mapped IPv6 answers are normalized and checked as IPv4.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(InPrefix(bytes, 8, 0)
                || InPrefix(bytes, 8, 10)
                || InPrefix(bytes, 10, 100, 64)
                || InPrefix(bytes, 8, 127)
                || InPrefix(bytes, 16, 169, 254)
                || InPrefix(bytes, 12, 172, 16)
                || InPrefix(bytes, 24, 192, 0, 0)
                || InPrefix(bytes, 24, 192, 0, 2)
                || InPrefix(bytes, 24, 192, 31, 196)
                || InPrefix(bytes, 24, 192, 52, 193)
                || InPrefix(bytes, 24, 192, 88, 99)
                || InPrefix(bytes, 24, 192, 175, 48)
                || InPrefix(bytes, 24, 192, 175, 48)
                || InPrefix(bytes, 16, 192, 168)
                || InPrefix(bytes, 15, 198, 18)
                || InPrefix(bytes, 24, 198, 51, 100)
                || InPrefix(bytes, 24, 203, 0, 113)
                || bytes[0] >= 224);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        // Only global-unicast 2000::/3 is accepted. Exclude documentation,
        // benchmarking/transition and other special-purpose assignments.
        return (bytes[0] & 0xE0) == 0x20
            && !InPrefix(bytes, 32, 0x20, 0x01, 0x0D, 0xB8)
            && !InPrefix(bytes, 23, 0x20, 0x01, 0x00, 0x00)
            && !InPrefix(bytes, 16, 0x20, 0x02)
            && !InPrefix(bytes, 20, 0x3F, 0xFF);
    }

    private static bool InPrefix(byte[] address, int prefixLength, params int[] prefix)
    {
        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;
        for (var i = 0; i < wholeBytes; i++)
            if (i >= prefix.Length || address[i] != prefix[i]) return false;
        if (remainingBits == 0) return true;
        if (wholeBytes >= prefix.Length) return false;
        var mask = 0xFF << (8 - remainingBits);
        return (address[wholeBytes] & mask) == (prefix[wholeBytes] & mask);
    }
}
