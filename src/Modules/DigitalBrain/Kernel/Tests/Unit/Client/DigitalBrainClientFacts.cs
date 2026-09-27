using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DigitalBrainClientFacts
{
    [Fact]
    public async Task RequiresGatewaysUnlessLocalDevelopment()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DigitalBrainClient.ConnectAsync([], TestContext.Current.CancellationToken));

        Assert.Contains("LocalDevelopment", error.Message, StringComparison.Ordinal);
    }
}
