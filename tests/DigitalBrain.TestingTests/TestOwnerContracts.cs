using DigitalBrain.Quickstart;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class TestOwnerContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "TestOwner exposes public Id and Client for isolated owners")]
    public async Task TestOwnerExposesPublicIdAndClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var other = test.Owner("other");

        Assert.False(string.IsNullOrWhiteSpace(other.Id.Value));
        Assert.NotEqual(test.Client.Owner, other.Id);
        Assert.Equal(other.Id, other.Client.Owner);

        var greeter = other.Neuron<IGreeter>("welcome");
        await other.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));

        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal("Hello, Ada.", greeted.Synapse.Message);
        Assert.Equal(other.Id, greeter.Id.Owner);
    }
}
