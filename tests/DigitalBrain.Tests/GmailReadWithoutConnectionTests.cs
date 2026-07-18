using Brain.Contracts;
using DigitalBrain.Tests;
using Xunit;

namespace Brain.KernelTests;

public class GmailReadWithoutConnectionTests(BrainClusterFixture<ConnectorsKindsConfigurator> fixture)
    : BrainTest<ConnectorsKindsConfigurator>(fixture)
{
    [Fact]
    public async Task Read_without_connection_fails_closed()
    {
        var gmail = Neuron("gmail", $"reader-{Guid.NewGuid():N}");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            gmail.InvokeAsync(new("gmail.read.v1", "{}", "cmd-read", OwnerSession)));
        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
    }
}
