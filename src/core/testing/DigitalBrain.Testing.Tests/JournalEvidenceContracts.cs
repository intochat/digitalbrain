using DigitalBrain.Abstractions;
using DigitalBrain.Quickstart;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class JournalEvidenceContracts(TestingFixture fixture)
{
    [Fact(DisplayName = "Outgoing journal evidence carries Sequence, CorrelationId, Caller, Timestamp, and Direction")]
    public async Task OutgoingJournalEvidenceCarriesDeliveryMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>(TestingScenario.WelcomeGreeter);

        await test.Client.SendAsync<IGreeter>(greeter.Id.Name, new SayHello(TestingScenario.Guest));

        var observed = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);

        Assert.Equal(TestingScenario.GreetedMessage(TestingScenario.Guest), observed.Synapse.Message);
        Assert.NotEqual(default, observed.SynapseId);
        Assert.True(observed.Sequence > 0);
        Assert.NotEqual(default, observed.CorrelationId);
        Assert.NotEqual(Guid.Empty, observed.CorrelationId.Value);
        Assert.NotEqual(default, observed.Caller);
        Assert.Equal(greeter.Id, observed.Caller);
        Assert.NotEqual(default, observed.Timestamp);
        Assert.Equal(JournalKind.Outgoing, observed.Direction);
    }
}
