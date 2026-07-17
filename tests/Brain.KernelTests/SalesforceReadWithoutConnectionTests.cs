using Brain.Contracts;
using Brain.Modules.Sdk;
using Xunit;

namespace Brain.KernelTests;

public class SalesforceReadWithoutConnectionTests(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : BrainTest<ConnectorsKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Read_without_connection_fails_closed()
    {
        var salesforce = Neuron("salesforce", $"reader-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            salesforce.InvokeAsync(new("salesforce.read.v1", """{"query":"SELECT Id FROM Account"}""", "cmd-read", OwnerSession)));
        Assert.Equal(BrainErrors.ConnectionUnhealthy, exception.Code);
    }
}
