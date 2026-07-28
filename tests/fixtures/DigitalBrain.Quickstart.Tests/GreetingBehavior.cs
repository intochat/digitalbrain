using Xunit;

namespace DigitalBrain.Quickstart.Tests;

public sealed class GreetingBehavior(QuickstartFixture fixture)
{
    [Fact(DisplayName =
        "Sample IGreeter SayHello journals Greeted that survives host silo restart (not Behavior install rail)")]
    public async Task GreetingIsDurableAcrossItsHostingSiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>();
        const string guest = "Ada";

        await test.Client.SendAsync(greeter.Id, new SayHello(guest));

        var first = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal($"Hello, {guest}.", first.Synapse.Message);

        await greeter.RestartHostAsync(cancellationToken);

        var committed = await greeter.Outgoing.ReadAsync<Greeted>(afterSequence: 0, cancellationToken);
        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
    }
}
