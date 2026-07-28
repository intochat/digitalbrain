using DigitalBrain.Quickstart;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class RestartHostContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "RestartHostAsync preserves committed journal records for the target neuron")]
    public async Task RestartHostPreservesCommittedJournalRecords()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);

        await test.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest));

        var first = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), first.Synapse.Message);

        await greeter.RestartHostAsync(cancellationToken);

        var committed = await greeter.Outgoing.ReadAsync<Greeted>(afterSequence: 0, cancellationToken);
        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
        Assert.Equal(first.Sequence, committed[0].Sequence);
    }
}
