using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.ping")]
public sealed record Ping(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IPingSource")]
public interface IPingSource : INeuron
{
    [Alias(nameof(SendTo))]
    Task<DeliveryOutcome> SendTo(NeuronId target, string text);
}

[Alias("DigitalBrain.Substrate.Tests.IPingSink")]
public interface IPingSink : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IPingSilent")]
public interface IPingSilent : INeuron;

internal sealed class PingSource(NeuronRuntime runtime) : Neuron(runtime), IPingSource
{
    public async Task<DeliveryOutcome> SendTo(NeuronId target, string text)
        => (await SendAsync(target, new Ping(text))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext)).Outcome;
}

internal sealed class PingSink(NeuronRuntime runtime) : Neuron(runtime), IPingSink, IHandle<Ping>
{
    public Task HandleAsync(Ping signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PingSilent(NeuronRuntime runtime) : Neuron(runtime), IPingSilent;

public sealed class SynapseSetTests
{
    [Fact]
    public async Task ConfiguredClock_StampsOutgoingDelivery()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero));
        await using var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([]),
            ConfigureSilo = silo => silo.Services.AddSingleton<TimeProvider>(clock),
        });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "clock");
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "clock");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var query = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());

        await source.SendTo(sinkId, "timestamp");

        var delivery = Assert.Single((await query.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        Assert.Equal(clock.GetUtcNow(), delivery.Timestamp);
    }

    [Fact]
    public async Task DistinctNeuronActivations_DoNotShareRuntimeBoundState()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var owner = new OwnerId("owner");
        var firstId = new NeuronId("pingsource", owner, "isolated-a");
        var secondId = new NeuronId("pingsource", owner, "isolated-b");
        var firstTarget = new NeuronId("pingsink", owner, "isolated-a");
        var secondTarget = new NeuronId("pingsink", owner, "isolated-b");

        await brain.Grains.GetGrain<IPingSource>(firstId.ToGrainId())
            .SendTo(firstTarget, "first");
        await brain.Grains.GetGrain<IPingSource>(secondId.ToGrainId())
            .SendTo(secondTarget, "second");

        var first = brain.Grains.GetGrain<INeuronQuery>(firstId.ToGrainId());
        var second = brain.Grains.GetGrain<INeuronQuery>(secondId.ToGrainId());
        var firstSnapshot = Assert.IsType<JournalSnapshot>(
            (await first.ReadJournal(JournalKind.Outgoing, long.MaxValue)).ResetSnapshot);
        var secondSnapshot = Assert.IsType<JournalSnapshot>(
            (await second.ReadJournal(JournalKind.Outgoing, long.MaxValue)).ResetSnapshot);

        Assert.Equal(
            (1L, 1L, 1),
            (firstSnapshot.TotalRecorded, firstSnapshot.LastSequence, firstSnapshot.RetainedCount));
        Assert.Equal(
            (1L, 1L, 1),
            (secondSnapshot.TotalRecorded, secondSnapshot.LastSequence, secondSnapshot.RetainedCount));
        Assert.Equal(firstTarget, Assert.Single(await first.ReadSynapses()).Target);
        Assert.Equal(secondTarget, Assert.Single(await second.ReadSynapses()).Target);
    }

    [Fact]
    public async Task DirectedSend_HandledTargetReturnsHandledAndLearns()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "a");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "b");

        var outcome = await source.SendTo(sinkId, "one");

        Assert.Equal(DeliveryOutcome.Handled, outcome);
        var outgoing = Assert.Single((await sourceQuery.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        var incoming = Assert.Single((await brain.Grains.GetGrain<INeuronQuery>(sinkId.ToGrainId())
            .ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(outgoing.SignalId, incoming.SignalId);
        var synapse = Assert.Single(await sourceQuery.ReadSynapses());
        Assert.Equal(sinkId, synapse.Target);
        Assert.Equal(nameof(Ping), synapse.SignalType);
        Assert.Equal(SynapseKind.Learned, synapse.Kind);
        Assert.Equal(0.65, synapse.Weight, precision: 10);
        Assert.Equal(1, synapse.FireCount);
    }

    [Fact]
    public async Task DirectedSend_HandlerlessTargetReturnsUnhandledAndDoesNotLearn()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var owner = new OwnerId("owner");
        var sourceId = new NeuronId("pingsource", owner, "unhandled");
        var silentId = new NeuronId("pingsilent", owner, "ignored");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var silentQuery = brain.Grains.GetGrain<INeuronQuery>(silentId.ToGrainId());

        var outcome = await source.SendTo(silentId, "ignored");

        Assert.Equal(DeliveryOutcome.Unhandled, outcome);
        Assert.Empty(await sourceQuery.ReadSynapses());
        var outgoing = Assert.Single((await sourceQuery.ReadJournal(JournalKind.Outgoing, 0)).Delta);
        var incoming = Assert.Single((await silentQuery.ReadJournal(JournalKind.Incoming, 0)).Delta);
        Assert.Equal(outgoing.SignalId, incoming.SignalId);
    }

    [Fact]
    public async Task SecondFire_PotentiatesTheSameSynapseRatherThanAddingOne()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "c");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "d");

        await source.SendTo(sinkId, "one");
        await source.SendTo(sinkId, "two");

        var synapse = Assert.Single(await sourceQuery.ReadSynapses());
        Assert.Equal(0.755, synapse.Weight, precision: 10);
        Assert.Equal(2, synapse.FireCount);
    }

    [Fact]
    public async Task DistinctTargets_GetDistinctSynapses()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var sourceId = new NeuronId("pingsource", new OwnerId("owner"), "e");
        var source = brain.Grains.GetGrain<IPingSource>(sourceId.ToGrainId());
        var sourceQuery = brain.Grains.GetGrain<INeuronQuery>(sourceId.ToGrainId());

        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "f"), "one");
        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "g"), "two");

        Assert.Equal(2, (await sourceQuery.ReadSynapses()).Count);
    }
}
