using DigitalBrain.TestingTests.Harness;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class TestOwnerContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "TestOwner exposes public Id and Client for isolated owners")]
    public async Task TestOwnerExposesPublicIdAndClient()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var other = test.Owner(TestingScenario.OtherOwner);

        Assert.False(string.IsNullOrWhiteSpace(other.Id.Value));
        Assert.NotEqual(test.Client.Owner, other.Id);
        Assert.Equal(other.Id, other.Client.Owner);

        var greeter = other.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);
        await other.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest), cancellationToken);

        var greeted = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), greeted.Synapse.Message);
        Assert.Equal(other.Id, greeter.Id.Owner);
    }
}
