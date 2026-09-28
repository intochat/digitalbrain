using DigitalBrain.Salesforce;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SalesforceEndpointFacts
{
    [Fact]
    public void LocalProtocolFixtureRequiresAnExplicitLoopbackChoice()
    {
        var options = new SalesforceMcpOptions { Endpoint = "http://127.0.0.1:1234/mcp" };
        Assert.Throws<InvalidOperationException>(options.ResolveEndpoint);
        options.AllowLoopback = true;
        Assert.Equal(options.Endpoint, options.ResolveEndpoint()!.AbsoluteUri);
        options.Endpoint = "http://example.com/mcp";
        Assert.Throws<InvalidOperationException>(options.ResolveEndpoint);
    }
}