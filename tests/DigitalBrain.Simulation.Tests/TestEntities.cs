using DigitalBrain.Abstractions.Entities;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrain.Simulation.Tests;

[ClientEntryPoint]
[Alias("test.counter")]
public interface ICounterEntity : IEntity<CounterState>
{
    [Alias(nameof(Add))]
    Task Add(int amount);

    // Test-only probe for the owner wall. OwnerBoundCallFilter only compares owners on an
    // ATTRIBUTED caller -- one whose own SourceId is itself a grain key in "{owner}/{name}"
    // form (see OwnerOf/OwnerBoundCallFilter.cs) -- and an external test client is never
    // attributed (Orleans reports no SourceId for calls originating outside the cluster), so
    // no external-client shape (IDigitalBrain.GetEntity nor a raw Grains.GetGrain call) can
    // ever exercise the wall: [ClientEntryPoint] alone grants it access regardless of target
    // owner. Reaching another owner's entity FROM WITHIN a grain (here, entity-to-entity) is
    // the only call shape that is genuinely attributed, so this method has this entity read a
    // second entity on the CALLER's behalf, letting the inner cross-owner call's
    // NeuronAuthorizationException propagate back out.
    [Alias(nameof(ReachAcrossOwner))]
    Task<CounterState?> ReachAcrossOwner(EntityId other);
}

[GenerateSerializer]
[Alias("test.counter-state")]
public sealed record CounterState([property: Id(0)] int Total);

// [GrainType] value must equal GrainTypeNames.Of(typeof(ICounterEntity)) = "counterentity" (the
// leading "I" stripped, then lowercased by NeuronId/EntityId's IdentityPart.Validated) -- the
// phase-3 convention, pinned here first.
[GrainType("counterentity")]
internal sealed class CounterEntity : Entity<CounterState>, ICounterEntity
{
    public async Task Add(int amount)
        => await SaveAsync(new CounterState((State?.Total ?? 0) + amount));

    public Task<CounterState?> ReachAcrossOwner(EntityId other)
        => GrainFactory.GetGrain<ICounterEntity>(other.ToGrainId()).Read();
}
