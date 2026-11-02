using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

// The csharp runtime hears plain sources through a session neuron: a signal published after the subscription is in its next batch.
public sealed class SessionDeliveryFacts(SessionSilo silo) : IClassFixture<SessionSilo>
{
    [Fact]
    public async Task ASignalPublishedAfterSubscribingArrivesInTheNextBatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = NeuronId.Parse("b1/probe.counter/session-delivery");
        var session = silo.Get<ISessionSynapses>("b1/runtime.session/session-delivery");
        await session.Subscribe(source, ["probe.counted"]);

        await silo.Get<ICounter>(source.ToString()).Add(3);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(TimeSpan.FromSeconds(20));
        SessionBatch? batch = null;
        while (batch is null && !limit.IsCancellationRequested) { batch = await session.Next(0, limit.Token); }

        Assert.NotNull(batch);
        Assert.Contains(batch.Signals, signal => signal.Source == source.ToString());
    }
}
