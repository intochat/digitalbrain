using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Tests;

[GenerateSerializer]
[Alias("db.test.tally-state")]
public sealed record TallyState([property: Id(0)] int Tally);

[Alias("db.test.announcing")]
public interface IAnnouncing : IGrainWithStringKey
{
    [Alias(nameof(ReadTally))]
    Task<int> ReadTally();

    [Alias(nameof(ReadStoredAnnouncements))]
    Task<int> ReadStoredAnnouncements();
}

[GrainType("announcing")]
internal sealed class AnnouncingNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<TallyState>> state)
    : Neuron<TallyState>(runtime, state), IAnnouncing
{
    public Task<int> ReadTally() => Task.FromResult(State?.Tally ?? 0);

    public Task<int> ReadStoredAnnouncements() => Task.FromResult(StoredAnnouncementCount);

    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Signal.Type != "Ping")
        {
            return Task.CompletedTask;
        }

        var tally = (State?.Tally ?? 0) + 1;
        Announce(Signal.Create("Pong", "{\"n\":" + tally + "}"), correlation: delivery.CorrelationId);
        return SaveAsync(new TallyState(tally), cancellationToken);
    }
}

[GrainType("holding")]
internal sealed class HoldingNeuron(NeuronRuntime runtime) : Neuron(runtime)
{
    protected override Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        // A blocked reaction would also block the Fire the scenario makes from this same neuron.
        if (FixtureSwitches.HeldQueues.ContainsKey(Id.Name))
        {
            throw new InvalidOperationException("The pending queue is held.");
        }

        return Task.CompletedTask;
    }
}

internal sealed class FixtureReactionCrashPoint : IReactionCrashPoint
{
    internal static ConcurrentDictionary<string, byte> LoseActivationOnce { get; } = new(StringComparer.Ordinal);

    public void AfterSnapshotSave(NeuronId neuron)
    {
        if (LoseActivationOnce.TryRemove(neuron.ToString(), out _))
        {
            // Simulates activation loss after the snapshot commits but before the pending head persists.
            throw new NeuronPersistenceException(neuron, "simulated lost activation after the snapshot save",
                new IOException("activation lost"));
        }
    }
}

internal sealed class FixtureDeliveryFaultFilter : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.InterfaceMethod.DeclaringType == typeof(INeuron)
            && context.InterfaceMethod.Name == nameof(INeuron.Deliver)
            && context.Grain is Neuron neuron
            && FixtureSwitches.DeliveryFailuresLeft.TryGetValue(neuron.Id.Name, out var left)
            && left > 0
            && FixtureSwitches.DeliveryFailuresLeft.AddOrUpdate(neuron.Id.Name, 0, (_, remaining) => remaining - 1) >= 0)
        {
            throw new TimeoutException("simulated delivery failure");
        }

        await context.Invoke();
    }
}
