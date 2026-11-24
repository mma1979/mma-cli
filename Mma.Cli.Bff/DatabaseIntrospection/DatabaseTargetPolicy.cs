using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

public interface IDnsHostResolver
{
    Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDnsHostResolver : IDnsHostResolver
{
    public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class DatabaseTargetPolicyOptions
{
    public IReadOnlyList<string> AllowedOrigins { get; init; } =
    [
        "http://localhost:5080",
        "http://127.0.0.1:5080",
        "http://[::1]:5080",
    ];

    public bool AllowLoopbackTargets { get; init; }

    public bool AllowPrivateNetworkTargets { get; init; }
}

public interface IDatabaseTargetPolicy
{
    void ValidateOriginAndHost(string? origin, string? requestHost);

    Task<ValidatedDatabaseTarget> ValidateAndResolveAsync(
        string host,
        int port,
        string? origin,
        string requestHost,
        CancellationToken cancellationToken);
}

public sealed partial class DatabaseTargetPolicy : IDatabaseTargetPolicy
{
    private readonly IDnsHostResolver _dnsResolver;
    private readonly HashSet<string> _allowedOrigins;
    private readonly bool _allowLoopbackTargets;
    private readonly bool _allowPrivateNetworkTargets;

    public DatabaseTargetPolicy(IDnsHostResolver dnsResolver, DatabaseTargetPolicyOptions options)
    {
        _dnsResolver = dnsResolver;
        _allowLoopbackTargets = options.AllowLoopbackTargets;
        _allowPrivateNetworkTargets = options.AllowPrivateNetworkTargets;
        _allowedOrigins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawOrigin in options.AllowedOrigins)
        {
            var normalized = NormalizeExactOrigin(rawOrigin);
            if (normalized is not null)
            {
                _allowedOrigins.Add(normalized);
            }
        }
    }

    public void ValidateOriginAndHost(string? origin, string? requestHost)
    {
        if (!IsAllowedLoopbackRequestHost(requestHost))
        {
            throw new DatabaseIntrospectionException("Request host is not allowed.", 403, "origin_not_allowed");
        }

        var normalizedOrigin = NormalizeExactOrigin(origin);
        if (normalizedOrigin is null || !_allowedOrigins.Contains(normalizedOrigin))
        {
            throw new DatabaseIntrospectionException("Request origin is not allowed.", 403, "origin_not_allowed");
        }
    }

    public async Task<ValidatedDatabaseTarget> ValidateAndResolveAsync(
        string host,
        int port,
        string? origin,
        string requestHost,
        CancellationToken cancellationToken)
    {
        ValidateOriginAndHost(origin, requestHost);

        if (port is < 1 or > 65535)
        {
            throw new DatabaseIntrospectionException("Invalid database port.", 400, "invalid_port");
        }

        var normalizedHost = host?.Trim().ToLowerInvariant() ?? string.Empty;
        if (string.IsNullOrEmpty(normalizedHost) || normalizedHost.Length > 253)
        {
            throw new DatabaseIntrospectionException("Invalid database host.", 400, "invalid_host");
        }

        var unbracketed = normalizedHost.StartsWith('[') && normalizedHost.EndsWith(']')
            ? normalizedHost[1..^1]
            : normalizedHost;

        if (IPAddress.TryParse(unbracketed, out var literalIp))
        {
            if (IsAddressForbiddenForPolicy(literalIp))
            {
                throw new DatabaseIntrospectionException("Forbidden database IP address.", 403, "target_not_allowed");
            }

            return new ValidatedDatabaseTarget(unbracketed, literalIp, port);
        }

        var hostWithoutTrailingDot = unbracketed.TrimEnd('.');
        if (!IsValidHostname(hostWithoutTrailingDot))
        {
            throw new DatabaseIntrospectionException("Malformed database hostname.", 400, "invalid_host");
        }

        if (IsHostnameForbiddenForPolicy(hostWithoutTrailingDot))
        {
            throw new DatabaseIntrospectionException("Forbidden local or internal hostname.", 403, "target_not_allowed");
        }

        IPAddress[] addresses;
        try
        {
            addresses = await _dnsResolver.GetHostAddressesAsync(hostWithoutTrailingDot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw new DatabaseIntrospectionException("DNS resolution timed out.", 504, "connection_timeout");
        }
        catch (Exception)
        {
            throw new DatabaseIntrospectionException("Could not resolve database host.", 502, "database_unreachable");
        }

        if (addresses.Length == 0)
        {
            throw new DatabaseIntrospectionException("Database host resolved to no addresses.", 502, "database_unreachable");
        }

        foreach (var address in addresses)
        {
            if (IsAddressForbiddenForPolicy(address))
            {
                throw new DatabaseIntrospectionException("Database host resolved to a forbidden address.", 403, "target_not_allowed");
            }
        }

        var selectedAddress = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
            ?? addresses[0];

        return new ValidatedDatabaseTarget(hostWithoutTrailingDot, selectedAddress, port);
    }

    private bool IsAddressForbiddenForPolicy(IPAddress address)
    {
        var normalized = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
        if (_allowLoopbackTargets && IPAddress.IsLoopback(normalized))
        {
            return false;
        }

        if (_allowPrivateNetworkTargets && IsPrivateNetworkAddress(normalized))
        {
            return false;
        }

        return IsForbiddenAddress(normalized);
    }

    private bool IsHostnameForbiddenForPolicy(string host)
    {
        if (_allowLoopbackTargets
            && (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return IsForbiddenHostname(host);
    }

    private static bool IsPrivateNetworkAddress(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            if (bytes.Length != 4) return false;
            byte a = bytes[0], b = bytes[1];
            return a == 10
                || (a == 172 && b >= 16 && b <= 31)
                || (a == 192 && b == 168);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC; // fc00::/7
        }

        return false;
    }

    public static bool IsForbiddenAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return IsForbiddenIpv4(address.MapToIPv4());
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsForbiddenIpv4(address),
            AddressFamily.InterNetworkV6 => IsForbiddenIpv6(address),
            _ => true,
        };
    }

    private static bool IsForbiddenIpv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            return true;
        }

        byte a = bytes[0], b = bytes[1], c = bytes[2];

        if (a == 0) return true;                                // 0.0.0.0/8 unspecified
        if (a == 10) return true;                               // 10.0.0.0/8 private
        if (a == 100 && b >= 64 && b <= 127) return true;       // 100.64.0.0/10 CGNAT
        if (a == 127) return true;                              // 127.0.0.0/8 loopback
        if (a == 169 && b == 254) return true;                  // 169.254.0.0/16 link-local / cloud metadata
        if (a == 172 && b >= 16 && b <= 31) return true;        // 172.16.0.0/12 private
        if (a == 192 && b == 0 && c == 0) return true;          // 192.0.0.0/24 IETF protocol assignments
        if (a == 192 && b == 0 && c == 2) return true;          // 192.0.2.0/24 TEST-NET-1
        if (a == 192 && b == 168) return true;                  // 192.168.0.0/16 private
        if (a == 198 && (b == 18 || b == 19)) return true;      // 198.18.0.0/15 benchmarking
        if (a == 198 && b == 51 && c == 100) return true;       // 198.51.100.0/24 TEST-NET-2
        if (a == 203 && b == 0 && c == 113) return true;        // 203.0.113.0/24 TEST-NET-3
        if (a >= 224) return true;                              // 224.0.0.0/4 multicast & 240.0.0.0/4 reserved/broadcast

        return false;
    }

    private static bool IsForbiddenIpv6(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal || address.IsIPv6Teredo)
        {
            return true;
        }

        var bytes = address.GetAddressBytes();
        if (bytes.Length != 16)
        {
            return true;
        }

        // Unique local fc00::/7
        if ((bytes[0] & 0xFE) == 0xFC) return true;

        // Link-local fe80::/10
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;

        // Multicast ff00::/8
        if (bytes[0] == 0xFF) return true;

        // Discard 100::/64
        if (bytes[0] == 0x01 && bytes[1] == 0x00 && bytes[2] == 0 && bytes[3] == 0
            && bytes[4] == 0 && bytes[5] == 0 && bytes[6] == 0 && bytes[7] == 0)
        {
            return true;
        }

        // Documentation 2001:db8::/32
        if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
        {
            return true;
        }

        // NAT64 well-known 64:ff9b::/96 and local-use 64:ff9b:1::/48
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
        {
            return true;
        }

        return false;
    }

    private static bool IsForbiddenHostname(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidHostname(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253)
        {
            return false;
        }

        var labels = host.Split('.');
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63 || !HostnameLabelRegex().IsMatch(label))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedLoopbackRequestHost(string? requestHost)
    {
        if (string.IsNullOrWhiteSpace(requestHost))
        {
            return false;
        }

        if (!Uri.TryCreate($"http://{requestHost.Trim()}", UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host;
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(host, out var ip))
        {
            return IPAddress.IsLoopback(ip);
        }

        return false;
    }

    private static string? NormalizeExactOrigin(string? rawOrigin)
    {
        if (string.IsNullOrWhiteSpace(rawOrigin))
        {
            return null;
        }

        var trimmed = rawOrigin.Trim();
        if (trimmed.Contains('*') || !Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }

        return uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.IgnoreCase)]
    private static partial Regex HostnameLabelRegex();
}
