using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Quickstart.Tests;

public sealed class GreetingBehavior(QuickstartFixture fixture)
{
    [Fact]
    public async Task GreetingIsDurableAcrossItsHostingSiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>("welcome");

        await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));

        var first = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal("Hello, Ada.", first.Synapse.Message);

        await greeter.RestartHostAsync(cancellationToken);

        var committed = await greeter.Outgoing.ReadAsync<Greeted>(
            afterSequence: 0,
            cancellationToken);
        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
    }
}
