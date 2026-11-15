using System.Net;
using System.Net.Sockets;

namespace BookTracker.Web.Services.Covers;

/// <summary>
/// Decides which upstream cover URLs the mirror may fetch and builds the HTTP
/// handler that enforces it on the wire. A cover URL comes from user-editable
/// fields and provider data, so the mirror fetches only <c>https</c> URLs with a
/// DNS host name on the default port, connects only to public addresses, and
/// follows at most <see cref="MaxRedirects"/> redirects, each re-checked.
/// </summary>
public static class CoverFetchPolicy
{
    /// <summary>Largest number of redirects the mirror follows for one cover.</summary>
    public const int MaxRedirects = 3;

    /// <summary>
    /// Returns null when <paramref name="url"/> may be fetched, otherwise a short
    /// reason for the refusal. Requires an absolute <c>https</c> URL whose host is
    /// a DNS name (not an IP literal), with no user info and the default port.
    /// </summary>
    public static string? Validate(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "not an absolute URL";
        if (uri.Scheme != Uri.UriSchemeHttps) return "scheme is not https";
        if (uri.HostNameType != UriHostNameType.Dns) return "host is not a DNS name";
        if (!string.IsNullOrEmpty(uri.UserInfo)) return "URL carries user info";
        if (!uri.IsDefaultPort) return "port is not the https default";
        return null;
    }

    /// <summary>
    /// The predicate the cover URL fields and the ISBN lookup share: true when
    /// <paramref name="url"/> is blank (the field is optional) or passes
    /// <see cref="Validate"/> once trimmed, so a saved cover URL is one the mirror
    /// can fetch.
    /// </summary>
    public static bool IsAcceptableCoverUrl(string? url) =>
        string.IsNullOrWhiteSpace(url) || Validate(url.Trim()) is null;

    /// <summary>
    /// <paramref name="url"/> with any user info removed, for log messages, so a
    /// URL carrying a credential does not write it to the log. Returns the input
    /// unchanged when it carries no user info or does not parse as an absolute URL.
    /// </summary>
    public static string ForLogging(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.UserInfo)) return url;
        return new UriBuilder(uri) { UserName = "", Password = "" }.Uri.AbsoluteUri;
    }

    /// <summary>
    /// True when <paramref name="address"/> is a public unicast address. False for
    /// loopback, unspecified, private (10/8, 172.16/12, 192.168/16, fc00::/7,
    /// site-local), link-local (169.254/16, fe80::/10), shared address space
    /// (100.64/10), 0.0.0.0/8, multicast and the reserved 240/4 block (which holds
    /// the broadcast address). IPv4-mapped IPv6 addresses are judged as IPv4.
    /// </summary>
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(IPAddress.IsLoopback(address)
                || address.Equals(IPAddress.IPv6Any)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || InRange(address, "fc00::", 7)
                || address.IsIPv6Multicast);
        }

        if (address.AddressFamily != AddressFamily.InterNetwork) return false;

        return !(InRange(address, "127.0.0.0", 8)
            || InRange(address, "0.0.0.0", 8)
            || InRange(address, "10.0.0.0", 8)
            || InRange(address, "172.16.0.0", 12)
            || InRange(address, "192.168.0.0", 16)
            || InRange(address, "169.254.0.0", 16)
            || InRange(address, "100.64.0.0", 10)
            || InRange(address, "224.0.0.0", 4)
            || InRange(address, "240.0.0.0", 4));
    }

    /// <summary>
    /// Builds the primary handler for the cover client: no automatic redirects, no
    /// cookies, and a connect callback that resolves the host, drops every
    /// non-public address and connects to the first remaining one, so the
    /// connection goes to the address that was checked.
    /// </summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        ConnectCallback = ConnectAsync,
    };

    /// <summary>The first public address in <paramref name="resolved"/>, or null when none is public.</summary>
    internal static IPAddress? FirstAllowedAddress(IPAddress[] resolved) =>
        resolved.FirstOrDefault(IsPublicAddress);

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var resolved = await Dns.GetHostAddressesAsync(host, cancellationToken);
        var address = FirstAllowedAddress(resolved)
            ?? throw new HttpRequestException($"Cover host {host} resolves only to blocked addresses");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch (OperationCanceledException)
        {
            socket.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            socket.Dispose();
            throw new HttpRequestException($"Cover host {host} could not be reached at {address}", ex);
        }
    }

    private static bool InRange(IPAddress address, string network, int prefixLength)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = IPAddress.Parse(network).GetAddressBytes();
        if (addressBytes.Length != networkBytes.Length) return false;

        var wholeBytes = prefixLength / 8;
        for (var i = 0; i < wholeBytes; i++)
        {
            if (addressBytes[i] != networkBytes[i]) return false;
        }

        var remainingBits = prefixLength % 8;
        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (addressBytes[wholeBytes] & mask) == (networkBytes[wholeBytes] & mask);
    }
}
