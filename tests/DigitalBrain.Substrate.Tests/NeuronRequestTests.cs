using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Orleans.Runtime;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.nested-request")]
public sealed record NestedRequest(NeuronId Target) : Signal<ProbeResponse>;

[GenerateSerializer]
[Alias("db.test.probe-request")]
public sealed record ProbeRequest(string Text, string Mode, int DelayMilliseconds = 0) : Signal<ProbeResponse>;

[GenerateSerializer]
[Alias("db.test.probe-response")]
public sealed record ProbeResponse(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.probe-noise")]
public sealed record ProbeNoise : Signal;

[Alias("DigitalBrain.Substrate.Tests.IRequestSource")]
public interface IRequestSource : INeuron, IHandle<NestedRequest>
{
    [Alias(nameof(Request))]
    Task<string> Request(NeuronId target, ProbeRequest request, int timeoutMilliseconds = 5000);
}

[Alias("DigitalBrain.Substrate.Tests.IRequestTarget")]
public interface IRequestTarget : INeuron, IHandle<ProbeRequest>;

[GrainType("requestsource")]
internal sealed class RequestSource(NeuronRuntime runtime) : Neuron(runtime), IRequestSource
{
    public async Task<string> Request(NeuronId target, ProbeRequest request, int timeoutMilliseconds = 5000)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMilliseconds));
        return (await RequestAsync(target, request, timeout.Token).ConfigureAwait(true)).Text;
    }

    public async Task HandleAsync(NestedRequest signal, CancellationToken cancellationToken)
    {
        var first = await RequestAsync(signal.Target, new ProbeRequest("first", "noise"), cancellationToken)
            .ConfigureAwait(true);
        var second = await RequestAsync(signal.Target, new ProbeRequest("second", "noise"), cancellationToken)
            .ConfigureAwait(true);
        await ReplyAsync(new ProbeResponse($"{first.Text},{second.Text}")).ConfigureAwait(true);
    }
}

[GrainType("requesttarget")]
internal sealed class RequestTarget(NeuronRuntime runtime)
    : Neuron(runtime), IRequestTarget, IHandle<ProbeNoise>
{
    public async Task HandleAsync(ProbeRequest signal, CancellationToken cancellationToken)
    {
        if (signal.DelayMilliseconds > 0)
        {
            await Task.Delay(signal.DelayMilliseconds,
                    signal.Mode == "ignore-cancellation" ? CancellationToken.None : cancellationToken)
                .ConfigureAwait(true);
        }

        if (signal.Mode == "missing")
        {
            return;
        }

        if (signal.Mode == "noise")
        {
            await SendAsync(Id, new ProbeNoise(), cancellationToken).ConfigureAwait(true);
        }

        if (signal.Mode == "cycle")
        {
            // The source remains busy awaiting this delivery. The request-path guard
            // must reject before making a remote request back into that activation.
            await RequestAsync(new NeuronId("requestsource", Id.Owner, signal.Text),
                    new NestedRequest(Id), cancellationToken)
                .ConfigureAwait(true);
        }

        await ReplyAsync(new ProbeResponse(signal.Text)).ConfigureAwait(true);
        if (signal.Mode == "compact")
        {
            // Two bounded entries exceed the retained-byte limit and evict the reply.
            await RecordOutgoingAsync(new RecordedFact(new string('x', 300_000))).ConfigureAwait(true);
            await RecordOutgoingAsync(new RecordedFact(new string('y', 300_000))).ConfigureAwait(true);
        }
    }

    public Task HandleAsync(ProbeNoise signal, CancellationToken cancellationToken)
        => ReplyAsync(new ProbeResponse("unrelated"));
}

public sealed class NeuronRequestTests
{
    [Fact]
    public async Task NestedRequest_UsesTargetJournalAndExactCausationAcrossRepeatedCalls()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var source = simulation.Brain.Get<IRequestSource>("nested");
        var target = new NeuronId("requesttarget", source.Id.Owner, "nested");

        var response = await source.RequestAsync(new NestedRequest(target), TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.Equal("first,second", response.Text);
        var route = Assert.Single(await Query(simulation, source.Id).ReadSynapses());
        Assert.Equal(target, route.Target);
        Assert.Equal(nameof(ProbeRequest), route.SignalType);
        Assert.Equal(SynapseKind.Learned, route.Kind);
        Assert.Equal(2, route.FireCount);
        var requests = (await Query(simulation, source.Id).ReadJournal(JournalKind.Outgoing, 0))
            .Delta.Where(delivery => delivery.Signal is ProbeRequest).ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(requests[0].CorrelationId, requests[1].CorrelationId);
        Assert.NotEqual(requests[0].SignalId, requests[1].SignalId);
        var replies = (await Query(simulation, target).ReadJournal(JournalKind.Outgoing, 0))
            .Delta.Where(delivery => delivery.Signal is ProbeResponse).ToArray();
        Assert.Equal(4, replies.Length);
        foreach (var request in requests)
        {
            var reply = Assert.Single(replies, delivery => delivery.CausationId == request.SignalId);
            Assert.Equal(((ProbeRequest)request.Signal).Text, ((ProbeResponse)reply.Signal).Text);
        }
    }

    [Theory]
    [InlineData("missing", "without recording")]
    [InlineData("compact", "compacted")]
    public async Task HandledRequestWithoutRetainedReply_FailsExplicitly(string mode, string error)
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var (sourceId, source, targetId) = Actors(simulation, mode);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.Request(targetId, new ProbeRequest("reply", mode)));

        Assert.Contains(error, failure.Message, StringComparison.Ordinal);
        Assert.Single(await Query(simulation, sourceId).ReadSynapses());
    }

    [Fact]
    public async Task UnhandledRequest_FailsWithoutLearning()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var (sourceId, source, _) = Actors(simulation, "ignored");
        var silent = new NeuronId("mixedpingsilent", sourceId.Owner, "ignored");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.Request(silent, new ProbeRequest("ignored", "normal")));

        Assert.Contains("did not handle", failure.Message, StringComparison.Ordinal);
        Assert.Empty(await Query(simulation, sourceId).ReadSynapses());
    }

    [Fact]
    public async Task CancelledRemoteRequest_DoesNotReinforceAfterLateCompletion()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var (sourceId, source, targetId) = Actors(simulation, "late");
        // Warm both activations so the deadline tests delivery, not silo startup.
        await Query(simulation, targetId).ReadJournal(JournalKind.Outgoing, 0);
        await Query(simulation, sourceId).ReadJournal(JournalKind.Outgoing, 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.Request(
            targetId, new ProbeRequest("late", "ignore-cancellation", 900), 150));
        Assert.Empty(await Query(simulation, sourceId).ReadSynapses());

        // A second call is serialized behind the still-running remote handler. Its
        // completion proves the late response has happened before the final assertion.
        Assert.Equal("current", await source.Request(targetId, new ProbeRequest("current", "normal")));
        var route = Assert.Single(await Query(simulation, sourceId).ReadSynapses());
        Assert.Equal(1, route.FireCount);
        var targetJournal = await Query(simulation, targetId).ReadJournal(JournalKind.Outgoing, 0);
        Assert.Contains(targetJournal.Delta, delivery => delivery.Signal is ProbeResponse { Text: "late" });
        Assert.Contains(targetJournal.Delta, delivery => delivery.Signal is ProbeResponse { Text: "current" });
    }

    [Fact]
    public async Task Cancellation_ReachesRemoteHandler()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var (sourceId, source, targetId) = Actors(simulation, "cancel");
        await Query(simulation, targetId).ReadJournal(JournalKind.Outgoing, 0);
        await Query(simulation, sourceId).ReadJournal(JournalKind.Outgoing, 0);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.Request(
            targetId, new ProbeRequest("cancelled", "normal", 5000), 150));
        Assert.Equal("after", await source.Request(targetId, new ProbeRequest("after", "normal"))
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        Assert.DoesNotContain((await Query(simulation, targetId).ReadJournal(JournalKind.Outgoing, 0)).Delta,
            delivery => delivery.Signal is ProbeResponse { Text: "cancelled" });
    }

    [Fact]
    public async Task RequestCyclesAndForeignOwners_FailBeforeDelivery()
    {
        await using var simulation = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var (sourceId, source, targetId) = Actors(simulation, "cycle");
        var self = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.Request(sourceId, new ProbeRequest("self", "normal")));
        Assert.Contains("itself", self.Message, StringComparison.Ordinal);

        var cycle = await Assert.ThrowsAsync<InvalidOperationException>(
            () => source.Request(targetId, new ProbeRequest(sourceId.Name, "cycle")));
        Assert.Contains("cycle", cycle.Message, StringComparison.Ordinal);

        await Assert.ThrowsAsync<NeuronAuthorizationException>(() => source.Request(
            new NeuronId("requesttarget", new OwnerId("foreign"), "private"), new ProbeRequest("private", "normal")));
        Assert.Empty(await Query(simulation, sourceId).ReadSynapses());

        // Failure restores request context; an independent next request still succeeds.
        Assert.Equal("recovered", await source.Request(targetId, new ProbeRequest("recovered", "normal")));
    }

    private static (NeuronId SourceId, IRequestSource Source, NeuronId TargetId) Actors(
        BrainSimulation simulation, string name)
    {
        var owner = new OwnerId("owner");
        var source = new NeuronId("requestsource", owner, name);
        return (source, simulation.Grains.GetGrain<IRequestSource>(source.ToGrainId()),
            new NeuronId("requesttarget", owner, name));
    }

    private static INeuronQuery Query(BrainSimulation simulation, NeuronId neuron)
        => simulation.Grains.GetGrain<INeuronQuery>(neuron.ToGrainId());
}
