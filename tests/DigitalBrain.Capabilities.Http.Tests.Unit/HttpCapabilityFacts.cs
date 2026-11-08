using System.Reflection;
using System.Text.Json;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Http;
using DigitalBrain.Contracts;

namespace DigitalBrain.Capabilities.Http.Tests.Unit;

public sealed class HttpCapabilityFacts
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The smallest provider: the allow-list check, then a canned answer. No network.
    private sealed class FakeHttp(HttpConnectionOptions options) : IHttpRead
    {
        public Task<JsonElement> GetJson(string url, CancellationToken ct)
        {
            HttpHosts.Require(new Uri(url), options.AllowedHosts);
            return Task.FromResult(JsonElement.Parse("""{"ok":true}"""));
        }

        public Task<string> GetText(string url, CancellationToken ct)
        {
            HttpHosts.Require(new Uri(url), options.AllowedHosts);
            return Task.FromResult("ok");
        }
    }

    [Fact]
    public async Task GetJsonRefusesAHostOutsideTheAllowList()
    {
        IHttpRead http = new FakeHttp(new HttpConnectionOptions(["api.example.org"]));

        var e = await Assert.ThrowsAsync<HostNotAllowedException>(() => http.GetJson("https://evil.test/x", Ct));

        Assert.Equal("evil.test", e.Host);
        Assert.True((await http.GetJson("https://API.example.org/x", Ct)).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void CapabilityIdsParse()
    {
        Assert.Equal(CapabilityId.Parse("http@1"), Capability.Http.V1);
        Assert.Equal("http@1", Capability.Http.V1Id);
        Assert.Equal(Capability.Http.V1, typeof(IHttpRead).GetCustomAttribute<CapabilityAttribute>()!.Id);
        Assert.Equal("http", typeof(IHttpRead).GetCustomAttribute<NeuronAliasAttribute>()!.Alias);
    }

    [Theory]
    [InlineData("https://api.example.org/a", true)]
    [InlineData("HTTP://Api.Example.ORG/a", true)]
    [InlineData("https://x.example.org/a", true)]
    [InlineData("https://a.b.example.org/a", true)]
    [InlineData("https://example.org/a", false)]
    [InlineData("https://notexample.org/a", false)]
    [InlineData("ftp://api.example.org/a", false)]
    public void WildcardsAndSchemes(string url, bool allowed)
    {
        string[] hosts = ["api.example.org", "*.example.org"];
        if (url.Contains("://x.", StringComparison.Ordinal) || url.Contains("a.b.", StringComparison.Ordinal)) { hosts = ["*.example.org"]; }
        if (allowed) { HttpHosts.Require(new Uri(url), hosts); }
        else { Assert.Throws<HostNotAllowedException>(() => HttpHosts.Require(new Uri(url), hosts)); }
    }
}
