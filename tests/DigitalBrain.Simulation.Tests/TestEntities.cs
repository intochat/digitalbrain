using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Simulation.Tests;

[Alias("test.counter")]
public interface ICounterEntity : IEntity<CounterState>
{
    [Alias(nameof(Add))]
    Task Add(int amount);

}

[GenerateSerializer]
[Alias("test.counter-state")]
public sealed record CounterState([property: Id(0)] int Total);

// [GrainType] value must equal GrainTypeNames.Of(typeof(ICounterEntity)) = "counterentity" (the
// leading "I" stripped, then lowercased by NeuronId/EntityId's IdentityPart.Validated) -- the
// phase-3 convention, pinned here first.
[GrainType("counterentity")]
internal sealed class CounterEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CounterState> state)
    : Entity<CounterState>(state), ICounterEntity
{
    public async Task Add(int amount)
        => await SaveAsync(new CounterState((State?.Total ?? 0) + amount));

}
