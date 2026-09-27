using DigitalBrain.Core;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DigitalBrainClientFacts
{
    [Fact]
    public void KeepsIpGatewaysAndResolvesHostNames()
    {
        var gateways = DigitalBrainClient.GatewayEndpoints("gwy.tcp://10.0.0.5:30000/0; gwy.tcp://localhost:30001/0");

        Assert.Equal("gwy.tcp://10.0.0.5:30000/0", gateways[0].ToString());
        Assert.Equal("gwy.tcp://127.0.0.1:30001/0", gateways[1].ToString());
    }

    [Fact]
    public async Task RequiresGatewaysUnlessLocalDevelopment()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DigitalBrainClient.ConnectAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("LocalDevelopment", error.Message, StringComparison.Ordinal);
    }
}
