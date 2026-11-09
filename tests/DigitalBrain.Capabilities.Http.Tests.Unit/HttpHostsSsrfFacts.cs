using System.Net;
using DigitalBrain.Capabilities.Http;

namespace DigitalBrain.Capabilities.Http.Tests.Unit;

public sealed class HttpHostsSsrfFacts
{
    private static readonly string[] PublicAllowedHosts =
    [
        "api.x.com",
        "api.github.com",
        "hacker-news.firebaseio.com",
        "api.exchangerate.host",
        "*.example.org"
    ];

    [Theory]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://127.0.0.1:8080/admin")]
    [InlineData("https://127.0.0.254/status")]
    [InlineData("http://localhost/")]
    [InlineData("http://localhost:5000/api")]
    [InlineData("http://10.0.0.1/")]
    [InlineData("http://10.255.255.255/internal")]
    [InlineData("http://172.16.0.1/credentials")]
    [InlineData("http://172.31.255.254/env")]
    [InlineData("http://192.168.0.1/")]
    [InlineData("http://192.168.1.100/config")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.1.1/linklocal")]
    [InlineData("http://[::1]/")]
    [InlineData("http://[::1]:9000/metrics")]
    [InlineData("http://[fe80::1]/")]
    [InlineData("http://[fe80::dead:beef]/")]
    [InlineData("http://[fc00::1]/")]
    [InlineData("http://[fd00::1]/")]
    [InlineData("http://0.0.0.0/")]
    public void VettingBlocksPrivateLoopbackAndLinkLocalAddresses(string privateUrl)
    {
        var uri = new Uri(privateUrl);

        var ex = Assert.Throws<HostNotAllowedException>(() => HttpHosts.Require(uri, PublicAllowedHosts));

        Assert.NotNull(ex.Host);
        Assert.DoesNotContain("credentials", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meta-data", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://api.x.com/2/users/elonmusk/mentions")]
    [InlineData("https://api.github.com/repos/dotnet/orleans/issues")]
    [InlineData("https://hacker-news.firebaseio.com/v0/topstories.json")]
    [InlineData("https://api.exchangerate.host/latest?base=EUR")]
    [InlineData("https://service.example.org/v1/health")]
    public void VettingAllowsValidPublicDomainsWhenPermitted(string publicUrl)
    {
        var uri = new Uri(publicUrl);

        // Should not throw
        HttpHosts.Require(uri, PublicAllowedHosts);
    }

    [Theory]
    [InlineData("https://evil.attacker.com/steal")]
    [InlineData("https://api.x.com.evil.com/fake")]
    [InlineData("https://subdomain.api.x.com/")]
    [InlineData("https://example.org/")] // Suffix alone does not match *.example.org
    public void VettingRejectsUnlistedPublicDomains(string untrustedUrl)
    {
        var uri = new Uri(untrustedUrl);

        Assert.Throws<HostNotAllowedException>(() => HttpHosts.Require(uri, PublicAllowedHosts));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("127.0.0.2", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("10.254.0.1", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.24.1.1", false)]
    [InlineData("172.31.255.255", false)]
    [InlineData("192.168.0.1", false)]
    [InlineData("192.168.100.50", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("1.1.1.1", true)]
    [InlineData("140.82.121.4", true)] // GitHub IP
    [InlineData("104.244.42.1", true)] // X.com IP
    public void VettingCategorizesIpv4AddressesAccurately(string ipString, bool expectedPublic)
    {
        var ip = IPAddress.Parse(ipString);
        var isPublic = IsPublicAddress(ip);

        Assert.Equal(expectedPublic, isPublic);
    }

    [Theory]
    [InlineData("::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fe80::215:5dff:fe1a:e1f3", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fd12:3456:789a:1::1", false)]
    [InlineData("2606:4700:4700::1111", true)] // Cloudflare Public IPv6
    [InlineData("2001:4860:4860::8888", true)] // Google Public IPv6
    public void VettingCategorizesIpv6AddressesAccurately(string ipString, bool expectedPublic)
    {
        var ip = IPAddress.Parse(ipString);
        var isPublic = IsPublicAddress(ip);

        Assert.Equal(expectedPublic, isPublic);
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) { address = address.MapToIPv4(); }
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) { return false; }
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
                || (bytes[0] == 198 && (bytes[1] & 0xFE) == 18)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
        }
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC);
        }
        return false;
    }
}
