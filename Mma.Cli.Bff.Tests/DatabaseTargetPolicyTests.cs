using System.Net;
using System.Net.Sockets;
using Xunit;

public sealed class DatabaseTargetPolicyTests
{
    private const string AllowedOrigin = "http://localhost:5080";
    private const string AllowedHost = "localhost:5080";

    private static DatabaseTargetPolicy CreatePolicy(
        Func<string, IPAddress[]>? dnsLookup = null,
        IReadOnlyList<string>? allowedOrigins = null,
        bool allowLoopbackTargets = false,
        bool allowPrivateNetworkTargets = false)
    {
        var resolver = new FakeDnsResolver(dnsLookup ?? (_ => [IPAddress.Parse("93.184.216.34")]));
        var options = new DatabaseTargetPolicyOptions
        {
            AllowedOrigins = allowedOrigins ?? [AllowedOrigin, "http://localhost:5173"],
            AllowLoopbackTargets = allowLoopbackTargets,
            AllowPrivateNetworkTargets = allowPrivateNetworkTargets,
        };
        return new DatabaseTargetPolicy(resolver, options);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("::ffff:169.254.169.254")]
    public async Task Forbidden_ip_literals_are_rejected_with_403_target_not_allowed(string ip)
    {
        var policy = CreatePolicy();
        var ex = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync(ip, 5432, AllowedOrigin, AllowedHost, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("target_not_allowed", ex.Code);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("db.localhost")]
    [InlineData("mybox.local")]
    [InlineData("metadata.google.internal")]
    public async Task Forbidden_hostname_suffixes_are_rejected_with_403_target_not_allowed(string host)
    {
        var dnsCalled = false;
        var policy = CreatePolicy(_ =>
        {
            dnsCalled = true;
            return [IPAddress.Parse("93.184.216.34")];
        });

        var ex = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync(host, 5432, AllowedOrigin, AllowedHost, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("target_not_allowed", ex.Code);
        Assert.False(dnsCalled);
    }

    [Fact]
    public async Task Mixed_public_and_private_dns_answers_are_rejected()
    {
        var policy = CreatePolicy(_ =>
        [
            IPAddress.Parse("93.184.216.34"),
            IPAddress.Parse("10.0.0.42"),
        ]);

        var ex = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("rebind.example.com", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("target_not_allowed", ex.Code);
    }

    [Fact]
    public async Task Public_dns_answers_return_validated_target_with_original_host_and_vetted_ip()
    {
        var publicIp = IPAddress.Parse("93.184.216.34");
        var policy = CreatePolicy(_ => [publicIp]);

        var target = await policy.ValidateAndResolveAsync("DB.Example.COM", 5432, "http://localhost:5173", AllowedHost, CancellationToken.None);

        Assert.Equal("db.example.com", target.OriginalHost);
        Assert.Equal(publicIp, target.Address);
        Assert.Equal(5432, target.Port);
    }

    [Theory]
    [InlineData("bad host")]
    [InlineData("http://db.example.com")]
    [InlineData("-invalid.example.com")]
    [InlineData("bad..example.com")]
    public async Task Malformed_hostnames_return_400_invalid_host(string host)
    {
        var policy = CreatePolicy();
        var ex = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync(host, 5432, AllowedOrigin, AllowedHost, CancellationToken.None));

        Assert.Equal(400, ex.StatusCode);
        Assert.Equal("invalid_host", ex.Code);
    }

    [Fact]
    public async Task Empty_or_failed_dns_lookup_returns_502_database_unreachable()
    {
        var emptyPolicy = CreatePolicy(_ => []);
        var emptyEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => emptyPolicy.ValidateAndResolveAsync("empty.example.com", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(502, emptyEx.StatusCode);
        Assert.Equal("database_unreachable", emptyEx.Code);

        var throwingPolicy = CreatePolicy(_ => throw new SocketException());
        var throwEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => throwingPolicy.ValidateAndResolveAsync("nx.example.com", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(502, throwEx.StatusCode);
        Assert.Equal("database_unreachable", throwEx.Code);
    }

    [Theory]
    [InlineData(null, AllowedHost)]
    [InlineData("", AllowedHost)]
    [InlineData("https://evil.example.com", AllowedHost)]
    [InlineData("http://localhost:9999", AllowedHost)]
    [InlineData("*", AllowedHost)]
    [InlineData(AllowedOrigin, "evil.example.com:5080")]
    [InlineData(AllowedOrigin, "192.168.1.50:5080")]
    public async Task Missing_or_untrusted_origin_or_host_returns_403_origin_not_allowed(string? origin, string requestHost)
    {
        var policy = CreatePolicy(allowedOrigins: [AllowedOrigin, "*"]);
        var ex = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("db.example.com", 5432, origin, requestHost, CancellationToken.None));

        Assert.Equal(403, ex.StatusCode);
        Assert.Equal("origin_not_allowed", ex.Code);
    }

    [Fact]
    public async Task AllowLoopbackTargets_permits_localhost_and_loopback_while_blocking_metadata_and_private()
    {
        var policy = CreatePolicy(
            dnsLookup: _ => [IPAddress.Loopback, IPAddress.IPv6Loopback],
            allowLoopbackTargets: true);

        var localhostTarget = await policy.ValidateAndResolveAsync("localhost", 5432, AllowedOrigin, AllowedHost, CancellationToken.None);
        Assert.Equal("localhost", localhostTarget.OriginalHost);
        Assert.Equal(IPAddress.Loopback, localhostTarget.Address);

        var ipv4Target = await policy.ValidateAndResolveAsync("127.0.0.1", 1433, AllowedOrigin, AllowedHost, CancellationToken.None);
        Assert.Equal(IPAddress.Loopback, ipv4Target.Address);

        var ipv6Target = await policy.ValidateAndResolveAsync("::1", 5432, AllowedOrigin, AllowedHost, CancellationToken.None);
        Assert.Equal(IPAddress.IPv6Loopback, ipv6Target.Address);

        var metadataEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("169.254.169.254", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(403, metadataEx.StatusCode);

        var privateEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("10.0.0.5", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(403, privateEx.StatusCode);
    }

    [Fact]
    public async Task AllowPrivateNetworkTargets_permits_rfc1918_while_blocking_metadata_and_loopback()
    {
        var policy = CreatePolicy(allowPrivateNetworkTargets: true);

        var rfc1918Target = await policy.ValidateAndResolveAsync("192.168.1.25", 5432, AllowedOrigin, AllowedHost, CancellationToken.None);
        Assert.Equal(IPAddress.Parse("192.168.1.25"), rfc1918Target.Address);

        var metadataEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("169.254.169.254", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(403, metadataEx.StatusCode);

        var loopbackEx = await Assert.ThrowsAsync<DatabaseIntrospectionException>(
            () => policy.ValidateAndResolveAsync("127.0.0.1", 5432, AllowedOrigin, AllowedHost, CancellationToken.None));
        Assert.Equal(403, loopbackEx.StatusCode);
    }

    private sealed class FakeDnsResolver(Func<string, IPAddress[]> resolver) : IDnsHostResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) =>
            Task.FromResult(resolver(host));
    }
}
