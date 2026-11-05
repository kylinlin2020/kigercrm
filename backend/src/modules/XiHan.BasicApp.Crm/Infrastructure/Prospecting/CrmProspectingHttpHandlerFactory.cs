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
    /// Conservative public-unicast filter. Unknown/special-use ranges fail closed.
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
            return !(InV4(bytes, 0, 8)
                || InV4(bytes, 10, 8)
                || InV4(bytes, 100, 64, 10)
                || InV4(bytes, 127, 8)
                || InV4(bytes, 169, 254, 16)
                || InV4(bytes, 172, 16, 12)
                || InV4(bytes, 192, 0, 0, 24)
                || InV4(bytes, 192, 0, 2, 24)
                || InV4(bytes, 192, 88, 99, 24)
                || InV4(bytes, 192, 168, 16)
                || InV4(bytes, 198, 18, 15)
                || InV4(bytes, 198, 51, 100, 24)
                || InV4(bytes, 203, 0, 113, 24)
                || bytes[0] >= 224);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        // Only global-unicast 2000::/3 is accepted. Exclude documentation,
        // benchmarking/transition and other special-purpose assignments.
        return (bytes[0] & 0xE0) == 0x20
            && !InV6(bytes, 0x20, 0x01, 0x0D, 0xB8, 32)
            && !InV6(bytes, 0x20, 0x01, 0x00, 0x00, 23)
            && !InV6(bytes, 0x20, 0x02, 0, 0, 16)
            && !InV6(bytes, 0x3F, 0xFF, 0, 0, 20);
    }

    private static bool InV4(byte[] bytes, params int[] prefix)
    {
        var prefixLength = prefix[^1];
        var octets = prefix.Length - 1;
        for (var i = 0; i < octets; i++)
            if (bytes[i] != prefix[i]) return false;
        var remaining = prefixLength - octets * 8;
        return remaining <= 0 || (bytes[octets] >> (8 - remaining)) == (prefix[octets] >> (8 - remaining));
    }

    private static bool InV6(byte[] bytes, byte a, byte b, byte c, byte d, int prefixLength)
    {
        var prefix = new byte[] { a, b, c, d };
        for (var i = 0; i < prefixLength / 8; i++)
            if (bytes[i] != prefix[i]) return false;
        return true;
    }
}
