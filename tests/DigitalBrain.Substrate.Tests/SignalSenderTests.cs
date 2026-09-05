using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.mixed-ping")]
public sealed record MixedPing(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.recorded-fact")]
public sealed record RecordedFact(string Text) : Signal;

[GenerateSerializer]
[Alias("db.test.self-ping")]
public sealed record SelfPing(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IMixedBroadcaster")]
public interface IMixedBroadcaster : INeuron
{
    [Alias(nameof(Broadcast))]
    Task<int> Broadcast(NeuronId silent, string text);
}

[Alias("DigitalBrain.Substrate.Tests.IMixedPingSink")]
public interface IMixedPingSink : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IMixedPingSilent")]
public interface IMixedPingSilent : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IJournalProbe")]
public interface IJournalProbe : INeuron
{
    [Alias(nameof(Record))]
    Task Record(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IRecordedSink")]
public interface IRecordedSink : INeuron;

[Alias("DigitalBrain.Substrate.Tests.ISelfSender")]
public interface ISelfSender : INeuron
{
    [Alias(nameof(SendSelf))]
    Task<DeliveryOutcome> SendSelf(string text);
}

internal sealed class MixedBroadcaster : Neuron, IMixedBroadcaster
{
    private readonly IDurableDictionary<string, Synapse> _routes;

    public MixedBroadcaster(NeuronRuntime runtime)
        : base(runtime)
    {
        _routes = ServiceProvider.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>("synapses");
    }

    public async Task<int> Broadcast(NeuronId silent, string text)
    {
        _routes[NeuronSynapses.KeyFor(silent, nameof(MixedPing))] = new Synapse(
            Id,
            silent,
            nameof(MixedPing),
            weight: 0.4,
            TimeProvider.GetUtcNow(),
            SynapseKind.Discovered,
            fireCount: 3);
        await WriteStateAsync().ConfigureAwait(true);

        return await BroadcastAsync(new MixedPing(text))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }
}

internal sealed class MixedPingSink(NeuronRuntime runtime)
    : Neuron(runtime), IMixedPingSink, IHandle<MixedPing>
{
    public Task HandleAsync(MixedPing signal, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class MixedPingSilent(NeuronRuntime runtime) : Neuron(runtime), IMixedPingSilent;

internal sealed class JournalProbe(NeuronRuntime runtime) : Neuron(runtime), IJournalProbe
{
    public Task Record(string text) => RecordOutgoingAsync(new RecordedFact(text));
}

internal sealed class RecordedSink(NeuronRuntime runtime)
    : Neuron(runtime), IRecordedSink, IHandle<RecordedFact>
{
    public Task HandleAsync(RecordedFact signal, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

internal sealed class SelfSender(NeuronRuntime runtime)
    : Neuron(runtime), ISelfSender, IHandle<SelfPing>
{
    public async Task<DeliveryOutcome> SendSelf(string text)
        => (await SendAsync(Id, new SelfPing(text))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)).Outcome;

    public Task HandleAsync(SelfPing signal, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class SignalSenderTests
{
    [Fact]
    public async Task Broadcast_ReachesHandledAndUnhandledTargetsButLearnsOnlyHandled()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var sourceId = new NeuronId("mixedbroadcaster", owner, "broadcast");
        var silentId = new NeuronId("mixedpingsilent", owner, "default");
        var source = brain.Grains.GetGrain<IMixedBroadcaster>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var sinkId = new NeuronId("mixedpingsink", owner, "default");
        await brain.Grains.GetGrain<IMixedPingSink>(sinkId.ToGrainId())
            .HandleAsync(new Subscribe(sourceId, nameof(MixedPing)), TestContext.Current.CancellationToken);

        var reached = await source.Broadcast(silentId, "mixed");

        Assert.Equal(2, reached);
        var routes = await sourceQuery.ReadSynapses();
        var handled = Assert.Single(routes, route => route.Target == sinkId);
        var silent = Assert.Single(routes, route => route.Target == silentId);
        Assert.Equal(SynapseKind.Bound, handled.Kind);
        Assert.Equal(SynapseKind.Discovered, silent.Kind);
        Assert.Equal(1, handled.FireCount);
        Assert.Equal(3, silent.FireCount);
        Assert.Equal(0.4, silent.Weight, precision: 10);
        Assert.Single((await Query(
            brain,
            new NeuronId("mixedpingsink", owner, "default"))
            .ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Single((await Query(brain, silentId).ReadJournal(JournalKind.Incoming, 0)).Delta);
    }

    [Fact]
    public async Task RecordOutgoing_JournalsWithoutDeliveringOrLearning()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var probeId = new NeuronId("journalprobe", new OwnerId("owner"), "record");
        var journalProbe = brain.Grains.GetGrain<IJournalProbe>(probeId.ToGrainId());
        var journalProbeQuery = Query(brain, probeId);

        await journalProbe.Record("observed");

        Assert.Single((await journalProbeQuery.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        Assert.Empty(await journalProbeQuery.ReadSynapses());
        var sinkId = new NeuronId("recordedsink", probeId.Owner, "default");
        Assert.Empty((await Query(brain, sinkId).ReadJournal(JournalKind.Incoming, 0)).Delta);
    }

    [Fact]
    public async Task DirectedSend_ToSelfUsesLocalPath()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var senderId = new NeuronId("selfsender", new OwnerId("owner"), "loop");
        var selfSender = brain.Grains.GetGrain<ISelfSender>(senderId.ToGrainId());

        Assert.Equal(
            DeliveryOutcome.Handled,
            await selfSender.SendSelf("loopback").WaitAsync(
                TimeSpan.FromSeconds(5),
                TestContext.Current.CancellationToken));
        var query = Query(brain, senderId);
        var outgoing = Assert.Single((await query.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        var incoming = Assert.Single((await query.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(outgoing.SignalId, incoming.SignalId);
        Assert.Equal(senderId, Assert.Single(await Query(brain, senderId).ReadSynapses()).Target);
    }

    private static INeuronQuery Query(BrainSimulation brain, NeuronId id)
        => brain.Grains.GetGrain<INeuronQuery>(id.ToGrainId());

}
